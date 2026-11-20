using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace Kkumple.Update
{
    internal sealed class GateRecord
    {
        public string path { get; set; }
        public string sha256 { get; set; }
    }
    internal sealed class InstallState
    {
        public string owner { get; set; }
        public string originalXml { get; set; }
        public string installedXmlHash { get; set; }
        public List<GateRecord> gates { get; set; }
        public string consentAtUtc { get; set; }
        public string disclosureSha256 { get; set; }
    }
    internal static class GateInstaller
    {
        public const string Owner = "KKUMPLE_UPDATER_V1";
        public const string Disclosure = "[꿈플 자동 업데이트 설치 안내]\r\n\r\n"
            + "• AutoCAD가 시작되어 꿈플을 불러오기 전에 온라인 업데이트를 확인합니다.\r\n\r\n"
            + "• 새 버전이 있으면 운영자 전자서명과 파일 SHA-256을 확인하고 자동 설치한 뒤 꿈플을 실행합니다. 필수 업데이트를 건너뛰는 선택은 없습니다.\r\n\r\n"
            + "• 운영자가 서명한 서비스 종료 신호가 있으면 업데이트와 꿈플 시작을 중단합니다.\r\n\r\n"
            + "• 기본 설정은 온라인 확인이 필수입니다. 인터넷 연결, 서명 확인, 설치에 실패하면 해당 시작에서는 꿈플을 불러오지 않습니다. AutoCAD와 도면은 계속 사용할 수 있습니다.\r\n\r\n"
            + "• 꿈플 전용 버전 폴더의 프로그램 파일만 갱신하며 기존 파일과 설정을 복사해 보존합니다. 도면(.dwg/.dxf/.dwt/.dws), .bak, .ini, .cfg, .settings 파일은 업데이트 패키지가 덮어쓸 수 없습니다. 이전 버전은 복구용 폴더에 보관합니다.\r\n\r\n"
            + "• 다른 AutoCAD가 파일을 사용 중이면 자동으로 종료하지 않으며, 해당 CAD를 닫고 다시 시도해야 합니다.\r\n\r\n"
            + "• 기존 기능 DLL은 수정하지 않습니다. 현재 사용자의 LISP 시작 경로 앞에 점검기를 연결합니다. 설치·제거에는 AutoCAD를 종료해야 합니다.\r\n\r\n"
            + "• 점검 기록과 동의 기록은 이 PC에 저장됩니다. 서버에는 일반적인 접속 정보(IP 등)가 보일 수 있습니다. 자동업데이트-제거.cmd로 시작 연결을 해제할 수 있습니다.";

        public static string DefaultBundle { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "KkumpleFree.bundle"); } }
        public static string DefaultData { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kkumple", "Updater"); } }
        public static string MutexName(string bundle) { return "Global\\KkumpleBundle-" + Protocol.Sha(Encoding.UTF8.GetBytes(SafePath.Full(bundle).ToUpperInvariant())).Substring(0, 24); }
        public static void CheckPartnerAgent(string directory)
        {
            string file = SafePath.Under(directory, "꿈플-서비스종료.exe");
            if (!File.Exists(file)) return;
            string marker = SafePath.Under(directory, ".kkumple-service-end-owner");
            Version version;
            if (!File.Exists(marker) || File.ReadAllText(marker, Encoding.UTF8).Trim() != "KKUMPLE_SERVICE_END_V1"
                || !Version.TryParse(System.Diagnostics.FileVersionInfo.GetVersionInfo(file).FileVersion, out version)
                || version < new Version(2, 2, 0, 0))
                throw new IOException("기존 서비스 종료 점검기를 이 배포본에 포함된 2.2.0 이상으로 먼저 다시 설치해 주세요. 파일 동시 변경 보호에 필요합니다.");
        }
        public static void EnsureOwnedData(string data, bool create)
        {
            SafePath.Check(data);
            string marker = SafePath.Under(data, ".kkumple-updater-owner");
            if (File.Exists(marker))
            {
                if (File.ReadAllText(marker, Encoding.ASCII) != Owner) throw new IOException("업데이트 점검기 폴더의 표식이 다릅니다.");
                return;
            }
            if (!create || (Directory.Exists(data) && Directory.GetFileSystemEntries(data).Length != 0))
                throw new IOException("소유자 표식이 없는 기존 폴더는 변경하지 않습니다.");
            Directory.CreateDirectory(data); SafePath.Atomic(marker, Encoding.ASCII.GetBytes(Owner));
        }
        private static InstallState ReadState(string data)
        {
            string file = SafePath.Under(data, "install-state.json");
            if (!File.Exists(file)) return null;
            var state = JsonXml.Read<InstallState>(File.ReadAllBytes(file));
            if (state == null || state.owner != Owner || state.gates == null) throw new InvalidDataException("설치 기록을 확인하지 못했습니다.");
            return state;
        }
        public static void UpdateStateHash(string data, string hash)
        {
            var state = ReadState(data);
            if (state == null) return;
            state.installedXmlHash = hash;
            SafePath.Atomic(SafePath.Under(data, "install-state.json"), JsonXml.Bytes(state));
        }
        public static void Install(string bundle, string data, string executable, UpdateConfig cfg)
        {
            cfg.Validate(); EnsureOwnedData(data, true);
            new UpdateEngine(bundle, data, cfg).Recover();
            var local = BundleInfo.Load(bundle); var previous = ReadState(data);
            XmlDocument xml = local.Xml;
            if (previous != null)
            {
                if (Protocol.Sha(local.XmlBytes) == previous.installedXmlHash)
                {
                    xml = JsonXml.ReadXml(Convert.FromBase64String(previous.originalXml));
                    xml.DocumentElement.SetAttribute("AppVersion", local.Xml.DocumentElement.GetAttribute("AppVersion"));
                }
                else
                {
                    // The original body installer can recreate its own original autoload entries.
                    // Preserve and wrap that new document; never overwrite a modified existing gate.
                    foreach (XmlElement entry in xml.SelectNodes("//ComponentEntry"))
                        if (entry.GetAttribute("ModuleName").IndexOf("kkumple-update-gate-", StringComparison.OrdinalIgnoreCase) >= 0)
                            throw new IOException("시작 설정이 다른 프로그램에 의해 변경되었습니다. 기존 설치를 확인한 뒤 연결해 주세요.");
                }
            }
            byte[] original = JsonXml.XmlBytes(xml);
            var gates = new List<GateRecord>(); var gateBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement entry in xml.SelectNodes("//ComponentEntry"))
            {
                string module = entry.GetAttribute("ModuleName").Replace('\\', '/');
                if (module.StartsWith("./", StringComparison.Ordinal)) module = module.Substring(2);
                if (!module.EndsWith(".lsp", StringComparison.OrdinalIgnoreCase) || module.IndexOf("kkumple-update-gate-", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException("이 설치본의 시작 모듈은 지원하는 LISP 로더가 아닙니다.");
                string originalLoader = SafePath.Under(bundle, module);
                if (!File.Exists(originalLoader)) throw new FileNotFoundException("꿈플 시작 LISP를 찾을 수 없습니다.", originalLoader);
                string gateRelative = "Contents/Loaders/kkumple-update-gate-" + Protocol.Sha(Encoding.UTF8.GetBytes(module)).Substring(0, 24) + ".lsp";
                string gateFile = SafePath.Under(bundle, gateRelative);
                byte[] bytes = Encoding.ASCII.GetBytes(CreateLisp(module));
                if (File.Exists(gateFile))
                {
                    bool owned = previous != null && previous.gates.Exists(g => g.path == gateRelative && g.sha256 == Protocol.FileSha(gateFile));
                    if (!owned) throw new IOException("시작 점검 LISP 위치에 다른 파일이 있습니다.");
                }
                gateBytes[gateRelative] = bytes;
                if (!gates.Exists(g => g.path == gateRelative)) gates.Add(new GateRecord { path = gateRelative, sha256 = Protocol.Sha(bytes) });
                entry.SetAttribute("ModuleName", "./" + gateRelative);
                entry.SetAttribute("LoadOnAppearance", "False");
            }
            if (gates.Count == 0) throw new InvalidDataException("연결할 꿈플 LISP가 없습니다.");
            byte[] installedXml = JsonXml.XmlBytes(xml);
            var state = new InstallState { owner = Owner, originalXml = Convert.ToBase64String(original),
                installedXmlHash = Protocol.Sha(installedXml), gates = gates, consentAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                disclosureSha256 = Protocol.Sha(Encoding.UTF8.GetBytes(Disclosure)) };
            string stateFile = SafePath.Under(data, "install-state.json");
            byte[] oldState = File.Exists(stateFile) ? File.ReadAllBytes(stateFile) : null;
            string configFile = SafePath.Under(data, "updater-config.json");
            byte[] oldConfig = File.Exists(configFile) ? File.ReadAllBytes(configFile) : null;
            string installedExe = SafePath.Under(data, "Kkumple.Updater.exe");
            byte[] oldExe = File.Exists(installedExe) ? File.ReadAllBytes(installedExe) : null;
            bool exeChanged = false;
            var existingGateBytes = new Dictionary<string, byte[]>();
            foreach (string name in gateBytes.Keys)
            {
                string file = SafePath.Under(bundle, name); existingGateBytes[name] = File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            try
            {
                if (!string.Equals(SafePath.Full(executable), installedExe, StringComparison.OrdinalIgnoreCase))
                { SafePath.Atomic(installedExe, File.ReadAllBytes(executable)); exeChanged = true; }
                SafePath.Atomic(configFile, JsonXml.Bytes(cfg));
                foreach (var item in gateBytes) SafePath.Atomic(SafePath.Under(bundle, item.Key), item.Value);
                SafePath.Atomic(stateFile, JsonXml.Bytes(state));
                SafePath.Atomic(SafePath.Under(bundle, "PackageContents.xml"), installedXml);
            }
            catch
            {
                SafePath.Atomic(SafePath.Under(bundle, "PackageContents.xml"), local.XmlBytes);
                if (oldState != null) SafePath.Atomic(stateFile, oldState);
                else if (File.Exists(stateFile)) File.Delete(stateFile);
                if (oldConfig != null) SafePath.Atomic(configFile, oldConfig);
                else if (File.Exists(configFile)) File.Delete(configFile);
                if (exeChanged)
                {
                    if (oldExe != null) SafePath.Atomic(installedExe, oldExe);
                    else if (File.Exists(installedExe)) File.Delete(installedExe);
                }
                foreach (var item in existingGateBytes)
                {
                    string file = SafePath.Under(bundle, item.Key);
                    if (item.Value == null) { if (File.Exists(file) && Protocol.FileSha(file) == Protocol.Sha(gateBytes[item.Key])) File.Delete(file); }
                    else SafePath.Atomic(file, item.Value);
                }
                throw;
            }
        }
        public static bool Uninstall(string bundle, string data)
        {
            EnsureOwnedData(data, false); var state = ReadState(data); if (state == null) return false;
            var local = BundleInfo.Load(bundle);
            if (Protocol.Sha(local.XmlBytes) != state.installedXmlHash)
                throw new IOException("시작 설정이 설치 이후 변경되어 자동 복원하지 않았습니다. 현재 파일은 보존했습니다.");
            foreach (var gate in state.gates)
            {
                string file = SafePath.Under(bundle, gate.path);
                if (File.Exists(file) && Protocol.FileSha(file) != gate.sha256) throw new IOException("시작 점검 LISP가 수정되어 자동 제거하지 않았습니다.");
            }
            var xml = JsonXml.ReadXml(Convert.FromBase64String(state.originalXml));
            xml.DocumentElement.SetAttribute("AppVersion", local.Xml.DocumentElement.GetAttribute("AppVersion"));
            SafePath.Atomic(SafePath.Under(bundle, "PackageContents.xml"), JsonXml.XmlBytes(xml));
            foreach (var gate in state.gates)
            {
                string file = SafePath.Under(bundle, gate.path);
                if (File.Exists(file) && Protocol.FileSha(file) == gate.sha256) File.Delete(file);
            }
            File.Delete(SafePath.Under(data, "install-state.json"));
            // 실행파일/로그/캐시는 감사와 재설치를 위해 보존. 시작 연결은 해제됨.
            return true;
        }
        internal static string CreateLisp(string originalModule)
        {
            // ASCII source works with pre-Unicode AutoCAD Lisp engines. User paths are resolved at runtime.
            string relative = originalModule.Replace('\\', '/');
            if (relative.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new InvalidDataException("LISP 경로 형식 오류입니다.");
            foreach (char ch in relative) if (ch > 127) throw new InvalidDataException("현재 CAD 호환 시작 로더의 파일 이름은 ASCII여야 합니다.");
            return "; KKUMPLE_UPDATER_V1 - startup update gate; original modules are not patched.\r\n"
                + "(vl-load-com)\r\n"
                + "(defun kkupd:permit (/ exe shell result cmd)\r\n"
                + "  (if (eq (vl-bb-ref 'kkumple-update-approved-v1) T) T\r\n"
                + "    (progn\r\n"
                + "      (setq exe (strcat (getenv \"LOCALAPPDATA\") \"/Kkumple/Updater/Kkumple.Updater.exe\"))\r\n"
                + "      (if (findfile exe)\r\n"
                + "        (progn\r\n"
                + "          (setq shell (vl-catch-all-apply 'vlax-create-object (list \"WScript.Shell\")))\r\n"
                + "          (if (not (vl-catch-all-error-p shell))\r\n"
                + "            (progn\r\n"
                + "              (setq cmd (strcat (chr 34) exe (chr 34) \" --check\"))\r\n"
                + "              (setq result (vl-catch-all-apply 'vlax-invoke-method (list shell 'Run cmd 1 :vlax-true)))\r\n"
                + "              (vlax-release-object shell)\r\n"
                + "              (if (and (not (vl-catch-all-error-p result)) (eq (type result) 'INT) (= result 0))\r\n"
                + "                (progn (vl-bb-set 'kkumple-update-approved-v1 T) T) nil)) nil)) nil))))\r\n"
                + "(if (kkupd:permit)\r\n"
                + "  (load (strcat (getenv \"APPDATA\") \"/Autodesk/ApplicationPlugins/KkumpleFree.bundle/" + relative + "\"))\r\n"
                + "  (princ \"\\n[Kkumple] Update check did not complete. Kkumple was not loaded; AutoCAD remains available.\"))\r\n"
                + "(princ)\r\n";
        }
    }
}
