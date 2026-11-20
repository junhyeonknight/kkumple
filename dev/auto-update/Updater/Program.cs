using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

namespace Kkumple.Update
{
    internal static class Program
    {
        private static readonly string Data = GateInstaller.DefaultData;
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string mode = args.Length == 0 ? "--install" : args[0];
            if (args.Length > 1 || (mode != "--install" && mode != "--uninstall" && mode != "--check")) return 64;
            try
            {
                if (mode != "--check" && Process.GetProcessesByName("acad").Length != 0)
                    throw new IOException("자동 업데이트 시작 연결을 설치/제거하려면 열려 있는 AutoCAD를 먼저 종료해 주세요. 도면과 프로그램은 변경하지 않았습니다.");
                if (mode == "--install")
                {
                    using (var form = new SetupForm()) if (form.ShowDialog() != DialogResult.OK) return 1;
                }
                if (mode == "--uninstall" && MessageBox.Show("꿈플 자동 업데이트 시작 연결을 해제합니다. 꿈플 본체와 도면, 이전 버전 백업, 로그는 보존합니다.",
                    "자동 업데이트 제거", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1;
                string mutexName = GateInstaller.MutexName(GateInstaller.DefaultBundle);
                using (var mutex = new Mutex(false, mutexName))
                {
                    bool held = false;
                    try
                    {
                        try { held = mutex.WaitOne(TimeSpan.FromSeconds(240)); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("다른 CAD가 꿈플 업데이트를 처리 중입니다. 잠시 후 CAD를 다시 시작해 주세요.");
                        if (mode == "--uninstall")
                        {
                            GateInstaller.EnsureOwnedData(Data, false);
                            string configPath = SafePath.Under(Data, "updater-config.json");
                            var config = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(configPath)); config.Validate();
                            new UpdateEngine(GateInstaller.DefaultBundle, Data, config).Recover();
                            GateInstaller.Uninstall(GateInstaller.DefaultBundle, Data); Log("Startup gate removed.");
                            MessageBox.Show("자동 업데이트 시작 연결을 해제했습니다. 점검기 파일과 기록은 보존했습니다.", "제거 완료"); return 0;
                        }
                        string directory = mode == "--check" ? Data : Path.GetDirectoryName(Application.ExecutablePath);
                        SafePath.Check(directory);
                        var cfg = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(SafePath.Under(directory, "updater-config.json")));
                        cfg.Validate();
                        GateInstaller.CheckPartnerAgent(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kkumple", "ServiceEndAgent"));
                        if (mode == "--install")
                        {
                            RequireServiceActive(cfg);
                            var local = BundleInfo.Load(GateInstaller.DefaultBundle);
                            var remote = Protocol.Verify(Net.Get(cfg.ManifestUrl, cfg.CheckTimeoutSeconds, 1048576), cfg, DateTimeOffset.UtcNow);
                            if (Protocol.VersionOf(remote.version) > local.Version && !remote.packages.Exists(p => p.family == local.Family))
                                throw new IOException("이 CAD 계열의 업데이트 파일이 온라인에 준비되지 않았습니다.");
                            GateInstaller.Install(GateInstaller.DefaultBundle, Data, Application.ExecutablePath, cfg);
                            Log("Startup gate installed. Online check required=" + cfg.RequireOnlineCheck);
                            MessageBox.Show("자동 업데이트 시작 연결을 설치했습니다.\n다음 AutoCAD 시작부터 꿈플을 불러오기 전에 업데이트를 확인합니다.", "설치 완료"); return 0;
                        }
                        GateInstaller.EnsureOwnedData(Data, false);
                        RequireServiceActive(cfg);
                        var engine = new UpdateEngine(GateInstaller.DefaultBundle, Data, cfg);
                        engine.VerifyServiceActive = () => RequireServiceActive(cfg);
                        var update = engine.Check();
                        if (update != null)
                        {
                            using (var progress = new UpdateForm(engine, update))
                            {
                                progress.ShowDialog();
                                if (progress.Failure != null) throw progress.Failure;
                            }
                            Log("Updated to " + update.version);
                        }
                        else Log("Check complete. Installed version may load.");
                        return 0;
                    }
                    finally { if (held) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex);
                MessageBox.Show((mode == "--check" ? "업데이트 확인/설치에 실패하여 이번 시작에서는 꿈플을 불러오지 않습니다.\nAutoCAD와 도면은 계속 사용할 수 있습니다.\n\n" : "작업을 완료하지 못했습니다.\n\n")
                    + ex.Message, "꿈플 자동 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }
        }
        private static void RequireServiceActive(UpdateConfig cfg)
        {
            byte[] bytes;
            string cache = SafePath.Under(Data, "last-verified-service-status.json");
            try { bytes = Net.Get(cfg.ServiceEndStatusUrl, cfg.CheckTimeoutSeconds, 1048576); }
            catch
            {
                if (cfg.RequireOnlineCheck) throw;
                GateInstaller.EnsureOwnedData(Data, false);
                bytes = File.ReadAllBytes(cache);
            }
            bool ended = Protocol.VerifyEnded(bytes, cfg);
            if (File.Exists(SafePath.Under(Data, ".kkumple-updater-owner")))
            { GateInstaller.EnsureOwnedData(Data, false); SafePath.Atomic(cache, bytes); }
            if (ended)
                throw new IOException("운영자가 꿈플 지원 종료를 공지했습니다. 자동 업데이트와 꿈플 시작을 중단합니다.");
        }
        private static void Log(string message)
        {
            try
            {
                GateInstaller.EnsureOwnedData(Data, false);
                File.AppendAllText(SafePath.Under(Data, "updater.log"), DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
    internal sealed class SetupForm : Form
    {
        public SetupForm()
        {
            Text = "꿈플 자동 업데이트 설치 및 동의"; Font = new Font("Malgun Gothic", 9f);
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(660, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var disclosure = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = GateInstaller.Disclosure, BackColor = Color.White, TabStop = false };
            disclosure.SetBounds(16, 16, 628, 404);
            var consent = new CheckBox { Text = "위 내용을 읽고 자동 업데이트에 동의합니다.", Checked = false };
            consent.SetBounds(16, 436, 628, 24);
            var install = new Button { Text = "동의하고 설치", Enabled = false, DialogResult = DialogResult.OK };
            install.SetBounds(394, 482, 122, 36);
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel }; cancel.SetBounds(526, 482, 118, 36);
            consent.CheckedChanged += delegate { install.Enabled = consent.Checked; };
            Controls.AddRange(new Control[] { disclosure, consent, install, cancel }); AcceptButton = install; CancelButton = cancel;
        }
    }
    internal sealed class UpdateForm : Form
    {
        private bool completed;
        public Exception Failure { get; private set; }
        public UpdateForm(UpdateEngine engine, UpdateManifest manifest)
        {
            Text = "꿈플 필수 업데이트"; Font = new Font("Malgun Gothic", 10f); ClientSize = new Size(500, 146);
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ControlBox = false;
            var label = new Label { Text = "꿈플 " + manifest.version + " 업데이트를 준비합니다...", AutoSize = false };
            label.SetBounds(20, 20, 460, 64);
            var bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25 }; bar.SetBounds(20, 100, 460, 22);
            Controls.AddRange(new Control[] { label, bar });
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!completed) e.Cancel = true; };
            Shown += delegate
            {
                var worker = new Thread(delegate()
                {
                    try { engine.Apply(manifest, message => { if (!IsDisposed) BeginInvoke((Action)(() => label.Text = message)); }); }
                    catch (Exception ex) { Failure = ex; }
                    finally { if (!IsDisposed) BeginInvoke((Action)(() => { completed = true; Close(); })); }
                });
                worker.IsBackground = true; worker.Start();
            };
        }
    }
}
