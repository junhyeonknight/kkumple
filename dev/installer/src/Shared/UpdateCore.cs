using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace Kkumple.Update
{
    internal sealed class UpdateConfig
    {
        public string Service { get; set; }
        public string AppId { get; set; }
        public string ManifestUrl { get; set; }
        public string ServiceEndStatusUrl { get; set; }
        public string PublicKeyXml { get; set; }
        public bool RequireOnlineCheck { get; set; }
        public int CheckTimeoutSeconds { get; set; }
        public int DownloadTimeoutSeconds { get; set; }
        public long MaxPackageBytes { get; set; }
        public long MaxExpandedBytes { get; set; }
        public int MaxEntries { get; set; }
        public UpdateConfig()
        {
            RequireOnlineCheck = true; CheckTimeoutSeconds = 8; DownloadTimeoutSeconds = 180;
            MaxPackageBytes = 512L * 1024 * 1024; MaxExpandedBytes = 1024L * 1024 * 1024; MaxEntries = 10000;
        }
        public void Validate()
        {
            if (AppId != "KKUMPLE_FREE_NONCOMMERCIAL_V2") throw new InvalidDataException("잘못된 제품 ID입니다.");
            if (string.IsNullOrWhiteSpace(Service)) throw new InvalidDataException("서비스 이름이 비어 있습니다.");
            Net.RequireHttps(ManifestUrl);
            Net.RequireHttps(ServiceEndStatusUrl);
            if (CheckTimeoutSeconds < 1 || CheckTimeoutSeconds > 60 || DownloadTimeoutSeconds < 1 || DownloadTimeoutSeconds > 1800
                || MaxPackageBytes < 1 || MaxPackageBytes > 1024L * 1024 * 1024 || MaxExpandedBytes < 1
                || MaxExpandedBytes > 4L * 1024 * 1024 * 1024 || MaxEntries < 1 || MaxEntries > 50000)
                throw new InvalidDataException("업데이트 제한 설정이 잘못되었습니다.");
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(PublicKeyXml);
                if (!rsa.PublicOnly || rsa.KeySize < 2048) throw new InvalidDataException("RSA-2048 이상의 공개키가 필요합니다.");
            }
        }
    }
    internal sealed class SignedEnvelope { public string payload { get; set; } public string signature { get; set; } }
    internal sealed class UpdateManifest
    {
        public int schema { get; set; }
        public string appId { get; set; }
        public string version { get; set; }
        public string issuedAtUtc { get; set; }
        public string expiresAtUtc { get; set; }
        public string releaseNotes { get; set; }
        public string updaterSha256 { get; set; }
        public List<UpdatePackage> packages { get; set; }
    }
    internal sealed class UpdatePackage
    {
        public string family { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public long bytes { get; set; }
    }
    internal sealed class BundleInfo
    {
        public XmlDocument Xml;
        public Version Version;
        public string Family;
        public byte[] XmlBytes;
        public static void LoadRootMarkerOnly(string bundle)
        {
            SafePath.Check(bundle);
            string marker = Path.Combine(bundle, ".kkumple-owner"); SafePath.Check(marker);
            if (!File.Exists(marker) || File.ReadAllText(marker, Encoding.UTF8).IndexOf("KKUMPLE_FREE_NONCOMMERCIAL_V2", StringComparison.Ordinal) < 0)
                throw new InvalidDataException("꿈플 전용 폴더의 소유자 표식을 확인하지 못했습니다.");
        }
        public static BundleInfo Load(string bundle)
        {
            LoadRootMarkerOnly(bundle);
            string file = Path.Combine(bundle, "PackageContents.xml"); SafePath.Check(file);
            var info = new BundleInfo(); info.XmlBytes = File.ReadAllBytes(file); info.Xml = JsonXml.ReadXml(info.XmlBytes);
            info.Version = Protocol.VersionOf(info.Xml.DocumentElement.GetAttribute("AppVersion"));
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement runtime in info.Xml.SelectNodes("//RuntimeRequirements[@SupportPath]"))
            {
                string support = runtime.GetAttribute("SupportPath").Replace('\\', '/');
                var m = Regex.Match(support, @"^(?:\./)?Contents/Versions/(2010|2013|2015|2025)/?$", RegexOptions.IgnoreCase);
                if (!m.Success) throw new InvalidDataException("지원하지 않는 꿈플 모듈 경로입니다: " + support);
                families.Add(m.Groups[1].Value);
            }
            if (families.Count != 1) throw new InvalidDataException("이 PC에 연결된 CAD 계열을 하나로 확인하지 못했습니다.");
            foreach (string family in families) info.Family = family;
            string current = Path.Combine(bundle, "Contents", "Versions", info.Family); SafePath.Check(current);
            if (!Directory.Exists(current)) throw new DirectoryNotFoundException("설치된 꿈플 모듈이 없습니다.");
            return info;
        }
    }
    internal static class JsonXml
    {
        public static T Read<T>(byte[] bytes) { return new JavaScriptSerializer { MaxJsonLength = 2097152 }.Deserialize<T>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')); }
        public static byte[] Bytes(object value) { return Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 2097152 }.Serialize(value)); }
        public static XmlDocument ReadXml(byte[] bytes)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var ms = new MemoryStream(bytes))
            using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
            return doc;
        }
        public static byte[] XmlBytes(XmlDocument doc)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = "\r\n" })) doc.Save(writer);
                return ms.ToArray();
            }
        }
    }
    internal static class SafePath
    {
        public static string Full(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"^[A-Za-z]:[\\/]")) throw new IOException("로컬 드라이브의 절대 경로가 필요합니다.");
            string full = Path.GetFullPath(path);
            if (full.IndexOf(':', 2) >= 0 || full.IndexOfAny(new[] { '*', '?' }) >= 0) throw new IOException("허용하지 않는 경로입니다.");
            foreach (string part in path.Replace('/', '\\').Substring(3).Split('\\'))
                if ((part.EndsWith(".", StringComparison.Ordinal) && part != "." && part != "..") || part.EndsWith(" ", StringComparison.Ordinal)) throw new IOException("모호한 경로입니다.");
            return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd('\\');
        }
        public static string Under(string root, string relative)
        {
            root = Full(root);
            if (string.IsNullOrEmpty(relative)) throw new IOException("빈 상대 경로입니다.");
            string name = relative.Replace('\\', '/');
            foreach (string part in name.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase))
                    throw new IOException("허용하지 않는 파일 경로: " + relative);
            string result = Path.GetFullPath(Path.Combine(root, name.Replace('/', '\\')));
            if (!result.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("폴더 밖 경로입니다.");
            Check(result); return result;
        }
        public static void Check(string path)
        {
            for (string p = Full(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            {
                try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결 폴더/심볼릭 링크를 사용할 수 없습니다: " + p); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
        public static void Atomic(string file, byte[] bytes)
        {
            Check(file); string parent = Path.GetDirectoryName(file); Directory.CreateDirectory(parent); Check(parent);
            string temp = Under(parent, ".kkupd-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temp, bytes);
            try { Check(file); if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file); }
            finally { if (File.Exists(temp)) { Check(temp); File.Delete(temp); } }
        }
        public static void Move(string root, string from, string to)
        {
            root = Full(root); from = Full(from); to = Full(to);
            if (!from.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || !to.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("이동 경로가 꿈플 버전 폴더 밖입니다.");
            Check(from); Check(to); Directory.Move(from, to);
        }
        public static bool Protected(string file)
        {
            return new HashSet<string>(new[] { ".dwg", ".dxf", ".dwt", ".dws", ".bak", ".ini", ".cfg", ".settings" }, StringComparer.OrdinalIgnoreCase).Contains(Path.GetExtension(file));
        }
    }
    internal static class Protocol
    {
        public static Version VersionOf(string text)
        {
            Version version;
            if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, @"^\d+\.\d+(?:\.\d+){0,2}$") || !Version.TryParse(text, out version))
                throw new InvalidDataException("버전 형식 오류: " + text);
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        }
        public static string Sha(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string FileSha(string file) { using (var sha = SHA256.Create()) using (var s = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        private static string VerifiedPayload(byte[] envelopeBytes, UpdateConfig cfg)
        {
            if (envelopeBytes.Length > 1048576) throw new InvalidDataException("업데이트 안내가 너무 큽니다.");
            var envelope = JsonXml.Read<SignedEnvelope>(envelopeBytes);
            if (envelope == null || string.IsNullOrEmpty(envelope.payload) || string.IsNullOrEmpty(envelope.signature)) throw new InvalidDataException("서명된 업데이트 안내가 아닙니다.");
            using (var rsa = new RSACryptoServiceProvider())
            using (var sha = SHA256.Create())
            {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(cfg.PublicKeyXml);
                var verifier = new RSAPKCS1SignatureDeformatter(rsa); verifier.SetHashAlgorithm("SHA256");
                if (!rsa.PublicOnly || rsa.KeySize < 2048 || !verifier.VerifySignature(sha.ComputeHash(Encoding.UTF8.GetBytes(envelope.payload)), Convert.FromBase64String(envelope.signature)))
                    throw new InvalidDataException("업데이트 전자서명이 일치하지 않습니다.");
            }
            return envelope.payload;
        }
        public static bool VerifyEnded(byte[] envelopeBytes, UpdateConfig cfg)
        {
            string payload = VerifiedPayload(envelopeBytes, cfg);
            var status = JsonXml.Read<Dictionary<string, object>>(Encoding.UTF8.GetBytes(payload));
            if (status == null || !status.ContainsKey("service") || !(status["service"] is string)
                || !status.ContainsKey("ended") || !(status["ended"] is bool)
                || !string.Equals(((string)status["service"]).Trim().Normalize(NormalizationForm.FormC),
                    (cfg.Service ?? "").Trim().Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
                throw new InvalidDataException("서비스 종료 상태의 제품 이름/형식이 다릅니다.");
            if (!(bool)status["ended"]) return false;
            if (status.ContainsKey("effectiveDateIso") && !string.IsNullOrEmpty(Convert.ToString(status["effectiveDateIso"], CultureInfo.InvariantCulture)))
            {
                DateTime effective;
                if (!DateTime.TryParseExact(Convert.ToString(status["effectiveDateIso"], CultureInfo.InvariantCulture), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out effective)) throw new InvalidDataException("서비스 종료 적용일 형식 오류입니다.");
                if (DateTime.Now.Date < effective.Date) return false;
            }
            return true;
        }
        public static UpdateManifest Verify(byte[] envelopeBytes, UpdateConfig cfg, DateTimeOffset now, bool allowExpired = false)
        {
            string payload = VerifiedPayload(envelopeBytes, cfg);
            var manifest = JsonXml.Read<UpdateManifest>(Encoding.UTF8.GetBytes(payload));
            DateTimeOffset issued, expires;
            if (manifest == null || manifest.schema != 1 || manifest.appId != cfg.AppId
                || !DateTimeOffset.TryParse(manifest.issuedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out issued)
                || !DateTimeOffset.TryParse(manifest.expiresAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out expires)
                || issued > now.AddMinutes(10) || (!allowExpired && expires <= now) || expires <= issued) throw new InvalidDataException("제품 또는 유효기간이 올바르지 않습니다.");
            VersionOf(manifest.version);
            if (manifest.packages == null) manifest.packages = new List<UpdatePackage>();
            var families = new HashSet<string>();
            foreach (var package in manifest.packages)
            {
                if (package == null || !Regex.IsMatch(package.family ?? "", @"^(2010|2013|2015|2025)$") || !families.Add(package.family)
                    || !Regex.IsMatch(package.sha256 ?? "", @"^[a-fA-F0-9]{64}$") || package.bytes < 1 || package.bytes > cfg.MaxPackageBytes)
                    throw new InvalidDataException("업데이트 패키지 정보가 잘못되었습니다.");
                Net.RequireHttps(package.url);
            }
            return manifest;
        }
    }
    internal static class Net
    {
        public static void RequireHttps(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("HTTPS 주소만 사용할 수 있습니다.");
        }
        public static byte[] Get(string url, int seconds, long limit)
        {
            using (var ms = new MemoryStream()) { Transfer(url, seconds, limit, ms, null); return ms.ToArray(); }
        }
        public static void Transfer(string url, int seconds, long limit, Stream output, Action<long> progress)
        {
            var timer = Stopwatch.StartNew();
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                RequireHttps(url);
                int remainingMs = seconds * 1000 - (int)timer.ElapsedMilliseconds;
                if (remainingMs <= 0) throw new IOException("다운로드 제한 시간을 초과했습니다.");
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET"; request.Timeout = remainingMs; request.ReadWriteTimeout = remainingMs;
                request.AllowAutoRedirect = false; request.UserAgent = "KkumpleUpdater/1.0";
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (var deadline = new System.Threading.Timer(ignored => request.Abort(), null, remainingMs, System.Threading.Timeout.Infinite))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400)
                    {
                        if (redirects == 5 || string.IsNullOrEmpty(response.Headers["Location"])) throw new IOException("다운로드 리디렉션 오류입니다.");
                        url = new Uri(new Uri(url), response.Headers["Location"]).AbsoluteUri; continue;
                    }
                    if (status != 200 || response.ContentLength > limit) throw new IOException("다운로드 상태/크기가 올바르지 않습니다.");
                    using (var stream = response.GetResponseStream())
                    {
                        byte[] buffer = new byte[65536]; long total = 0; int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            total += count;
                            if (total > limit || timer.Elapsed.TotalSeconds > seconds) throw new IOException("다운로드 제한 시간 또는 크기를 초과했습니다.");
                            output.Write(buffer, 0, count); if (progress != null) progress(total);
                        }
                    }
                    return;
                }
            }
            throw new IOException("다운로드 리디렉션 오류입니다.");
        }
    }
    internal sealed class UpdateJournal
    {
        public string id { get; set; }
        public string family { get; set; }
        public string oldXml { get; set; }
        public string oldInstallState { get; set; }
        public bool committed { get; set; }
    }
    internal sealed class UpdateEngine
    {
        private readonly string bundle, data;
        private readonly UpdateConfig cfg;
        internal Func<string, int, long, byte[]> FetchMetadata;
        internal Action<string, int, long, Stream, Action<long>> Download;
        internal Action<string> TestCheckpoint;
        internal Action VerifyServiceActive;
        public UpdateEngine(string bundleDirectory, string dataDirectory, UpdateConfig config)
        {
            bundle = SafePath.Full(bundleDirectory); data = SafePath.Full(dataDirectory); cfg = config;
            FetchMetadata = Net.Get; Download = Net.Transfer;
        }
        private string JournalPath { get { return SafePath.Under(data, "active-update.json"); } }
        private string CachePath { get { return SafePath.Under(data, "last-verified-update.json"); } }
        private void Checkpoint(string value) { if (TestCheckpoint != null) TestCheckpoint(value); }
        public UpdateManifest Check()
        {
            Recover(); var local = BundleInfo.Load(bundle); byte[] bytes;
            try { bytes = FetchMetadata(cfg.ManifestUrl, cfg.CheckTimeoutSeconds, 1048576); }
            catch
            {
                if (cfg.RequireOnlineCheck) throw;
                if (!File.Exists(CachePath)) return null;
                var cached = Protocol.Verify(File.ReadAllBytes(CachePath), cfg, DateTimeOffset.UtcNow);
                if (Protocol.VersionOf(cached.version) > local.Version) throw new IOException("필수 업데이트를 완료해야 꿈플을 사용할 수 있습니다.");
                return null;
            }
            var manifest = Protocol.Verify(bytes, cfg, DateTimeOffset.UtcNow);
            if (File.Exists(CachePath))
            {
                var previous = Protocol.Verify(File.ReadAllBytes(CachePath), cfg, DateTimeOffset.UtcNow, true);
                if (Protocol.VersionOf(manifest.version) < Protocol.VersionOf(previous.version)) throw new InvalidDataException("이전 버전의 업데이트 안내가 수신되었습니다.");
            }
            SafePath.Atomic(CachePath, bytes);
            return Protocol.VersionOf(manifest.version) > local.Version ? manifest : null;
        }
        public void Apply(UpdateManifest manifest, Action<string> progress)
        {
            Recover(); var local = BundleInfo.Load(bundle);
            if (Protocol.VersionOf(manifest.version) <= local.Version) return;
            UpdatePackage package = manifest.packages.Find(p => p.family == local.Family);
            if (package == null) throw new InvalidDataException("이 CAD 계열의 필수 업데이트 파일이 아직 준비되지 않았습니다.");
            string zip = SafePath.Under(data, "package-" + Guid.NewGuid().ToString("N") + ".zip");
            Directory.CreateDirectory(data); SafePath.Check(data);
            try
            {
                if (progress != null) progress("꿈플 " + manifest.version + " 다운로드 중...");
                using (var stream = new FileStream(zip, FileMode.CreateNew, FileAccess.Write))
                    Download(package.url, cfg.DownloadTimeoutSeconds, package.bytes, stream, n => { if (progress != null) progress("업데이트 다운로드 " + n / 1024 + " / " + package.bytes / 1024 + " KB"); });
                if (new FileInfo(zip).Length != package.bytes || !string.Equals(Protocol.FileSha(zip), package.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("업데이트 파일의 크기 또는 SHA-256이 일치하지 않습니다.");
                InstallZip(zip, manifest.version, progress);
            }
            finally { if (File.Exists(zip)) { SafePath.Check(zip); File.Delete(zip); } }
        }
        internal void InstallZip(string zip, string version, Action<string> progress)
        {
            if (VerifyServiceActive != null) VerifyServiceActive();
            var local = BundleInfo.Load(bundle); Protocol.VersionOf(version);
            string versions = SafePath.Under(bundle, "Contents/Versions");
            string current = SafePath.Under(versions, local.Family);
            string id = Guid.NewGuid().ToString("N");
            string stage = SafePath.Under(versions, ".kkupd-stage-" + id);
            string backup = SafePath.Under(versions, ".kkupd-backup-" + id);
            Directory.CreateDirectory(stage);
            if (progress != null) progress("기존 파일을 보존하며 업데이트를 준비합니다...");
            CopyTree(current, stage, 0);
            Extract(zip, stage);
            string tag = ".kkumple-update-transaction";
            SafePath.Atomic(SafePath.Under(stage, tag), Encoding.ASCII.GetBytes(id));
            PreflightLocks(current, 0);
            if (VerifyServiceActive != null) VerifyServiceActive();
            string installState = SafePath.Under(data, "install-state.json");
            var journal = new UpdateJournal { id = id, family = local.Family,
                oldXml = Convert.ToBase64String(local.XmlBytes), oldInstallState = File.Exists(installState) ? Convert.ToBase64String(File.ReadAllBytes(installState)) : null };
            SafePath.Atomic(JournalPath, JsonXml.Bytes(journal));
            try
            {
                SafePath.Atomic(SafePath.Under(current, tag), Encoding.ASCII.GetBytes(id));
                Checkpoint("before-swap"); SafePath.Move(versions, current, backup);
                Checkpoint("after-backup"); SafePath.Move(versions, stage, current);
                Checkpoint("after-swap");
                local.Xml.DocumentElement.SetAttribute("AppVersion", version);
                byte[] newXml = JsonXml.XmlBytes(local.Xml);
                SafePath.Atomic(SafePath.Under(bundle, "PackageContents.xml"), newXml);
                GateInstaller.UpdateStateHash(data, Protocol.Sha(newXml));
                Checkpoint("after-version"); journal.committed = true;
                SafePath.Atomic(JournalPath, JsonXml.Bytes(journal));
                DeleteTag(current, id); File.Delete(JournalPath);
                if (progress != null) progress("업데이트 완료. 꿈플을 불러옵니다.");
            }
            catch { Recover(); throw; }
        }
        public void Recover()
        {
            SafePath.Check(data);
            if (!File.Exists(JournalPath)) return;
            var journal = JsonXml.Read<UpdateJournal>(File.ReadAllBytes(JournalPath));
            if (journal == null || !Regex.IsMatch(journal.id ?? "", @"^[a-f0-9]{32}$") || !Regex.IsMatch(journal.family ?? "", @"^(2010|2013|2015|2025)$"))
                throw new InvalidDataException("복구 기록을 확인하지 못했습니다.");
            BundleInfo.LoadRootMarkerOnly(bundle);
            string versions = SafePath.Under(bundle, "Contents/Versions");
            string current = SafePath.Under(versions, journal.family), backup = SafePath.Under(versions, ".kkupd-backup-" + journal.id);
            if (journal.committed) { DeleteTag(current, journal.id); File.Delete(JournalPath); return; }
            if (Directory.Exists(backup))
            {
                CheckTag(backup, journal.id);
                if (Directory.Exists(current))
                {
                    CheckTag(current, journal.id);
                    SafePath.Move(versions, current, SafePath.Under(versions, ".kkupd-failed-" + Guid.NewGuid().ToString("N")));
                }
                SafePath.Move(versions, backup, current);
            }
            else if (!Directory.Exists(current)) throw new IOException("복구할 원래 모듈 폴더가 없습니다.");
            DeleteTag(current, journal.id);
            SafePath.Atomic(SafePath.Under(bundle, "PackageContents.xml"), Convert.FromBase64String(journal.oldXml));
            if (journal.oldInstallState != null) SafePath.Atomic(SafePath.Under(data, "install-state.json"), Convert.FromBase64String(journal.oldInstallState));
            File.Delete(JournalPath);
        }
        private static void CheckTag(string dir, string id)
        {
            string file = SafePath.Under(dir, ".kkumple-update-transaction");
            if (!File.Exists(file) || File.ReadAllText(file, Encoding.ASCII) != id) throw new IOException("복구 폴더의 표식이 다릅니다. 파일은 보존했습니다.");
        }
        private static void DeleteTag(string dir, string id)
        {
            string file = SafePath.Under(dir, ".kkumple-update-transaction");
            if (File.Exists(file)) { CheckTag(dir, id); File.Delete(file); }
        }
        private static void CopyTree(string source, string destination, int depth)
        {
            if (depth > 64) throw new IOException("폴더 깊이가 너무 큽니다.");
            SafePath.Check(source); SafePath.Check(destination);
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                SafePath.Check(entry); string target = SafePath.Under(destination, Path.GetFileName(entry));
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) { Directory.CreateDirectory(target); CopyTree(entry, target, depth + 1); }
                else File.Copy(entry, target, false);
            }
        }
        private static void PreflightLocks(string dir, int depth)
        {
            if (depth > 64) throw new IOException("폴더 깊이가 너무 큽니다.");
            SafePath.Check(dir);
            foreach (string entry in Directory.GetFileSystemEntries(dir))
            {
                SafePath.Check(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) PreflightLocks(entry, depth + 1);
                else
                {
                    try { using (File.Open(entry, FileMode.Open, FileAccess.Read, FileShare.None)) { } }
                    catch (IOException ex) { throw new IOException("꿈플 파일을 사용 중인 다른 AutoCAD를 종료한 뒤 다시 시도해 주세요: " + Path.GetFileName(entry), ex); }
                }
            }
        }
        private void Extract(string zip, string destination)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; int count = 0;
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > cfg.MaxEntries) throw new InvalidDataException("업데이트 ZIP 항목 수가 올바르지 않습니다.");
                foreach (var entry in archive.Entries)
                {
                    if (++count > cfg.MaxEntries || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("ZIP 연결 파일/항목 제한 오류입니다.");
                    bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    string relative = directory ? entry.FullName.Substring(0, entry.FullName.Length - 1) : entry.FullName;
                    string target = SafePath.Under(destination, relative);
                    if (!names.Add(relative.Replace('\\', '/'))) throw new InvalidDataException("ZIP 파일 이름이 중복됩니다.");
                    if (directory) { if (entry.Length != 0) throw new InvalidDataException("잘못된 폴더 항목입니다."); Directory.CreateDirectory(target); continue; }
                    if (SafePath.Protected(target) || Path.GetFileName(target) == ".kkumple-update-transaction") throw new InvalidDataException("보호 파일은 업데이트할 수 없습니다: " + relative);
                    expanded = checked(expanded + entry.Length);
                    if (expanded > cfg.MaxExpandedBytes) throw new InvalidDataException("압축 해제 크기 제한을 초과했습니다.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); SafePath.Check(target);
                    if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[65536]; long actual = 0; int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                        { actual += n; if (actual > entry.Length || actual > cfg.MaxExpandedBytes) throw new InvalidDataException("ZIP 실제 크기 제한 오류입니다."); output.Write(buffer, 0, n); }
                        if (actual != entry.Length) throw new InvalidDataException("ZIP 파일 크기가 일치하지 않습니다.");
                    }
                }
            }
        }
    }
}
