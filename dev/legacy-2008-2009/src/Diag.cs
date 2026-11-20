// 꿈플 2008/2009 시험판 진단: PC·AutoCAD 환경, 설치 상태, 오류, CAD 명령행 기록을 모아
// ntfy.sh 중계 주소로 보낸다. 개발 PC의 수신기가 받아 D:\꿈플-2008-로그 에 저장한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Kkumple.Legacy17
{
    static class Diag
    {
        // 추측하기 어려운 중계 주소 (수신기와 같아야 함)
        public const string Topic = "kk2008-REDACTED-see-local-file";
        const int ChunkBytes = 3400;
        public static readonly StringBuilder Session = new StringBuilder();

        public static void Note(string s)
        {
            lock (Session) Session.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + s);
        }

        public static string Build(Options o, string reason, Exception error)
        {
            var sb = new StringBuilder();
            Action<string> w = s => sb.AppendLine(s);
            w("=== 꿈플 2008/2009 진단 보고서 ===");
            w("reason=" + reason);
            w("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            w("connector=" + typeof(Diag).Assembly.GetName().Version + " exe=" + Safe(() => Process.GetCurrentProcess().MainModule.FileName));
            w("machine=" + Environment.MachineName + " user=" + Environment.UserName);
            w("os=" + Environment.OSVersion + " 64bitOS=" + Environment.Is64BitOperatingSystem + " 64bitProc=" + Environment.Is64BitProcess);
            w("osName=" + Safe(() => Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") + " " + Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") + " build " + Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")));
            w("clr=" + Environment.Version + " net4release=" + Safe(() => Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release")));
            w("net35=" + Safe(() => Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5", "Install")) + " net20=" + Safe(() => Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v2.0.50727", "Install")));
            w("ansiCodePage=" + Encoding.Default.CodePage + " culture=" + System.Globalization.CultureInfo.CurrentCulture.Name + " uiCulture=" + System.Globalization.CultureInfo.CurrentUICulture.Name);
            w("appdata=" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            w("dataDir=" + o.DataDir + " ascii=" + o.DataDir.All(c => c < 128) + " short=" + Safe(() => Engine.ShortPath(o.DataDir)));
            if (error != null) { w(""); w("--- 오류 ---"); w(error.ToString()); }

            w(""); w("--- 실행 중인 AutoCAD ---");
            foreach (string pn in new[] { "acad", "acadlt" })
                foreach (var p in Process.GetProcessesByName(pn))
                    w(pn + " pid=" + p.Id + " path=" + Safe(() => p.MainModule.FileName) + " started=" + Safe(() => p.StartTime.ToString("HH:mm:ss")));

            w(""); w("--- 설치된 Autodesk 제품 (HKLM) ---");
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                foreach (string family in new[] { "AutoCAD", "AutoCAD LT" })
                    Safe(() =>
                    {
                        using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (var fk = hk.OpenSubKey(@"SOFTWARE\Autodesk\" + family))
                        {
                            if (fk == null) return "";
                            foreach (string rel in fk.GetSubKeyNames())
                                using (var rk = fk.OpenSubKey(rel))
                                    foreach (string sub in rk.GetSubKeyNames())
                                        using (var pk = rk.OpenSubKey(sub))
                                        {
                                            string loc = (pk.GetValue("AcadLocation") ?? pk.GetValue("Location")) as string;
                                            w(view + " " + family + "\\" + rel + "\\" + sub + " name=" + pk.GetValue("ProductName") + " loc=" + loc + " lang=" + pk.GetValue("Language"));
                                            if (!string.IsNullOrEmpty(loc))
                                                foreach (string exe in new[] { "acad.exe", "acadlt.exe" })
                                                {
                                                    string f = Path.Combine(loc, exe);
                                                    if (File.Exists(f)) { var vi = FileVersionInfo.GetVersionInfo(f); w("    " + exe + " product=" + vi.ProductName + " ver=" + vi.FileVersion + " desc=" + vi.FileDescription); }
                                                }
                                        }
                        }
                        return "";
                    });

            w(""); w("--- 사용자 프로필·시작하기 세트 (HKCU) ---");
            foreach (string family in new[] { "AutoCAD", "AutoCAD LT" })
                Safe(() =>
                {
                    using (var fk = Registry.CurrentUser.OpenSubKey(@"Software\Autodesk\" + family))
                    {
                        if (fk == null) return "";
                        foreach (string rel in fk.GetSubKeyNames())
                            using (var rk = fk.OpenSubKey(rel))
                                foreach (string sub in rk.GetSubKeyNames())
                                    using (var prof = rk.OpenSubKey(sub + @"\Profiles"))
                                    {
                                        w(family + "\\" + rel + "\\" + sub + (prof == null ? " (프로필 없음)" : ""));
                                        if (prof == null) continue;
                                        w("    현재 프로필=" + prof.GetValue(""));
                                        foreach (string pn in prof.GetSubKeyNames())
                                            using (var sk = prof.OpenSubKey(pn + @"\Dialogs\Appload\Startup"))
                                            {
                                                w("    프로필 '" + pn + "'");
                                                if (sk == null) { w("        시작하기 세트 키 없음"); continue; }
                                                foreach (string v in sk.GetValueNames()) w("        " + v + "=" + sk.GetValue(v));
                                            }
                                    }
                    }
                    return "";
                });

            w(""); w("--- 설치 폴더 ---");
            if (Directory.Exists(o.DataDir))
            {
                foreach (string f in Directory.GetFiles(o.DataDir)) w(Path.GetFileName(f) + " " + new FileInfo(f).Length + " " + File.GetLastWriteTime(f).ToString("MM-dd HH:mm:ss"));
                foreach (string d in Directory.GetDirectories(o.DataDir)) w("[" + Path.GetFileName(d) + "] " + Directory.GetFiles(d, "*", SearchOption.AllDirectories).Length + " files");
                foreach (string f in Directory.GetFiles(o.DataDir, "load-report-*.txt").Concat(Directory.GetFiles(o.DataDir, "kkl-log.txt")))
                { w(""); w("--- " + Path.GetFileName(f) + " ---"); w(Tail(f, 12000)); }
                string cadlog = Path.Combine(o.DataDir, "cadlog");
                if (Directory.Exists(cadlog))
                    foreach (string f in Directory.GetFiles(cadlog, "*.log").OrderByDescending(File.GetLastWriteTime).Take(2))
                    { w(""); w("--- CAD 명령행 기록 " + Path.GetFileName(f) + " ---"); w(Tail(f, 16000)); }
            }
            else w("없음 (아직 설치하지 않음)");

            lock (Session) if (Session.Length > 0) { w(""); w("--- 연결기 진행 기록 ---"); w(Session.ToString()); }
            return sb.ToString();
        }

        // 보고서를 저장하고 중계 주소로 보낸다. 실패해도 예외를 던지지 않는다.
        public static string Send(Options o, string reason, Exception error)
        {
            string text;
            try { text = Build(o, reason, error); }
            catch (Exception ex) { text = "보고서 작성 실패: " + ex + "\r\n원래 오류: " + error; }
            string id = DateTime.Now.ToString("MMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            try
            {
                string dir = Path.Combine(o.DataDir.Length > 0 && Directory.Exists(o.DataDir) ? o.DataDir : Path.GetTempPath(), "diag");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, id + ".txt"), text, new UTF8Encoding(true));
            }
            catch { }
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var chunks = Split(Encoding.UTF8.GetBytes(text), ChunkBytes);
                for (int i = 0; i < chunks.Count; i++)
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers["Title"] = "KK2008 " + id + " " + (i + 1) + "/" + chunks.Count + " " + Ascii(reason) + " " + Ascii(Environment.MachineName);
                        wc.Headers["Tags"] = "kk2008";
                        wc.Headers["Content-Type"] = "text/plain; charset=utf-8";
                        wc.UploadData("https://ntfy.sh/" + Topic, "POST", chunks[i]);
                    }
                    if (i + 1 < chunks.Count) Thread.Sleep(300);
                }
                return "진단 보고서를 보냈습니다 (" + id + ", " + chunks.Count + "조각).";
            }
            catch (Exception ex) { return "진단 보고서 전송 실패(인터넷 연결 확인): " + ex.Message + " / 사본: " + o.DataDir + "\\diag"; }
        }

        static List<byte[]> Split(byte[] all, int max)
        {
            var list = new List<byte[]>();
            int pos = 0;
            while (pos < all.Length)
            {
                int len = Math.Min(max, all.Length - pos);
                // UTF-8 문자 중간에서 자르지 않는다
                while (pos + len < all.Length && len > 0 && (all[pos + len] & 0xC0) == 0x80) len--;
                var b = new byte[len]; Buffer.BlockCopy(all, pos, b, 0, len); list.Add(b); pos += len;
            }
            if (list.Count == 0) list.Add(Encoding.UTF8.GetBytes("(빈 보고서)"));
            return list;
        }

        static string Ascii(string s) { var sb = new StringBuilder(); foreach (char c in s ?? "") sb.Append(c > 32 && c < 127 ? c : '_'); return sb.ToString(); }

        static string Tail(string f, int max)
        {
            try
            {
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var b = new byte[fs.Length]; int n = 0; while (n < b.Length) { int r = fs.Read(b, n, b.Length - n); if (r <= 0) break; n += r; }
                    string t = Encoding.Default.GetString(b, 0, n);
                    return t.Length > max ? "...(앞부분 생략)\r\n" + t.Substring(t.Length - max) : t;
                }
            }
            catch (Exception ex) { return "(읽기 실패: " + ex.Message + ")"; }
        }

        static string Reg(RegistryKey hive, string key, string name)
        {
            using (var k = hive.OpenSubKey(key)) return k == null ? "-" : Convert.ToString(k.GetValue(name) ?? "-");
        }

        static string Safe(Func<string> f) { try { return f(); } catch (Exception ex) { return "(오류: " + ex.Message + ")"; } }
    }
}
