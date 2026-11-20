using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

// 꿈플 "서비스 종료" 에이전트 (비파괴·투명형)
// --------------------------------------------------------------------------
// - 서명된 종료 신호: 안내 표시 → 꿈플 전용 파일 정리 → 성공한 경우 예약 작업 해제
// - 사용자 도면(.dwg 등)·개인 데이터·AutoCAD 본체는 코드로 강제하여 절대 삭제하지 않음
// - 기본값 DryRun=true: 실제 삭제 없이 "삭제 예정" 목록만 로그로 남김(먼저 반드시 이 상태로 검증)
// - 꿈플 바이너리는 수정하지 않음(역분석/패치 없음). 이 프로그램은 독립 실행 동반 프로그램임.
namespace KkumpleServiceEnd
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Logger log = null;
            try
            {
                string cfgPath = ResolveConfigPath(args);
                Config cfg = Config.Load(cfgPath);
                if (Array.IndexOf(args, "--notice") >= 0)
                {
                    MessageBox.Show(cfg.NoticeMessage, cfg.NoticeTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                log = new Logger(cfg.ResolveLogPath(cfgPath));
                log.Info("=== 꿈플 서비스 종료 에이전트 시작 ===");
                log.Info("설정 파일: " + cfgPath);
                log.Info("DryRun=" + cfg.DryRun + ", 만료일=" + cfg.ExpiryDateIso + ", StatusUrl=" + cfg.StatusUrl);

                if (!cfg.ShouldActivate(log))
                {
                    log.Info("활성화 조건 아님(원격 종료 신호 없음/만료 전). 아무 작업도 하지 않고 종료합니다.");
                    return 0;
                }

                using (var operation = new BundleOperationLock(cfg.PathsToRemove[0], 120000))
                {
                    log.Info("서비스 종료 조건 충족. 종료 절차를 진행합니다.");
                    if (!Notify(cfg, cfgPath, log))
                    {
                        log.Info("사용자가 취소했습니다(consent 모드). 제거를 진행하지 않습니다.");
                        return 0;
                    }
    
                    // 1) 꿈플 자체 정리 명령이 설정돼 있으면 먼저 호출(가장 안전한 정리 방법)
                    RunPreUninstall(cfg, log);
    
                    // 2) 명시된 "꿈플 전용" 경로만 제거(allow-list + 다중 가드)
                    RemovalGuard guard = new RemovalGuard(cfg.AllowedBaseDirs, cfg.ProtectedExtensions,
                        cfg.RequireOwnerMarker, cfg.OwnerMarkerFile, cfg.OwnerMarkerMustContain, log);
                    bool complete = cfg.PathsToRemove.Count > 0;
                    foreach (string p in cfg.PathsToRemove)
                        complete = guard.Remove(p, cfg.DryRun) && complete;
    
                    // 잠긴 파일/거부된 경로가 있으면 표식과 예약 작업을 남겨 다음 점검에서 재시도.
                    if (!complete)
                    {
                        log.Warn("정리 미완료. 예약 작업과 소유자 표식을 보존하여 다음 실행에서 재시도합니다.");
                        return 2;
                    }
                    if (!FinishTask(cfg, log)) return 3;
    
                    log.Info("=== 완료 ===");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                if (log != null) log.Error("오류: " + ex);
                // 예약 작업의 오류가 대화상자에서 무한 대기하지 않게 로그와 종료코드로 보고.
                if (log == null) Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static string ResolveConfigPath(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--config") return Path.GetFullPath(args[i + 1]);
            // 기본값: 실행 파일 옆의 config.json
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "config.json");
        }

        // 반환 false = (consent 모드에서) 사용자가 취소함 → 제거 중단
        private static bool Notify(Config cfg, string cfgPath, Logger log)
        {
            string mode = string.IsNullOrWhiteSpace(cfg.NoticeMode) ? "notify-async" : cfg.NoticeMode.Trim().ToLowerInvariant();
            log.Info("안내 모드: " + mode);
            if (cfg.DryRun) { log.Info("[dry-run] 안내창 생략, 로그만."); return true; }

            if (mode == "silent") return true;

            if (mode == "consent")
            {
                DialogResult r = MessageBox.Show(
                    cfg.NoticeMessage + "\n\n[확인]을 누르면 꿈플 구성요소가 정리됩니다.\n사용자 도면/개인 데이터는 삭제되지 않습니다.",
                    cfg.NoticeTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                return r == DialogResult.OK;
            }

            if (mode == "notify-async")
            {
                // 별도 프로세스로 표시하여 정리 프로세스 종료 후에도 안내가 유지됨.
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                        "--notice --config \"" + cfgPath + "\"") { UseShellExecute = false });
                }
                catch (Exception ex) { log.Warn("종료 안내 표시 실패: " + ex.Message); }
                return true;
            }

            // 기본: notify (모달 안내 후 진행)
            MessageBox.Show(cfg.NoticeMessage, cfg.NoticeTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private static void RunPreUninstall(Config cfg, Logger log)
        {
            if (string.IsNullOrWhiteSpace(cfg.PreUninstallExe))
            {
                log.Info("PreUninstallExe 미설정 — 꿈플 자체 정리 명령은 건너뜁니다.");
                return;
            }
            string exe = Path.GetFullPath(cfg.PreUninstallExe);
            if (!File.Exists(exe))
            {
                log.Warn("PreUninstallExe 를 찾을 수 없습니다: " + exe);
                return;
            }
            log.Info("꿈플 자체 정리 명령 실행: \"" + exe + "\" " + cfg.PreUninstallArgs);
            if (cfg.DryRun) { log.Info("[dry-run] 실제 실행하지 않음."); return; }
            try
            {
                var psi = new ProcessStartInfo(exe, cfg.PreUninstallArgs ?? "")
                { UseShellExecute = false, CreateNoWindow = false };
                Process p = Process.Start(psi);
                if (p != null && p.WaitForExit(cfg.PreUninstallTimeoutSec * 1000))
                    log.Info("정리 명령 종료코드: " + p.ExitCode);
                else
                    log.Warn("정리 명령이 시간 내에 끝나지 않았습니다.");
            }
            catch (Exception ex) { log.Warn("정리 명령 실행 실패: " + ex.Message); }
        }

        private static bool FinishTask(Config cfg, Logger log)
        {
            // 예약 작업 제거
            if (!string.IsNullOrWhiteSpace(cfg.ScheduledTaskName))
            {
                log.Info("예약 작업 제거: " + cfg.ScheduledTaskName);
                if (cfg.DryRun) log.Info("[dry-run] schtasks 삭제 생략.");
                else
                {
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(cfg.ScheduledTaskName, @"^KkumpleServiceEnd(?:-[A-Za-z0-9-]+)?$"))
                            throw new InvalidOperationException("꿈플 전용 예약 작업 이름이 아닙니다.");
                        var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                            "/delete /tn \"" + cfg.ScheduledTaskName + "\" /f")
                        { UseShellExecute = false, CreateNoWindow = true };
                        using (Process p = Process.Start(psi))
                        {
                            if (p == null || !p.WaitForExit(15000) || p.ExitCode != 0)
                            { log.Warn("예약 작업 해제 실패. 다음 점검에서 재시도합니다."); return false; }
                        }
                    }
                    catch (Exception ex) { log.Warn("예약 작업 제거 실패: " + ex.Message); return false; }
                }
            }

            log.Info("점검기와 로그는 보존합니다. 자동점검-제거.cmd로 제거할 수 있습니다.");
            return true;
        }
    }

    internal sealed class BundleOperationLock : IDisposable
    {
        private readonly System.Threading.Mutex mutex;
        private bool held;
        public static string NameFor(string path)
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd('\\', '/').ToUpperInvariant();
            using (var sha = SHA256.Create())
                return "Global\\KkumpleBundle-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(full))).Replace("-", "").ToLowerInvariant().Substring(0, 24);
        }
        public BundleOperationLock(string path, int milliseconds)
        {
            mutex = new System.Threading.Mutex(false, NameFor(path));
            try { held = mutex.WaitOne(milliseconds); } catch (System.Threading.AbandonedMutexException) { held = true; }
            if (!held) { mutex.Dispose(); throw new IOException("꿈플 업데이트가 진행 중이어서 종료 정리를 미뤘습니다. 예약 작업을 보존합니다."); }
        }
        public void Dispose() { if (held) { held = false; mutex.ReleaseMutex(); } mutex.Dispose(); }
    }

    // ----- 설정 -----
    internal sealed class Config
    {
        public string ServiceName { get; set; }
        public string ExpiryDateIso { get; set; }                 // 예: "2026-12-31"
        public string NoticeTitle { get; set; }
        public string NoticeMessage { get; set; }
        public bool DryRun { get; set; }                          // 기본 안전값(생성자에서 true)
        public string NoticeMode { get; set; }                    // consent | notify | notify-async | silent
        public bool SelfRemove { get; set; }

        public List<string> AllowedBaseDirs { get; set; }         // 이 폴더들 밑에서만 삭제 허용
        public List<string> PathsToRemove { get; set; }           // 삭제 대상(꿈플 전용)
        public List<string> ProtectedExtensions { get; set; }     // 절대 삭제 금지 확장자

        public bool RequireOwnerMarker { get; set; }              // true면 폴더에 소유자 표식 파일이 있어야 삭제
        public string OwnerMarkerFile { get; set; }               // 예: ".kkumple-owner"
        public string OwnerMarkerMustContain { get; set; }        // 표식 파일에 반드시 포함돼야 하는 문자열

        public string PreUninstallExe { get; set; }               // 선택: 꿈플 자체 정리 명령
        public string PreUninstallArgs { get; set; }
        public int PreUninstallTimeoutSec { get; set; }

        public string ScheduledTaskName { get; set; }             // 선택: 제거할 예약 작업 이름
        public string LogPath { get; set; }                       // 선택: 로그 경로

        // ----- 온라인 원격 제어 -----
        public string StatusUrl { get; set; }                     // 서명된 상태 파일(status.json)의 URL
        public string UpdatePublicKeyXml { get; set; }            // 원격 신호 검증용 RSA 공개키(XML)
        public int CheckTimeoutSec { get; set; }                  // 상태 조회 타임아웃(초)

        public Config()
        {
            ServiceName = "꿈플";
            NoticeTitle = "꿈플 지원 종료 안내";
            NoticeMessage = "꿈플 지원이 종료되었습니다.";
            DryRun = true;
            NoticeMode = "notify-async";
            SelfRemove = false;
            AllowedBaseDirs = new List<string>();
            PathsToRemove = new List<string>();
            ProtectedExtensions = new List<string> { ".dwg", ".dxf", ".dwt", ".dws", ".bak" };
            RequireOwnerMarker = true;
            OwnerMarkerFile = ".kkumple-owner";
            OwnerMarkerMustContain = "KKUMPLE_FREE";
            PreUninstallTimeoutSec = 120;
            CheckTimeoutSec = 15;
        }

        public static Config Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("설정 파일이 없습니다: " + path);
            string json = File.ReadAllText(path, Encoding.UTF8);
            var ser = new JavaScriptSerializer();
            Config c = ser.Deserialize<Config>(json) ?? new Config();
            if (c.AllowedBaseDirs == null) c.AllowedBaseDirs = new List<string>();
            if (c.PathsToRemove == null) c.PathsToRemove = new List<string>();
            if (c.ProtectedExtensions == null) c.ProtectedExtensions = new List<string>();
            foreach (string ext in new[] { ".dwg", ".dxf", ".dwt", ".dws", ".bak" })
                if (!c.ProtectedExtensions.Contains(ext)) c.ProtectedExtensions.Add(ext);
            if (c.PathsToRemove.Count == 0 || c.AllowedBaseDirs.Count == 0)
                throw new InvalidDataException("삭제 대상/허용 경로가 비어 있습니다.");
            if (!c.RequireOwnerMarker || string.IsNullOrWhiteSpace(c.OwnerMarkerMustContain))
                throw new InvalidDataException("소유자 표식 가드는 필수입니다.");
            if (!string.IsNullOrWhiteSpace(c.PreUninstallExe))
                throw new InvalidDataException("이 배포본에서는 외부 제거 명령을 허용하지 않습니다.");
            return c;
        }

        public string ResolveLogPath(string cfgPath)
        {
            if (!string.IsNullOrWhiteSpace(LogPath)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(LogPath));
            return Path.Combine(Path.GetDirectoryName(cfgPath), "service-end.log");
        }

        public bool IsExpired(Logger log)
        {
            if (string.IsNullOrWhiteSpace(ExpiryDateIso))
            {
                log.Warn("ExpiryDateIso 가 비어 있습니다. 만료 아님으로 처리합니다.");
                return false;
            }
            DateTime d;
            if (!DateTime.TryParseExact(ExpiryDateIso, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                log.Warn("ExpiryDateIso 형식 오류(yyyy-MM-dd 필요): " + ExpiryDateIso + " → 만료 아님으로 처리.");
                return false;
            }
            return DateTime.Now.Date >= d.Date;
        }

        // 활성화(=서비스 종료 실행) 조건: 원격 서명 신호 우선, 오프라인 만료일은 선택적 백스톱
        public bool ShouldActivate(Logger log)
        {
            if (RemoteStatus.SaysEnded(this, log)) return true;   // 서명 검증된 'ended:true'
            if (IsExpired(log)) return true;                       // 오프라인 백스톱(ExpiryDateIso 설정 시)
            return false;
        }
    }

    // ----- 온라인 원격 상태(서명 검증) -----
    // 보안 원칙:
    //  - 원격 신호는 반드시 RSA-SHA256 서명 검증을 통과해야만 유효(미서명/위조는 무시)
    //  - 원격 payload 는 '종료 여부'만 전달. 삭제 경로는 로컬 설정에만 존재(서버가 경로 지정 불가)
    //  - 조회 실패/검증 실패 시 아무 것도 하지 않음(fail-safe: 계속 동작 유지)
    //  - 조회 시 개인정보/식별자를 전송하지 않음(단순 GET)
    internal static class RemoteStatus
    {
        public static bool SaysEnded(Config cfg, Logger log)
        {
            if (string.IsNullOrWhiteSpace(cfg.StatusUrl)) return false;
            if (string.IsNullOrWhiteSpace(cfg.UpdatePublicKeyXml))
            {
                log.Warn("공개키(UpdatePublicKeyXml) 미설정 — 원격 신호를 신뢰할 수 없어 무시합니다.");
                return false;
            }

            byte[] raw;
            try { raw = Fetch(cfg.StatusUrl, cfg.CheckTimeoutSec); }
            catch (Exception ex) { log.Warn("상태 조회 실패(무시): " + ex.Message); return false; }

            try
            {
                var ser = new JavaScriptSerializer();
                string text = Encoding.UTF8.GetString(raw);
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1); // BOM 제거
                SignedStatus outer = ser.Deserialize<SignedStatus>(text);
                if (outer == null || string.IsNullOrEmpty(outer.payload) || string.IsNullOrEmpty(outer.signature))
                { log.Warn("상태 형식 오류(무시)."); return false; }

                byte[] payloadBytes = Encoding.UTF8.GetBytes(outer.payload);
                byte[] sigBytes = Convert.FromBase64String(outer.signature);
                if (!VerifyRsaSha256(payloadBytes, sigBytes, cfg.UpdatePublicKeyXml))
                { log.Warn("서명 검증 실패(무시) — 위조되었거나 키가 다릅니다."); return false; }

                StatusPayload p = ser.Deserialize<StatusPayload>(outer.payload);
                if (p == null) { log.Warn("payload 파싱 실패(무시)."); return false; }

                // 서비스 이름은 유니코드 정규화(NFC) 후 비교 — 한글 NFC/NFD 차이로 오탐 방지
                string pSvc = NormalizeName(p.service);
                string cSvc = NormalizeName(cfg.ServiceName);
                if (pSvc.Length == 0 || !string.Equals(pSvc, cSvc, StringComparison.Ordinal))
                { log.Info("다른 서비스 대상 신호 — 무시: " + p.service); return false; }

                if (!p.ended) { log.Info("원격 상태: 정상(ended=false)."); return false; }

                if (!string.IsNullOrWhiteSpace(p.effectiveDateIso))
                {
                    DateTime eff;
                    if (!DateTime.TryParseExact(p.effectiveDateIso, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out eff))
                    { log.Warn("적용일 형식 오류 — 종료 신호 무시."); return false; }
                    if (DateTime.Now.Date < eff.Date)
                    { log.Info("원격 종료 신호 있으나 적용일 이전: " + p.effectiveDateIso); return false; }
                }

                log.Info("원격 종료 신호 확인(서명 검증 통과).");
                return true;
            }
            catch (Exception ex) { log.Warn("상태 처리 오류(무시): " + ex.Message); return false; }
        }

        private static byte[] Fetch(string url, int timeoutSec)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = Math.Max(3, timeoutSec) * 1000;
            req.ReadWriteTimeout = req.Timeout;
            req.UserAgent = "KkumpleServiceEnd/1.0"; // 식별 정보 없음
            req.AllowAutoRedirect = true;
            try { req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore); } catch { }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                byte[] buf = new byte[8192]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 1024 * 1024) break; // 1MB 상한
                }
                return ms.ToArray();
            }
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return s.Trim().Normalize(NormalizationForm.FormC); }
            catch { return s.Trim(); }
        }

        private static bool VerifyRsaSha256(byte[] data, byte[] sig, string publicKeyXml)
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(publicKeyXml);
                using (var sha = new SHA256Managed())
                {
                    byte[] hash = sha.ComputeHash(data);
                    var def = new RSAPKCS1SignatureDeformatter(rsa);
                    def.SetHashAlgorithm("SHA256");
                    return def.VerifySignature(hash, sig);
                }
            }
        }
    }

    internal sealed class SignedStatus
    {
        public string payload { get; set; }
        public string signature { get; set; }
    }

    internal sealed class StatusPayload
    {
        public string service { get; set; }
        public bool ended { get; set; }
        public string effectiveDateIso { get; set; }
        public string issuedAtIso { get; set; }
    }

    // ----- 삭제 가드(핵심 안전장치) -----
    internal sealed class RemovalGuard
    {
        private readonly List<string> _allowed;
        private readonly HashSet<string> _protectedExt;
        private readonly List<string> _forbidden;
        private readonly List<string> _systemForbidden;
        private readonly bool _requireMarker;
        private readonly string _markerFile;
        private readonly string _markerContains;
        private readonly Logger _log;

        public RemovalGuard(List<string> allowedBases, List<string> protectedExt,
            bool requireMarker, string markerFile, string markerContains, Logger log)
        {
            _log = log;
            _requireMarker = requireMarker;
            _markerFile = string.IsNullOrWhiteSpace(markerFile) ? ".kkumple-owner" : markerFile;
            if (Path.GetFileName(_markerFile) != _markerFile || _markerFile == "." || _markerFile == ".."
                || _markerFile.IndexOfAny(new[] { ':', '*', '?' }) >= 0)
                throw new InvalidDataException("소유자 표식은 파일 이름이어야 합니다.");
            _markerContains = markerContains;
            _allowed = new List<string>();
            foreach (string b in allowedBases)
                if (!string.IsNullOrWhiteSpace(b)) _allowed.Add(Norm(b));

            _protectedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string e in protectedExt) if (!string.IsNullOrWhiteSpace(e)) _protectedExt.Add(e.Trim());

            _forbidden = BuildForbidden();
            _systemForbidden = new List<string>();
            foreach (Environment.SpecialFolder sf in new[] { Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86 })
            {
                string p = Environment.GetFolderPath(sf);
                if (!string.IsNullOrEmpty(p)) _systemForbidden.Add(Norm(p));
            }
        }

        public bool IsAllowed(string path, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(path)) { reason = "빈 경로"; return false; }
            if (path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0) { reason = "와일드카드 금지"; return false; }

            string full;
            try { full = Norm(path); }
            catch (Exception e) { reason = "경로 해석 실패: " + e.Message; return false; }

            string root = Path.GetPathRoot(full);
            if (string.Equals(full, Norm(root ?? ""), StringComparison.OrdinalIgnoreCase))
            { reason = "드라이브 루트 금지"; return false; }

            foreach (string f in _forbidden)
            {
                if (string.Equals(full, f, StringComparison.OrdinalIgnoreCase))
                { reason = "보호 경로 자체: " + f; return false; }
                if (IsInside(f, full))
                { reason = "보호 경로를 포함하는 상위 경로: " + f; return false; }
            }
            foreach (string f in _systemForbidden)
                if (IsInside(full, f)) { reason = "시스템/프로그램 경로 내부"; return false; }

            bool inAllowed = false;
            foreach (string b in _allowed)
                if (string.Equals(full, b, StringComparison.OrdinalIgnoreCase) || IsInside(full, b))
                { inAllowed = true; break; }
            if (!inAllowed) { reason = "허용 기준 폴더(AllowedBaseDirs) 밖"; return false; }
            // 루트부터 대상까지 연결점/심볼릭 링크를 하나라도 만나면 거부.
            for (string check = full; !string.IsNullOrEmpty(check); check = Path.GetDirectoryName(check))
            {
                try
                {
                    if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                    { reason = "연결 폴더/링크 금지: " + check; return false; }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception e) { reason = "경로 검사 실패: " + e.Message; return false; }
            }
            return true;
        }

        public bool Remove(string path, bool dryRun)
        {
            string reason;
            if (!IsAllowed(path, out reason))
            {
                _log.Warn("거부: \"" + path + "\" — " + reason);
                return false;
            }
            string full = Norm(path);
            FileAttributes attr;
            try { attr = File.GetAttributes(full); }
            catch (FileNotFoundException) { _log.Info("대상 없음: " + full); return true; }
            catch (DirectoryNotFoundException) { _log.Info("대상 없음: " + full); return true; }
            catch (Exception ex) { _log.Warn("대상 검사 실패: " + ex.Message); return false; }
            if ((attr & FileAttributes.Directory) != 0)
            {
                if (_requireMarker && !HasOwnerMarker(full))
                { _log.Warn("거부: 소유자 표식 없음/불일치: " + full); return false; }
                _log.Info((dryRun ? "[dry-run] " : "") + "폴더 정리: " + full);
                bool kept = false;
                return SafeDeleteDirectory(full, dryRun, true, ref kept);
            }
            if (_requireMarker && !HasOwnerMarker(Path.GetDirectoryName(full)))
            { _log.Warn("거부: 소유자 표식 없음(파일의 폴더): " + full); return false; }
            if (IsProtectedFile(full)) { _log.Warn("보호 확장자라 보존: " + full); return true; }
            return dryRun || TryDeleteFile(full);
        }

        // 비재귀 열거로 연결점을 따라가지 않으며, 루트 표식은 정리가 성공할 때까지 보존.
        private bool SafeDeleteDirectory(string dir, bool dryRun, bool isRoot, ref bool kept)
        {
            string reason;
            if (!IsAllowed(dir, out reason)) { _log.Warn("거부: " + reason); return false; }
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception ex) { _log.Warn("폴더 열거 실패: " + ex.Message); return false; }
            bool complete = true;
            string marker = Path.Combine(dir, _markerFile);
            foreach (string entry in entries)
            {
                if (isRoot && string.Equals(entry, marker, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsAllowed(entry, out reason)) { _log.Warn("거부: " + reason); complete = false; continue; }
                try
                {
                    if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                    {
                        bool childKept = false;
                        complete = SafeDeleteDirectory(entry, dryRun, false, ref childKept) && complete;
                        kept = kept || childKept;
                    }
                    else if (IsProtectedFile(entry))
                    { _log.Warn("보호 확장자라 보존: " + entry); kept = true; }
                    else
                    {
                        _log.Info((dryRun ? "[dry-run] " : "") + "삭제: " + entry);
                        if (!dryRun) complete = TryDeleteFile(entry) && complete;
                    }
                }
                catch (Exception ex) { _log.Warn("정리 실패: " + entry + " — " + ex.Message); complete = false; }
            }
            if (dryRun || !complete || kept) return complete;
            byte[] markerBytes = null;
            if (isRoot && File.Exists(marker))
            {
                try { markerBytes = File.ReadAllBytes(marker); }
                catch (Exception ex) { _log.Warn("표식 보존 준비 실패: " + ex.Message); return false; }
                if (!TryDeleteFile(marker)) return false;
            }
            if (TryRemoveEmptyDir(dir)) return true;
            if (markerBytes != null)
            {
                try { if (IsAllowed(dir, out reason)) File.WriteAllBytes(marker, markerBytes); }
                catch (Exception ex) { _log.Warn("표식 복원 실패: " + ex.Message); }
            }
            return false;
        }

        private bool IsProtectedFile(string file)
        {
            string ext = Path.GetExtension(file);
            return !string.IsNullOrEmpty(ext) && _protectedExt.Contains(ext);
        }

        private bool HasOwnerMarker(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                string mf = Path.Combine(dir, _markerFile);
                string reason;
                if (!IsAllowed(mf, out reason)) return false;
                if (!File.Exists(mf)) return false;
                if (string.IsNullOrEmpty(_markerContains)) return true;
                string txt = File.ReadAllText(mf, Encoding.UTF8);
                return txt.IndexOf(_markerContains, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool TryDeleteFile(string file)
        {
            try
            {
                string reason;
                if (!IsAllowed(file, out reason) || IsProtectedFile(file)) return false;
                File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); return true;
            }
            catch (Exception ex) { _log.Warn("파일 삭제 실패: " + file + " — " + ex.Message); return false; }
        }

        private bool TryRemoveEmptyDir(string d)
        {
            try
            {
                string reason;
                if (!IsAllowed(d, out reason)) return false;
                Directory.Delete(d, false); return true;
            }
            catch (Exception ex) { _log.Warn("폴더 제거 실패: " + d + " — " + ex.Message); return false; }
        }

        private static List<string> BuildForbidden()
        {
            var f = new List<string>();
            Action<Environment.SpecialFolder> add = sf =>
            {
                try { string p = Environment.GetFolderPath(sf); if (!string.IsNullOrEmpty(p)) f.Add(Norm(p)); }
                catch { }
            };
            add(Environment.SpecialFolder.Windows);
            add(Environment.SpecialFolder.System);
            add(Environment.SpecialFolder.SystemX86);
            add(Environment.SpecialFolder.ProgramFiles);
            add(Environment.SpecialFolder.ProgramFilesX86);
            add(Environment.SpecialFolder.CommonProgramFiles);
            add(Environment.SpecialFolder.UserProfile);
            add(Environment.SpecialFolder.MyDocuments);
            add(Environment.SpecialFolder.Personal);
            add(Environment.SpecialFolder.Desktop);
            add(Environment.SpecialFolder.DesktopDirectory);
            add(Environment.SpecialFolder.MyMusic);
            add(Environment.SpecialFolder.MyPictures);
            add(Environment.SpecialFolder.MyVideos);
            add(Environment.SpecialFolder.CommonDocuments);
            add(Environment.SpecialFolder.ApplicationData);
            add(Environment.SpecialFolder.LocalApplicationData);
            try { foreach (var dr in DriveInfo.GetDrives()) f.Add(Norm(dr.RootDirectory.FullName)); } catch { }
            return f;
        }

        private static string Norm(string p)
        {
            string expanded = Environment.ExpandEnvironmentVariables(p).Replace('/', '\\');
            if (expanded.Length < 3 || !char.IsLetter(expanded[0]) || expanded[1] != ':' || expanded[2] != '\\'
                || expanded.IndexOf(':', 2) >= 0 || expanded.IndexOfAny(new[] { '*', '?' }) >= 0)
                throw new InvalidDataException("로컬 드라이브의 절대 경로만 허용합니다.");
            foreach (string part in expanded.Substring(3).Split('\\'))
                if ((part.EndsWith(".", StringComparison.Ordinal) && part != "." && part != "..")
                    || part.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidDataException("점/공백으로 끝나는 경로 구성요소는 허용하지 않습니다.");
            string full = Path.GetFullPath(expanded);
            foreach (string part in full.Substring(3).Split('\\'))
                if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidDataException("점/공백으로 끝나는 경로 구성요소는 허용하지 않습니다.");
            string root = Path.GetPathRoot(full);
            return full.Length == root.Length ? root : full.TrimEnd('\\');
        }

        private static bool IsInside(string child, string parent)
        {
            string c = Norm(child);
            string pa = Norm(parent);
            return c.Length > pa.Length &&
                   c.StartsWith(pa.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----- 로그 -----
    internal sealed class Logger
    {
        private readonly string _path;
        public Logger(string path) { _path = path; }

        public void Info(string m) { Write("INFO", m); }
        public void Warn(string m) { Write("WARN", m); }
        public void Error(string m) { Write("ERROR", m); }

        private void Write(string lvl, string m)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + lvl + "] " + m;
            try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); } catch { }
            Console.WriteLine(line);
        }
    }
}
