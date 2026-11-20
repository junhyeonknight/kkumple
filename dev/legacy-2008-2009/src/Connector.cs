// 꿈플 AutoCAD 2008/2009 연결기 (시험판)
// - 설치된 AutoCAD 2008(R17.1)/2009(R17.2)를 찾아 꿈플 2010 계열 모듈을 연결한다.
// - 자동 로딩: 현재 사용자의 '시작하기 세트(Startup Suite)'에 로더 LISP 한 개를 추가한다.
// - acad.lsp/acaddoc.lsp, 주 CUI, AutoCAD 프로그램 폴더는 수정하지 않는다.
// - 제거 시 우리가 추가한 시작하기 세트 항목과 소유 표식이 있는 데이터 폴더만 지운다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("꿈플 2008-2009 연결기 (시험판)")]
[assembly: AssemblyProduct("꿈플")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace Kkumple.Legacy17
{
    sealed class CadProduct
    {
        public string Release;      // R17.1 / R17.2
        public string KeyName;      // ACAD-7001:412
        public string Name;         // AutoCAD 2009 ...
        public string Location;
        public string UserKey;      // Software\Autodesk\AutoCAD\R17.2\ACAD-7001:412 (HKCU 기준 상대 경로)
        public bool HasProfiles;
        public string Year { get { return Release == "R17.1" ? "2008" : "2009"; } }
        public string Id { get { return Release + "\\" + KeyName; } }
        public override string ToString()
        {
            return (string.IsNullOrEmpty(Name) ? "AutoCAD " + Year : Name) + "  [" + Release + " " + KeyName + "]" + (HasProfiles ? "" : "  - CAD를 한 번 실행한 뒤 종료해 주세요");
        }
    }

    sealed class Options
    {
        public string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KkumpleFree", "Legacy17");
        public RegistryKey MachineHive = null;  // null = HKLM 64/32비트 양쪽
        public string MachineBase = @"Software\Autodesk\AutoCAD";
        public RegistryKey UserHive = Registry.CurrentUser;
        public string UserBase = @"Software\Autodesk\AutoCAD";
        public bool TestRoots;
    }

    static class Engine
    {
        public const string LoaderName = "kkumple-legacy17.lsp";
        const string OwnerFile = ".kkumple-legacy17-owner";
        const string OwnerText = "KKUMPLE_LEGACY17_EXPERIMENTAL";
        static readonly string[] Releases = { "R17.1", "R17.2" };

        public static List<CadProduct> Discover(Options o)
        {
            var found = new Dictionary<string, CadProduct>(StringComparer.OrdinalIgnoreCase);
            var hives = new List<RegistryKey>();
            if (o.MachineHive != null) hives.Add(o.MachineHive);
            else
            {
                hives.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64));
                hives.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32));
            }
            foreach (var hive in hives)
                foreach (string rel in Releases)
                    using (var rk = hive.OpenSubKey(o.MachineBase + "\\" + rel))
                    {
                        if (rk == null) continue;
                        foreach (string sub in rk.GetSubKeyNames())
                        {
                            if (!sub.StartsWith("ACAD-", StringComparison.OrdinalIgnoreCase)) continue;
                            using (var pk = rk.OpenSubKey(sub))
                            {
                                var p = new CadProduct { Release = rel, KeyName = sub };
                                p.Name = pk.GetValue("ProductName") as string;
                                p.Location = (pk.GetValue("AcadLocation") ?? pk.GetValue("Location")) as string;
                                p.UserKey = o.UserBase + "\\" + rel + "\\" + sub;
                                using (var prof = o.UserHive.OpenSubKey(p.UserKey + "\\Profiles")) p.HasProfiles = prof != null && prof.GetSubKeyNames().Length > 0;
                                if (!found.ContainsKey(p.Id)) found[p.Id] = p;
                            }
                        }
                    }
            return found.Values.OrderBy(p => p.Id).ToList();
        }

        public static void EnsureCadClosed(Options o)
        {
            if (o.TestRoots) return;
            if (Process.GetProcessesByName("acad").Length != 0)
                throw new InvalidOperationException("열려 있는 AutoCAD를 모두 종료한 뒤 다시 실행해 주세요. 도면과 프로그램은 변경하지 않았습니다.");
        }

        public static void Install(Options o, IList<CadProduct> targets, Action<string> log)
        {
            if (targets.Count == 0) throw new InvalidOperationException("연결할 AutoCAD 2008/2009를 선택해 주세요.");
            foreach (var t in targets) if (!t.HasProfiles) throw new InvalidOperationException(t + "\n\nAutoCAD를 한 번 실행했다가 종료한 뒤 다시 시도해 주세요(사용자 프로필이 아직 없습니다).");
            EnsureCadClosed(o);

            // 1) 페이로드 준비 (임시 폴더에 풀고 해시 확인 후 교체)
            string data = Path.GetFullPath(o.DataDir);
            string parent = Path.GetDirectoryName(data);
            Directory.CreateDirectory(parent);
            if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any() && !IsOwned(data))
                throw new InvalidOperationException("설치 폴더에 꿈플 시험판이 아닌 파일이 있어 변경하지 않았습니다:\n" + data);
            string stage = Path.Combine(parent, ".kkl-stage-" + Guid.NewGuid().ToString("N"));
            string backup = Path.Combine(parent, ".kkl-backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                log("모듈 압축 해제와 SHA-256 확인 중...");
                Extract(stage);
                File.WriteAllText(Path.Combine(stage, OwnerFile), OwnerText, Encoding.ASCII);
                BuildLoader(stage, data); // 템플릿·인코딩 사전 검사 (실패 시 기존 설치 유지)
                if (Directory.Exists(data))
                {
                    // 이전 시험판의 보고서는 보존
                    foreach (string rep in Directory.GetFiles(data, "load-report-*.txt")) File.Copy(rep, Path.Combine(stage, Path.GetFileName(rep)), true);
                    Directory.Move(data, backup);
                }
                Directory.Move(stage, data);
                // 로더는 최종 폴더가 생긴 뒤 만든다(ASCII 짧은 경로를 쓸 수 있도록).
                File.WriteAllBytes(Path.Combine(data, LoaderName), BuildLoader(data, data));
                if (Directory.Exists(backup)) TryDelete(backup);
            }
            catch
            {
                TryDelete(stage);
                if (Directory.Exists(backup)) { if (Directory.Exists(data)) TryDelete(data); if (!Directory.Exists(data)) Directory.Move(backup, data); }
                throw;
            }
            log("모듈 설치 완료: " + data);

            // 2) 시작하기 세트 등록
            string loaderPath = LoaderPathForCad(data);
            foreach (var t in targets)
                using (var prof = o.UserHive.OpenSubKey(t.UserKey + "\\Profiles"))
                    foreach (string profile in prof.GetSubKeyNames())
                    {
                        string key = t.UserKey + "\\Profiles\\" + profile + "\\Dialogs\\Appload\\Startup";
                        using (var sk = o.UserHive.CreateSubKey(key))
                        {
                            var list = ReadStartup(sk);
                            list.RemoveAll(p => IsOurLoader(p, data));
                            list.Add(loaderPath);
                            WriteStartup(sk, list);
                        }
                        log("시작하기 세트 등록: " + t.Year + " / 프로필 '" + profile + "'");
                    }
        }

        public static void Uninstall(Options o, Action<string> log)
        {
            EnsureCadClosed(o);
            string data = Path.GetFullPath(o.DataDir);
            int removed = 0;
            foreach (string rel in Releases)
                using (var rk = o.UserHive.OpenSubKey(o.UserBase + "\\" + rel))
                {
                    if (rk == null) continue;
                    foreach (string sub in rk.GetSubKeyNames())
                        using (var prof = rk.OpenSubKey(sub + "\\Profiles"))
                        {
                            if (prof == null) continue;
                            foreach (string profile in prof.GetSubKeyNames())
                            {
                                string key = o.UserBase + "\\" + rel + "\\" + sub + "\\Profiles\\" + profile + "\\Dialogs\\Appload\\Startup";
                                using (var sk = o.UserHive.OpenSubKey(key, true))
                                {
                                    if (sk == null) continue;
                                    var list = ReadStartup(sk);
                                    int n = list.RemoveAll(p => IsOurLoader(p, data));
                                    if (n > 0) { WriteStartup(sk, list); removed += n; log("시작하기 세트 해제: " + rel + " " + sub + " / '" + profile + "'"); }
                                }
                            }
                        }
                }
            if (Directory.Exists(data))
            {
                if (!IsOwned(data)) throw new InvalidOperationException("소유 표식이 없는 폴더라 삭제하지 않았습니다: " + data);
                Directory.Delete(data, true);
                log("시험판 파일 삭제: " + data);
            }
            log(removed == 0 ? "등록된 시작하기 세트 항목이 없었습니다." : "제거 완료 (" + removed + "개 항목).");
        }

        // --- 시작하기 세트 (HKCU ... \Profiles\<p>\Dialogs\Appload\Startup : NumStartup, 1Startup, 2Startup ...) ---
        static List<string> ReadStartup(RegistryKey sk)
        {
            var list = new List<string>();
            int n; int.TryParse(Convert.ToString(sk.GetValue("NumStartup") ?? "0"), out n);
            for (int i = 1; i <= n; i++) { string v = sk.GetValue(i + "Startup") as string; if (!string.IsNullOrEmpty(v)) list.Add(v); }
            return list;
        }

        static void WriteStartup(RegistryKey sk, List<string> list)
        {
            int old; int.TryParse(Convert.ToString(sk.GetValue("NumStartup") ?? "0"), out old);
            for (int i = 1; i <= Math.Max(old, list.Count) + 1; i++) if (sk.GetValue(i + "Startup") != null) sk.DeleteValue(i + "Startup", false);
            for (int i = 0; i < list.Count; i++) sk.SetValue((i + 1) + "Startup", list[i], RegistryValueKind.String);
            sk.SetValue("NumStartup", list.Count.ToString(), RegistryValueKind.String);
        }

        static bool IsOurLoader(string path, string data)
        {
            if (!string.Equals(Path.GetFileName(path), LoaderName, StringComparison.OrdinalIgnoreCase)) return false;
            string dir = Path.GetDirectoryName(path) ?? "";
            return string.Equals(Long(dir), Long(data), StringComparison.OrdinalIgnoreCase)
                || string.Equals(dir.TrimEnd('\\'), data.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                || string.Equals(dir.TrimEnd('\\'), Short(data).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        static bool IsOwned(string dir)
        {
            string f = Path.Combine(dir, OwnerFile);
            return File.Exists(f) && File.ReadAllText(f).Trim() == OwnerText;
        }

        // --- 페이로드 ---
        static void Extract(string stage)
        {
            Directory.CreateDirectory(stage);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegacyPayload.zip"))
            {
                if (s == null) throw new InvalidDataException("내장 페이로드가 없습니다. 빌드를 확인해 주세요.");
                using (var z = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    string root = Path.GetFullPath(stage) + "\\";
                    foreach (var e in z.Entries)
                    {
                        if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) continue;
                        string dest = Path.GetFullPath(Path.Combine(stage, e.FullName.Replace('/', '\\')));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("잘못된 압축 경로: " + e.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                }
            }
            // 파일 목록 대조
            string listFile = Path.Combine(stage, "payload-files.txt");
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payload-files.txt" };
            foreach (string line in File.ReadAllLines(listFile))
            {
                if (line.Length == 0) continue;
                string[] p = line.Split(new[] { ' ' }, 3);
                string file = Path.Combine(stage, p[2].Replace('/', '\\'));
                if (p[2] == "payload-files.txt") continue;
                if (!File.Exists(file) || new FileInfo(file).Length != long.Parse(p[1]) || Sha(file) != p[0])
                    throw new InvalidDataException("파일 검증 실패: " + p[2]);
                expected.Add(p[2].Replace('/', '\\'));
            }
            foreach (string f in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                if (!expected.Contains(f.Substring(stage.Length + 1))) throw new InvalidDataException("목록에 없는 파일: " + f);
        }

        static byte[] BuildLoader(string stage, string data)
        {
            string tmpl;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("loader-template.lsp"))
            using (var r = new StreamReader(s, Encoding.ASCII)) tmpl = r.ReadToEnd();
            var aliases = new StringBuilder();
            var commands = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Path.Combine(stage, "aliases.tsv"), Encoding.ASCII))
            {
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                aliases.Append("(defun c:").Append(p[0]).Append(" () (kkl:run \"").Append(p[1]).Append("\"))\r\n");
                commands.Add(p[1]);
            }
            string root = LoaderPathForCad(data, true);
            string text = tmpl.Replace("@@ROOT@@", root.Replace("\\", "\\\\"))
                              .Replace("@@COMMANDS@@", string.Join(" ", commands.Select(c => "\"" + c + "\"").ToArray()))
                              .Replace("@@ALIASES@@", aliases.ToString().TrimEnd());
            if (text.Contains("@@")) throw new InvalidDataException("로더 템플릿 치환이 끝나지 않았습니다.");
            // AutoCAD 2008/2009 LISP는 시스템 ANSI 코드 페이지로 파일을 읽는다.
            var enc = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return enc.GetBytes(text);
        }

        // 2008/2009 LISP·시작하기 세트에 넣을 경로: ASCII 짧은 경로 → 시스템 코드 페이지로 표현 가능한 전체 경로 순으로 선택
        static string LoaderPathForCad(string data, bool directory = false)
        {
            string full = directory ? data : Path.Combine(data, LoaderName);
            if (IsAscii(full)) return full;
            string shortDir = Short(data);
            if (Directory.Exists(data) && IsAscii(shortDir) && shortDir != data) return directory ? shortDir : Path.Combine(shortDir, LoaderName);
            var enc = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try { enc.GetBytes(full); return full; }
            catch (EncoderFallbackException) { throw new InvalidOperationException("설치 경로를 이 PC의 언어 설정으로 표현할 수 없습니다: " + full); }
        }

        static bool IsAscii(string s) { return s.All(c => c < 128); }

        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern int GetShortPathName(string l, StringBuilder s, int n);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern int GetLongPathName(string s, StringBuilder l, int n);
        static string Short(string p) { var sb = new StringBuilder(1024); return Directory.Exists(p) && GetShortPathName(p, sb, sb.Capacity) > 0 ? sb.ToString() : p; }
        static string Long(string p) { var sb = new StringBuilder(1024); return Directory.Exists(p) && GetLongPathName(p, sb, sb.Capacity) > 0 ? sb.ToString().TrimEnd('\\') : p.TrimEnd('\\'); }

        static string Sha(string f) { using (var h = SHA256.Create()) using (var s = File.OpenRead(f)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        static void TryDelete(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }
    }

    sealed class MainForm : Form
    {
        readonly Options opt;
        readonly CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        readonly TextBox logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        readonly Button install = new Button { Text = "꿈플 연결(설치)", AutoSize = true };
        readonly Button remove = new Button { Text = "이 컴퓨터에서 제거", AutoSize = true };
        readonly Button close = new Button { Text = "닫기", AutoSize = true };

        public MainForm(Options o)
        {
            opt = o;
            Text = "꿈플 AutoCAD 2008/2009 연결기 (시험판 0.1)";
            Font = new Font("맑은 고딕", 9f);
            Size = new Size(640, 520); MinimumSize = new Size(520, 420);
            StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            var warn = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DarkRed,
                Text = "시험판입니다. 꿈플 모듈은 AutoCAD 2010용으로 만들어져 2008/2009에서 일부 기능이 동작하지 않을 수 있습니다.\r\n" +
                       "• 2009: 리본(꿈플 I·II 탭)·풀다운 메뉴·도구막대  • 2008: 리본이 없는 버전이라 풀다운 메뉴·도구막대로 표시\r\n" +
                       "• CAD 첫 실행 후 명령행의 'Registered N of M' 문구와 KKREPORT 보고서를 확인해 전달해 주세요."
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { close, remove, install });
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(warn, 0, 0);
            grid.Controls.Add(new Label { Text = "연결할 AutoCAD (2008/2009만 표시):", AutoSize = true, Margin = new Padding(0, 6, 0, 3) }, 0, 1);
            grid.Controls.Add(list, 0, 2);
            grid.Controls.Add(logBox, 0, 3);
            grid.Controls.Add(buttons, 0, 4);
            Controls.Add(grid);
            install.Click += (s, e) => Run(true);
            remove.Click += (s, e) => Run(false);
            close.Click += (s, e) => Close();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Load += (s, e) => Refresh2();
        }

        void Refresh2()
        {
            list.Items.Clear();
            foreach (var p in Engine.Discover(opt)) list.Items.Add(p, p.HasProfiles);
            if (list.Items.Count == 0) Log("이 PC에서 AutoCAD 2008/2009를 찾지 못했습니다. (2010 이상은 기존 꿈플 설치 프로그램을 사용하세요.)");
            install.Enabled = list.Items.Count > 0;
        }

        void Log(string s) { logBox.AppendText(s + Environment.NewLine); }

        void Run(bool doInstall)
        {
            try
            {
                UseWaitCursor = true; install.Enabled = remove.Enabled = false;
                if (doInstall)
                {
                    Engine.Install(opt, list.CheckedItems.Cast<CadProduct>().ToList(), Log);
                    Log("완료. AutoCAD 2008/2009를 실행하면 꿈플을 불러옵니다.");
                    MessageBox.Show(this, "연결을 마쳤습니다.\n\nAutoCAD를 실행한 뒤 명령행의 [Kkumple 2008-2009 TEST] 문구를 확인해 주세요.\n2009에서 리본 탭이 보이지 않으면 사용설명의 '리본 탭 표시' 절차를 따라 주세요.", "꿈플 시험판");
                }
                else
                {
                    if (MessageBox.Show(this, "꿈플 2008/2009 시험판을 제거할까요?\n\n먼저 AutoCAD에서 KKUNLOADMENU 명령으로 메뉴를 내린 뒤 제거하면 다음 실행 때 '메뉴 파일 없음' 경고가 나오지 않습니다.\n도면과 AutoCAD 설정은 변경하지 않습니다.", "제거 확인", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    Engine.Uninstall(opt, Log);
                }
            }
            catch (Exception ex) { Log("실패: " + ex.Message); MessageBox.Show(this, ex.Message, "꿈플 시험판", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { UseWaitCursor = false; remove.Enabled = true; install.Enabled = list.Items.Count > 0; }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var o = new Options();
            string mode = null, logFile = null; var products = new List<string>(); bool all = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--list": case "--install": case "--uninstall": mode = args[i]; break;
                    case "--all": all = true; break;
                    case "--product": products.Add(args[++i]); break;
                    case "--data-dir": o.DataDir = args[++i]; break;
                    case "--log": logFile = args[++i]; break;
                    case "--test-root": // 시험용: HKCU\<경로>\Machine, HKCU\<경로>\User 를 HKLM/HKCU 대신 사용
                        o.MachineHive = Registry.CurrentUser; o.MachineBase = args[++i] + @"\Machine";
                        o.UserBase = args[i] + @"\User"; o.TestRoots = true; break;
                    default: MessageBox.Show("알 수 없는 옵션: " + args[i]); return 2;
                }
            }
            if (mode == null)
            {
                Application.EnableVisualStyles();
                Application.Run(new MainForm(o));
                return 0;
            }
            var log = new StringBuilder();
            Action<string> write = s => log.AppendLine(s);
            int code = 0;
            try
            {
                var found = Engine.Discover(o);
                if (mode == "--list") foreach (var p in found) write(p.Id + "\t" + p.HasProfiles + "\t" + p.Name);
                else if (mode == "--install") Engine.Install(o, found.Where(p => all || products.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToList(), write);
                else Engine.Uninstall(o, write);
            }
            catch (Exception ex) { write("ERROR: " + ex.Message); code = 1; }
            if (logFile != null) File.WriteAllText(logFile, log.ToString(), new UTF8Encoding(true));
            else if (code != 0 || mode == "--list") MessageBox.Show(log.ToString(), "꿈플 시험판");
            return code;
        }
    }
}
