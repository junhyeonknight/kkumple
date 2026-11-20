using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Kkumple.Update;

namespace Kkumple.Distribution
{
    // 설치 도우미 자기 업데이트.
    // 시작하자마자 서명된 update.json의 "installer" 항목을 확인하고, 더 새 버전이 있으면
    // 받아서 크기·SHA-256을 확인한 뒤 실행 파일을 교체하고 새 버전으로 다시 시작한다.
    // 인터넷이 없거나 확인에 실패하면 지금 버전으로 그대로 진행한다.
    internal static class SelfUpdate
    {
        private const string OldSuffix = ".old-";
        private const string ConfigEntry = "payload/updater/updater-config.json";
        private const string BodyEntry = "payload/body/꿈플.exe";

        internal sealed class InstallerInfo
        {
            public string Version, Url, Sha256;
            public long Bytes;
        }

        /// <summary>새 버전을 실행했으면 true(현재 프로세스는 종료해야 함).</summary>
        public static bool Run(string[] args)
        {
            CleanupOldCopies();
            if (Has(args, "--no-self-update") || Has(args, "--updated-from")) return false;
            Version current = Normalize(typeof(SelfUpdate).Assembly.GetName().Version);
            string pretend = Value(args, "--self-update-as-version"); // 시험용: 현재 버전을 낮춰 업데이트 경로 확인
            if (pretend != null) current = Protocol.VersionOf(pretend);

            UpdateConfig cfg;
            try { cfg = ReadEmbeddedConfig(false); }
            catch (Exception ex) { SetupTasks.RecordError(ex); return false; }
            if (cfg == null) return false; // 경량판(본체 미포함)은 CAD 자동 업데이트로 교체되므로 제외
            RecordLocation(Application.ExecutablePath);

            string launched = null; Exception failure = null;
            using (var splash = new UpdateSplash())
            {
                var worker = new Thread(delegate()
                {
                    try { launched = Prepare(cfg, current, splash); }
                    catch (Exception ex) { failure = ex; }
                    splash.Finish();
                }) { IsBackground = true, Name = "Kkumple setup self-update" };
                splash.Shown += delegate { worker.Start(); };
                Application.Run(splash);
                if (splash.Skipped) { if (failure != null) SetupTasks.RecordError(failure); return false; }
            }
            if (failure != null) { SetupTasks.RecordError(failure); return false; }
            if (launched == null) return false;
            try
            {
                Process.Start(new ProcessStartInfo(launched, "--updated-from " + current.ToString(3))
                    { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launched) });
                return true;
            }
            catch (Exception ex) { SetupTasks.RecordError(ex); return false; }
        }

        private static string Prepare(UpdateConfig cfg, Version current, UpdateSplash splash)
        {
            splash.SetText("새 버전을 확인하고 있습니다...");
            byte[] envelope = Net.Get(cfg.ManifestUrl, cfg.CheckTimeoutSeconds, 1048576);
            InstallerInfo info = ReadInstallerInfo(envelope, cfg, DateTimeOffset.UtcNow);
            if (info == null || Protocol.VersionOf(info.Version) <= current) return null;

            string cached = EnsureCached(info, cfg, () => splash.SetText("새 설치 도우미 " + info.Version + "을(를) 받고 있습니다..."), n => splash.SetProgress(n, info.Bytes));
            if (splash.Skipped) return null; // 사용자가 건너뛴 뒤에는 실행 파일을 바꾸지 않는다.

            splash.SetText("새 설치 도우미로 바꾸고 있습니다...");
            string self = Application.ExecutablePath;
            string target = cached;
            try
            {
                // 실행 중인 exe는 덮어쓸 수 없지만 이름은 바꿀 수 있다. 옛 파일은 새 버전이 시작할 때 지운다.
                string old = self + OldSuffix + Guid.NewGuid().ToString("N").Substring(0, 8);
                File.Move(self, old);
                try { File.Copy(cached, self); target = self; }
                catch { try { File.Move(old, self); } catch { } }
            }
            catch { /* 쓰기 금지 위치(압축 파일 안 등): 받은 위치에서 실행 */ }
            return target;
        }

        // 받은 설치 도우미를 공용 캐시에 두고 경로를 돌려준다. 이미 같은 파일이 있으면 다시 받지 않는다.
        private static string EnsureCached(InstallerInfo info, UpdateConfig cfg, Action starting, Action<long> progress)
        {
            string cacheDir = SafePath.Under(KkumpleLocal, "Installer/" + info.Version);
            string cached = SafePath.Under(cacheDir, "꿈플-설치.exe");
            if (Matches(cached, info)) return cached;
            if (starting != null) starting();
            Directory.CreateDirectory(cacheDir); SafePath.Check(cacheDir);
            string temp = SafePath.Under(cacheDir, "꿈플-설치.exe." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".download");
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    Net.Transfer(info.Url, Math.Max(cfg.DownloadTimeoutSeconds, 900), info.Bytes, output, progress);
                if (!Matches(temp, info)) throw new InvalidDataException("받은 설치 도우미의 크기 또는 SHA-256이 서명된 정보와 다릅니다.");
                if (Matches(cached, info)) return cached; // 다른 프로세스가 먼저 받음
                if (File.Exists(cached)) File.Delete(cached);
                File.Move(temp, cached);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return cached;
        }

        private static string KkumpleLocal
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kkumple"); }
        }
        private static string LocationsFile { get { return SafePath.Under(KkumpleLocal, "installer-locations.txt"); } }
        private const string InstallerAssemblyName = "꿈플-설치";

        // 사용자가 실행한 전체판 설치 도우미의 위치를 기억해 두면, CAD를 켤 때 그 파일도 최신으로 바꿀 수 있다.
        internal static void RecordLocation(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return;                       // 압축 파일 안에서 바로 실행한 임시 복사본
                if (full.StartsWith(KkumpleLocal + "\\", StringComparison.OrdinalIgnoreCase)) return;          // 자체 캐시
                var list = ReadLocations();
                list.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, full);
                if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
                Directory.CreateDirectory(KkumpleLocal);
                SafePath.Atomic(LocationsFile, new UTF8Encoding(false).GetBytes(string.Join("\r\n", list.ToArray()) + "\r\n"));
            }
            catch (Exception ex) { SetupTasks.RecordError(ex); }
        }
        private static List<string> ReadLocations()
        {
            var list = new List<string>();
            try
            {
                if (File.Exists(LocationsFile))
                    foreach (string line in File.ReadAllLines(LocationsFile, Encoding.UTF8))
                        if (line.Trim().Length > 0 && Regex.IsMatch(line.Trim(), @"^[A-Za-z]:\\")) list.Add(line.Trim());
            }
            catch { }
            return list;
        }

        /// <summary>CAD 시작 시(--refresh-installer) 화면 없이 기억해 둔 설치 도우미 파일을 최신으로 바꾼다.</summary>
        public static int RefreshRecordedInstallers()
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            using (var mutex = new Mutex(false, @"Local\Kkumple-InstallerRefresh"))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) return 0; // 다른 CAD가 이미 확인 중
                    CleanupStaleUpdateFolders();
                    var targets = new List<KeyValuePair<string, Version>>();
                    foreach (string path in ReadLocations())
                    {
                        Version v = InstallerVersionOf(path);
                        if (v != null) targets.Add(new KeyValuePair<string, Version>(path, v));
                    }
                    if (targets.Count == 0) return 0; // 기억된 설치 도우미가 없으면 인터넷도 쓰지 않는다
                    UpdateConfig cfg = ReadEmbeddedConfig(true);
                    if (cfg == null) return 0;
                    InstallerInfo info = ReadInstallerInfo(Net.Get(cfg.ManifestUrl, cfg.CheckTimeoutSeconds, 1048576), cfg, DateTimeOffset.UtcNow);
                    if (info == null) return 0;
                    Version latest = Protocol.VersionOf(info.Version);
                    if (!targets.Exists(t => t.Value < latest)) return 0;
                    string cached = EnsureCached(info, cfg, null, null);
                    foreach (var t in targets)
                    {
                        if (t.Value >= latest) continue;
                        string temp = t.Key + ".kkupd-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                        try
                        {
                            File.Copy(cached, temp, false);
                            File.Replace(temp, t.Key, null); // 설치 도우미가 열려 있으면 실패하고 다음 기회에 다시 시도
                            Log("설치 도우미 갱신: " + t.Key + " " + t.Value.ToString(3) + " → " + info.Version);
                        }
                        catch (Exception ex) { Log("설치 도우미 갱신 보류: " + t.Key + " - " + ex.Message); }
                        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
                    }
                    return 0;
                }
                catch (Exception ex) { SetupTasks.RecordError(ex); return 1; }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }

        // 실패한 업데이트가 남긴 준비 폴더(.kkupd-stage-*)와 오래된 실행 복사본(Shadow)을 정리한다.
        // 업데이트가 진행 중이면(복구 기록이 있으면) 아무것도 지우지 않는다. 이전 버전 백업(.kkupd-backup-*)은 보존한다.
        private static void CleanupStaleUpdateFolders()
        {
            try
            {
                string versions = Path.Combine(GateInstaller.DefaultBundle, "Contents", "Versions");
                string journal = Path.Combine(GateInstaller.DefaultData, "active-update.json");
                if (Directory.Exists(versions) && !File.Exists(journal))
                    foreach (string dir in Directory.GetDirectories(versions, ".kkupd-stage-*"))
                    {
                        try
                        {
                            if (!Regex.IsMatch(Path.GetFileName(dir), @"^\.kkupd-stage-[a-f0-9]{32}$")) continue;
                            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) < TimeSpan.FromMinutes(30)) continue;
                            SafePath.Check(dir);
                            Directory.Delete(dir, true);
                            Log("남은 업데이트 준비 폴더 정리: " + Path.GetFileName(dir));
                        }
                        catch (Exception ex) { Log("준비 폴더 정리 보류: " + Path.GetFileName(dir) + " - " + ex.Message); }
                    }
                string shadow = Path.Combine(KkumpleLocal, "Shadow");
                if (Directory.Exists(shadow))
                    foreach (string kind in Directory.GetDirectories(shadow))
                        foreach (string dir in Directory.GetDirectories(kind))
                            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > TimeSpan.FromDays(14))
                                try { SafePath.Check(dir); Directory.Delete(dir, true); } catch { /* 사용 중인 복사본은 다음에 */ }
            }
            catch (Exception ex) { Log("정리 실패: " + ex.Message); }
        }

        // 꿈플 전체판 설치 도우미(어셈블리 이름 '꿈플-설치')일 때만 버전을 돌려준다.
        private static Version InstallerVersionOf(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                if (!string.Equals(System.Reflection.AssemblyName.GetAssemblyName(path).Name, InstallerAssemblyName, StringComparison.Ordinal)) return null;
                Version v;
                return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out v) ? Normalize(v) : null;
            }
            catch { return null; }
        }

        private static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(KkumpleLocal);
                File.AppendAllText(SafePath.Under(KkumpleLocal, "installer-refresh.log"), DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        internal static InstallerInfo ReadInstallerInfo(byte[] envelope, UpdateConfig cfg, DateTimeOffset now)
        {
            Protocol.Verify(envelope, cfg, now); // 전자서명·제품·유효기간 확인
            var signed = JsonXml.Read<SignedEnvelope>(envelope);
            var payload = JsonXml.Read<Dictionary<string, object>>(Encoding.UTF8.GetBytes(signed.payload));
            object raw;
            if (payload == null || !payload.TryGetValue("installer", out raw) || raw == null) return null;
            var map = raw as Dictionary<string, object>;
            if (map == null) throw new InvalidDataException("설치 도우미 정보 형식이 올바르지 않습니다.");
            var info = new InstallerInfo
            {
                Version = Convert.ToString(Get(map, "version")),
                Url = Convert.ToString(Get(map, "url")),
                Sha256 = Convert.ToString(Get(map, "sha256")),
                Bytes = Convert.ToInt64(Get(map, "bytes")),
            };
            Protocol.VersionOf(info.Version);
            Net.RequireHttps(info.Url);
            if (!Regex.IsMatch(info.Sha256 ?? "", "^[a-fA-F0-9]{64}$") || info.Bytes < 1 || info.Bytes > cfg.MaxPackageBytes)
                throw new InvalidDataException("설치 도우미 정보가 올바르지 않습니다.");
            info.Sha256 = info.Sha256.ToLowerInvariant();
            return info;
        }

        private static UpdateConfig ReadEmbeddedConfig(bool allowCompact)
        {
            using (Stream stream = typeof(SelfUpdate).Assembly.GetManifestResourceStream(EmbeddedPayload.ResourceName))
            {
                if (stream == null) return null;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    if (!allowCompact && zip.GetEntry(BodyEntry) == null) return null;
                    var entry = zip.GetEntry(ConfigEntry);
                    if (entry == null || entry.Length > 65536) return null;
                    using (var s = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        var cfg = JsonXml.Read<UpdateConfig>(ms.ToArray());
                        cfg.Validate();
                        return cfg;
                    }
                }
            }
        }

        private static void CleanupOldCopies()
        {
            var t = new Thread(delegate()
            {
                try
                {
                    string self = Application.ExecutablePath;
                    string dir = Path.GetDirectoryName(self), name = Path.GetFileName(self);
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        bool left = false;
                        foreach (string f in Directory.GetFiles(dir, name + OldSuffix + "*"))
                            try { File.Delete(f); } catch { left = true; }
                        if (!left) break;
                        Thread.Sleep(1000);
                    }
                    string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kkumple", "Installer");
                    Version mine = Normalize(typeof(SelfUpdate).Assembly.GetName().Version);
                    if (Directory.Exists(cache))
                        foreach (string d in Directory.GetDirectories(cache))
                        {
                            Version v;
                            if (Version.TryParse(Path.GetFileName(d), out v) && Normalize(v) < mine
                                && !self.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))
                                try { SafePath.Check(d); Directory.Delete(d, true); } catch { }
                        }
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
        }

        private static bool Matches(string file, InstallerInfo info)
        {
            return File.Exists(file) && new FileInfo(file).Length == info.Bytes && Protocol.FileSha(file) == info.Sha256;
        }
        private static object Get(Dictionary<string, object> map, string key)
        {
            object v; if (!map.TryGetValue(key, out v) || v == null) throw new InvalidDataException("설치 도우미 정보에 " + key + " 항목이 없습니다.");
            return v;
        }
        private static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0)); }
        private static bool Has(string[] args, string key) { return Array.IndexOf(args ?? new string[0], key) >= 0; }
        private static string Value(string[] args, string key)
        {
            int i = Array.IndexOf(args ?? new string[0], key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }

    internal sealed class UpdateSplash : Form
    {
        private readonly Label text = new Label { AutoSize = false, Text = "새 버전을 확인하고 있습니다..." };
        private readonly ProgressBar bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        private readonly Button skip = new Button { Text = "건너뛰기" };
        public bool Skipped { get; private set; }
        private bool finished;

        public UpdateSplash()
        {
            Text = "꿈플 설치 도우미"; Font = new Font("Malgun Gothic", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(420, 130);
            text.SetBounds(20, 18, 380, 24); bar.SetBounds(20, 50, 380, 20); skip.SetBounds(300, 84, 100, 30);
            skip.Click += delegate { Skipped = true; Close(); };
            CancelButton = skip;
            FormClosing += delegate { if (!finished) Skipped = true; };
            Controls.AddRange(new Control[] { text, bar, skip });
        }
        public void SetText(string value) { Ui(() => text.Text = value); }
        public void SetProgress(long done, long total)
        {
            Ui(() =>
            {
                if (bar.Style != ProgressBarStyle.Continuous) { bar.Style = ProgressBarStyle.Continuous; bar.Maximum = 1000; }
                bar.Value = (int)Math.Max(0, Math.Min(1000, done * 1000 / Math.Max(1, total)));
            });
        }
        public void Finish() { Ui(() => { finished = true; Close(); }); }
        private void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { /* 창이 이미 닫힘(ObjectDisposedException 포함) */ }
        }
    }
}
