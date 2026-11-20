using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// 꿈플 설치 전 '고지 + 동의' 게이트
// --------------------------------------------------------------------------
// - 사용자가 설치 전에 '원격으로 지원 종료 시 꿈플 구성요소만 제거됨'을 고지받고 명시적으로 동의해야만 설치 진행
// - 동의하지 않으면 설치 중단(exit 1)
// - 동의 시 consent.json(동의 시각·고지문 해시·버전) 기록 후, 설정된 실제 설치 프로그램 실행(선택)
namespace KkumpleInstallConsent
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                ConsentConfig cfg = ConsentConfig.LoadNearExe();
                using (var f = new ConsentForm(cfg))
                    if (f.ShowDialog() != DialogResult.OK) return 1;

                string consentPath = RecordConsent(cfg);
                string packageDir = Path.GetDirectoryName(Application.ExecutablePath);
                // 실제 꿈플 설치파일이 별도로 제공된 경우, 성공 종료를 기다린 뒤 점검기를 등록.
                if (!string.IsNullOrWhiteSpace(cfg.InstallerExe))
                {
                    string exe = Environment.ExpandEnvironmentVariables(cfg.InstallerExe);
                    if (!Path.IsPathRooted(exe)) exe = Path.Combine(packageDir, exe);
                    RunStep(exe, cfg.InstallerArgs ?? "", packageDir, false);
                }
                if (cfg.RegisterAgentAfterInstall)
                {
                    string script = Path.Combine(packageDir, "install-agent.ps1");
                    if (!File.Exists(script)) throw new FileNotFoundException("자동 점검 설치 스크립트가 없습니다.", script);
                    string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe");
                    RunStep(powershell, "-NoProfile -ExecutionPolicy Bypass -File \"" + script
                        + "\" -ConsentRecord \"" + consentPath + "\"", packageDir, true);
                    MessageBox.Show("꿈플 자동 점검기를 설치했습니다.\n매일 오전 9시 사용자 권한으로 확인합니다.\n설치 위치: %LOCALAPPDATA%\\Kkumple\\ServiceEndAgent",
                        "설치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("설치를 완료하지 못했습니다.\n" + ex.Message
                    + "\n자세한 기록: %LOCALAPPDATA%\\Kkumple\\setup.log", "설치 실패",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        private static void RunStep(string exe, string args, string workingDir, bool hidden)
        {
            if (!File.Exists(exe)) throw new FileNotFoundException("실행파일이 없습니다.", exe);
            using (var p = Process.Start(new ProcessStartInfo(exe, args)
                { WorkingDirectory = workingDir, UseShellExecute = false, CreateNoWindow = hidden }))
            {
                if (p == null) throw new InvalidOperationException("프로세스를 시작하지 못했습니다.");
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("설치 단계 실패(종료코드 " + p.ExitCode + "): " + Path.GetFileName(exe));
            }
        }

        private static string RecordConsent(ConsentConfig cfg)
        {
            string path = cfg.ResolveConsentRecordPath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string hash;
            using (var sha = new SHA256Managed())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(cfg.DisclosureText ?? ""))).Replace("-", "");

            var rec = new ConsentRecord();
            rec.agreed = true;
            rec.service = cfg.ServiceName;
            rec.version = cfg.Version;
            rec.timestampIso = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ssK");
            rec.disclosureSha256 = hash;

            var ser = new JavaScriptSerializer();
            File.WriteAllText(path, ser.Serialize(rec), Encoding.UTF8);
            return path;
        }
    }

    internal sealed class ConsentForm : Form
    {
        public ConsentForm(ConsentConfig cfg)
        {
            Text = cfg.Title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 620;
            Height = 520;
            Font = new Font("Malgun Gothic", 9f);

            var body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.Text = cfg.DisclosureText;
            body.SetBounds(16, 16, 572, 360);
            body.BackColor = Color.White;
            body.TabStop = false;

            var agree = new CheckBox();
            agree.Text = "위 내용을 읽고 이해했으며, 이에 동의합니다.";
            agree.SetBounds(16, 388, 572, 24);

            var ok = new Button();
            ok.Text = "동의하고 설치";
            ok.DialogResult = DialogResult.OK;
            ok.Enabled = false;
            ok.SetBounds(352, 428, 116, 36);

            var cancel = new Button();
            cancel.Text = "동의 안 함(취소)";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(472, 428, 116, 36);

            agree.CheckedChanged += delegate { ok.Enabled = agree.Checked; };

            Controls.Add(body);
            Controls.Add(agree);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal sealed class ConsentConfig
    {
        public string ServiceName { get; set; }
        public string Title { get; set; }
        public string DisclosureText { get; set; }
        public string InstallerExe { get; set; }
        public string InstallerArgs { get; set; }
        public string ConsentRecordPath { get; set; }
        public string Version { get; set; }
        public bool RegisterAgentAfterInstall { get; set; }

        public ConsentConfig()
        {
            ServiceName = "꿈플";
            Title = "꿈플 설치 전 안내 및 동의";
            Version = "2.2.0";
            RegisterAgentAfterInstall = true;
            InstallerExe = "";   // 동의 후 실행할 실제 설치 프로그램(예: 꿈플.exe). 비우면 실행 안 함.
            InstallerArgs = "";
            ConsentRecordPath = ""; // 비우면 현재 사용자 LocalAppData에 저장.
            DisclosureText =
                "[꿈플 설치 전 안내 및 동의]\r\n\r\n" +
                "• 꿈플은 DreamPlus 기반의 무료·비상업적 파생 도구입니다.\r\n\r\n" +
                "• 지원 종료(서비스 종료) 시, 원격 신호에 따라 '꿈플 구성요소'가 자동으로 제거될 수 있습니다.\r\n\r\n" +
                "• 이때 사용자의 도면 파일(.dwg 등)·개인 데이터·AutoCAD 본체는 삭제되지 않습니다.\r\n\r\n" +
                "• 이를 위해 꿈플은 주기적으로 '서명된 상태 파일'을 온라인으로 확인합니다. 이 과정에서 개인정보·식별정보는 전송하지 않습니다.\r\n\r\n" +
                "• 원격 종료 신호는 운영자의 전자서명이 확인된 경우에만 적용됩니다.\r\n\r\n" +
                "위 내용에 동의하셔야 설치가 진행됩니다. 동의하지 않으시면 설치가 취소됩니다.";
        }

        public static ConsentConfig LoadNearExe()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "consent-config.json");
                if (File.Exists(path))
                {
                    var ser = new JavaScriptSerializer();
                    ConsentConfig c = ser.Deserialize<ConsentConfig>(File.ReadAllText(path, Encoding.UTF8));
                    if (c != null)
                    {
                        if (string.IsNullOrEmpty(c.DisclosureText)) c.DisclosureText = new ConsentConfig().DisclosureText;
                        if (string.IsNullOrEmpty(c.Title)) c.Title = "꿈플 설치 전 안내 및 동의";
                        if (string.IsNullOrEmpty(c.ServiceName)) c.ServiceName = "꿈플";
                        return c;
                    }
                }
            }
            catch (Exception ex) { throw new InvalidDataException("동의 설정 파일을 읽을 수 없습니다.", ex); }
            throw new FileNotFoundException("consent-config.json이 없습니다.");
        }

        public string ResolveConsentRecordPath()
        {
            if (!string.IsNullOrWhiteSpace(ConsentRecordPath)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(ConsentRecordPath));
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Kkumple", "InstallConsent", "consent.json");
        }
    }

    internal sealed class ConsentRecord
    {
        public bool agreed { get; set; }
        public string service { get; set; }
        public string version { get; set; }
        public string timestampIso { get; set; }
        public string disclosureSha256 { get; set; }
    }
}
