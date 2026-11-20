// Exact safety guard and logger from the verified service-end agent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace KkumpleServiceEnd
{
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
