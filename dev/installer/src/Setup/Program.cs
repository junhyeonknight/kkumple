using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Kkumple.Update;

[assembly: System.Reflection.AssemblyVersion("2.2.7.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.7.0")]

namespace Kkumple.Distribution
{
    internal sealed class ConsentSettings
    {
        public string ServiceName { get; set; }
        public string Version { get; set; }
        public string DisclosureText { get; set; }
    }
    internal sealed class CadStillRunningException : IOException
    {
        public CadStillRunningException() : base("AutoCAD가 종료 중입니다. 모든 CAD 창을 닫아 주세요.") { }
    }
    internal class InstallActions
    {
        public string Bundle = GateInstaller.DefaultBundle;
        public string Data = GateInstaller.DefaultData;
        public string Agent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Kkumple","ServiceEndAgent");
        public string Consent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Kkumple","InstallConsent","consent.json");
        public string Backups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Kkumple","InstallBackups");
        public virtual bool CadRunning() { return IsCadRunning(); }
        public static bool IsCadRunning()
        {
            Process[] processes = Process.GetProcessesByName("acad");
            try
            {
                foreach (Process process in processes)
                {
                    try { if (!process.HasExited) return true; }
                    catch (InvalidOperationException) { /* It ended after enumeration. */ }
                    catch (System.ComponentModel.Win32Exception) { return true; }
                }
                return false;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        public virtual byte[] Fetch(string url,int timeout,int limit) { return Net.Get(url,timeout,limit); }
        public virtual void OpenBody(string exe,string directory) { SetupTasks.Run(exe,"",directory,false); }
        public virtual void RegisterAgent(string script,string record) { RunScript(script,"-ConsentRecord \""+record+"\""); }
        public virtual void RemoveAgent(string script) { RunScript(script,"-Quiet"); }
        private static void RunScript(string script,string arguments)
        {
            string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            SetupTasks.Run(ps,"-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" "+arguments,Path.GetDirectoryName(script),true);
        }
    }
    internal static class SetupTasks
    {
        public static string CombinedDisclosure(string root)
        {
            var agent = JsonXml.Read<ConsentSettings>(File.ReadAllBytes(SafePath.Under(root, "payload/service-end/consent-config.json")));
            return "[설치 전에 읽어 주세요]\r\n\r\n다음 단계에서 사용할 CAD를 선택합니다. 이미 꿈플을 설치했어도 다른 CAD로 바꾸거나 다시 설치할 수 있습니다.\r\n\r\n"
                + "선택한 CAD에 설치한 뒤에는 ‘CAD 실행’을 누르지 말고 그 창을 닫아 주세요. 이 화면에서 나머지 설정을 마치면 설치가 완료됩니다.\r\n\r\n"
                + "관리자가 사용을 중단하면 꿈플이 삭제될 수 있습니다. 나중에 사용이 허용돼도 이미 삭제된 꿈플은 재설치해야 합니다.\r\n\r\n"
                + GateInstaller.Disclosure + "\r\n\r\n" + agent.DisclosureText;
        }
        public static void VerifyPayload(string root)
        {
            var files = JsonXml.Read<Dictionary<string, string>>(File.ReadAllBytes(SafePath.Under(root, "payload-hashes.json")));
            if (files == null || files.Count < 8) throw new InvalidDataException("설치 파일을 확인할 수 없습니다. 다시 받아 주세요.");
            foreach (string required in new[] { "payload/body/꿈플.exe", "payload/body/꿈플.exe.config", "payload/updater/Kkumple.Updater.exe",
                "payload/updater/updater-config.json", "payload/service-end/꿈플-서비스종료.exe", "payload/service-end/config.json",
                "payload/service-end/consent-config.json", "payload/service-end/agent-common.ps1", "payload/service-end/install-agent.ps1",
                "payload/service-end/remove-agent.ps1", "payload/service-end/자동점검-제거.cmd" })
                if (!files.ContainsKey(required)) throw new InvalidDataException("필수 구성의 검증 항목이 없습니다: " + required);
            foreach (var file in files)
            {
                string full = SafePath.Under(root, file.Key);
                if (!File.Exists(full) || !string.Equals(Protocol.FileSha(full), file.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("설치 파일이 없거나 달라졌습니다: " + file.Key);
            }
        }
        public static string WriteAgentConsent(string root, string recordFile)
        {
            var settings = JsonXml.Read<ConsentSettings>(File.ReadAllBytes(SafePath.Under(root, "payload/service-end/consent-config.json")));
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(settings.DisclosureText))).Replace("-", "");
            SafePath.Atomic(recordFile, JsonXml.Bytes(new { agreed = true, service = settings.ServiceName, version = settings.Version,
                timestampIso = DateTimeOffset.Now.ToString("o"), disclosureSha256 = hash,
                combinedDisclosureSha256 = Protocol.Sha(Encoding.UTF8.GetBytes(CombinedDisclosure(root))) }));
            return recordFile;
        }
        public static void Run(string exe, string args, string directory, bool hidden)
        {
            if (!File.Exists(exe)) throw new FileNotFoundException("설치 실행파일이 없습니다.", exe);
            using (var p = Process.Start(new ProcessStartInfo(exe, args)
                { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = hidden }))
            {
                if (p == null) throw new IOException("설치 프로그램을 시작하지 못했습니다.");
                p.WaitForExit();
                if (p.ExitCode != 0) throw new IOException("설치 단계가 중단되었습니다. 종료코드: " + p.ExitCode + " / " + Path.GetFileName(exe));
            }
        }
        public static void Install(string root, Action<string> progress)
        {
            Install(root,progress,new InstallActions());
        }
        public static void Install(string root, Action<string> progress, InstallActions actions)
        {
            if (actions.CadRunning()) throw new CadStillRunningException();
            progress("설치 준비와 인터넷 연결을 확인하고 있습니다..."); VerifyPayload(root);
            var cfg = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(SafePath.Under(root, "payload/updater/updater-config.json"))); cfg.Validate();
            if (Protocol.VerifyEnded(actions.Fetch(cfg.ServiceEndStatusUrl, cfg.CheckTimeoutSeconds, 1048576), cfg))
                throw new IOException("현재 관리자가 꿈플 사용을 중단했습니다. 관리자에게 문의해 주세요.");
            var remote = Protocol.Verify(actions.Fetch(cfg.ManifestUrl, cfg.CheckTimeoutSeconds, 1048576), cfg, DateTimeOffset.UtcNow);
            string bundle = actions.Bundle;
            using (var mutex = new Mutex(false, GateInstaller.MutexName(bundle)))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(TimeSpan.FromSeconds(240)); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("다른 꿈플 작업이 진행 중입니다. 잠시 후 다시 실행해 주세요.");
                    if (Protocol.VerifyEnded(actions.Fetch(cfg.ServiceEndStatusUrl, cfg.CheckTimeoutSeconds, 1048576), cfg))
                        throw new IOException("현재 관리자가 꿈플 사용을 중단했습니다. 관리자에게 문의해 주세요.");
                    if (actions.CadRunning()) throw new CadStillRunningException();
                    if(Directory.Exists(bundle))BundleInfo.LoadRootMarkerOnly(bundle);
                    string backup=SettingsBackup.Save(bundle,actions.Backups);
                    try
                    {
                        progress("사용할 CAD를 선택하는 창을 열었습니다. 설치한 뒤 그 창을 닫아 주세요.");
                        // The CAD picker is always needed, including when the same body version is installed.
                        actions.OpenBody(SafePath.Under(root,"payload/body/꿈플.exe"),SafePath.Under(root,"payload/body"));
                    }
                    finally { SettingsBackup.RestoreMissing(bundle,backup); }
                    if(actions.CadRunning())throw new IOException("설치를 마치려면 CAD를 닫고 이 창에서 ‘다음: CAD 선택’을 다시 눌러 주세요.");
                    var local=BundleInfo.Load(bundle);
                    progress("CAD를 열 때 새 버전을 확인하도록 설정하고 있습니다...");
                    GateInstaller.Install(bundle, actions.Data, SafePath.Under(root, "payload/updater/Kkumple.Updater.exe"), cfg);
                    if(Protocol.VerifyEnded(actions.Fetch(cfg.ServiceEndStatusUrl,cfg.CheckTimeoutSeconds,1048576),cfg))
                        throw new IOException("현재 관리자가 꿈플 사용을 중단했습니다. 관리자에게 문의해 주세요.");
                    if(Protocol.VersionOf(remote.version)>local.Version&&!remote.packages.Exists(p=>p.family==local.Family))
                        throw new IOException("선택한 CAD에 필요한 새 버전을 아직 준비 중입니다. 관리자에게 문의해 주세요.");
                    progress("자동 확인 기능을 설정하고 있습니다...");
                    string record = actions.Consent;
                    WriteAgentConsent(root, record);
                    string script = SafePath.Under(root, "payload/service-end/install-agent.ps1");
                    actions.RegisterAgent(script,record);
                    GateInstaller.CheckPartnerAgent(actions.Agent);
                    progress("설치를 마쳤습니다. 이제 평소처럼 AutoCAD를 실행하세요.");
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
        public static void Uninstall(string root,Action<string> progress) { Uninstall(root,progress,new InstallActions()); }
        public static void Uninstall(string root,Action<string> progress,InstallActions actions)
        {
            if(actions.CadRunning())throw new IOException("열려 있는 CAD를 모두 닫은 뒤 다시 시도해 주세요.");
            VerifyPayload(root);
            using(var mutex=new Mutex(false,GateInstaller.MutexName(actions.Bundle)))
            {
                bool held=false;
                try
                {
                    try{held=mutex.WaitOne(TimeSpan.FromSeconds(240));}catch(AbandonedMutexException){held=true;}
                    if(!held)throw new IOException("다른 꿈플 작업이 진행 중입니다. 잠시 후 다시 실행해 주세요.");
                    progress("삭제할 파일을 확인하고 있습니다...");
                    LocalRemoval.Preflight(actions.Bundle,actions.Data,actions.Agent);
                    progress("자동 확인 기능을 종료하고 있습니다...");
                    actions.RemoveAgent(SafePath.Under(root,"payload/service-end/remove-agent.ps1"));
                    progress("이 컴퓨터에서 꿈플을 삭제하고 있습니다...");
                    string log=Path.Combine(Path.GetDirectoryName(actions.Data),"uninstall.log");
                    LocalRemoval.RemoveBundle(actions.Bundle,log);
                    LocalRemoval.RemoveUpdater(actions.Data);
                    progress("삭제했습니다. 도면과 개인 설정은 그대로 보관됩니다.");
                }
                finally{if(held)mutex.ReleaseMutex();}
            }
        }
        public static void RecordError(Exception ex)
        {
            try
            {
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Kkumple");
                SafePath.Check(folder);Directory.CreateDirectory(folder);
                File.AppendAllText(SafePath.Under(folder,"setup.log"),DateTimeOffset.Now.ToString("o")+" "+ex+Environment.NewLine,Encoding.UTF8);
            }catch{}
        }
        public static string FriendlyError(Exception ex)
        {
            if(ex is WebException||ex is TimeoutException)return "인터넷 연결을 확인하고 다시 시도해 주세요. 계속 문제가 생기면 관리자에게 문의해 주세요.";
            if(ex is UnauthorizedAccessException)return "파일을 바꿀 수 없습니다. CAD와 꿈플을 모두 닫은 뒤 다시 시도해 주세요.";
            if(ex is InvalidDataException||ex is FileNotFoundException||ex is DirectoryNotFoundException)return "설치에 필요한 파일을 확인할 수 없습니다. 설치파일을 다시 받아 실행해 주세요.";
            if(ex.Message.StartsWith("현재 관리자가")||ex.Message.StartsWith("선택한 CAD")||ex.Message.StartsWith("다른 꿈플 작업")||ex.Message.StartsWith("열려 있는 CAD")||ex.Message.StartsWith("설치를 마치려면"))return ex.Message;
            return "작업을 끝내지 못했습니다. CAD를 모두 닫고 다시 시도해 주세요. 계속 문제가 생기면 관리자에게 문의해 주세요.";
        }
    }
    internal sealed class SetupForm : Form
    {
        private bool busy;
        internal static string InstalledVersion(string bundle)
        {
            try
            {
                SafePath.Check(bundle);
                if (!Directory.Exists(bundle) ||
                    (!File.Exists(Path.Combine(bundle, ".kkumple-owner")) &&
                     !File.Exists(Path.Combine(bundle, "PackageContents.xml"))))
                    return "미설치";
                var version = BundleInfo.Load(bundle).Version;
                return version.ToString(version.Revision == 0 ? 3 : 4);
            }
            catch (InvalidDataException) { return "확인할 수 없음"; }
            catch (IOException) { return "확인할 수 없음"; }
            catch (UnauthorizedAccessException) { return "확인할 수 없음"; }
            catch (System.Xml.XmlException) { return "확인할 수 없음"; }
        }

        internal static string HelperVersion
        {
            get { return typeof(SetupForm).Assembly.GetName().Version.ToString(3); }
        }

        public SetupForm(string root) : this(root, null, null, null) { }
        internal SetupForm(string root, Action<bool, Action<string>> operation, Func<bool> confirmRemoval, Action<string> reportFailure, string installedBundle = null)
        {
            Text = "꿈플 설치 및 관리"; Font = new Font("Malgun Gothic", 9.5f); StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(740, 650); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var title = new Label { Text = "꿈플을 설치하거나 사용할 CAD를 바꿔 보세요", Font = new Font("Malgun Gothic", 17f, FontStyle.Bold), AutoSize = true, Location = new Point(22, 18) };
            var hint = new Label { Text = "이미 설치했어도 다음 단계에서 CAD를 다시 선택할 수 있습니다.", AutoSize = true, Location = new Point(24, 59) };
            var body = new TextBox { Name = "disclosure", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White, Text = SetupTasks.CombinedDisclosure(root), TabStop = false };
            body.SetBounds(24, 134, 692, 310);
            var helper = new Label { Name = "helperVersion", Text = "설치 도우미 버전  " + HelperVersion, AutoSize = true, Location = new Point(24, 86) };
            var installed = new Label { Name = "installedVersion", AutoSize = true, Location = new Point(24, 106) };
            Action refreshVersion = () => installed.Text = "설치된 꿈플 버전  " + InstalledVersion(installedBundle ?? GateInstaller.DefaultBundle);
            refreshVersion();
            Activated += delegate { if (!busy) refreshVersion(); };
            var agree = new CheckBox { Name = "agreement", Text = "위 내용을 읽고 동의합니다." }; agree.SetBounds(24, 460, 690, 28);
            const string ReadyText = "CAD를 모두 닫고 진행해 주세요. 다음 단계에서 설치할 CAD를 선택합니다.";
            const string NextText = "다음: CAD 선택";
            var status = new Label { Name = "status", Text = ReadyText, AutoSize = false }; status.SetBounds(24, 505, 692, 54);
            var install = new Button { Name = "installButton", Text = NextText, Enabled = false }; install.SetBounds(24, 590, 184, 36);
            var remove = new Button { Name = "removeButton", Text = "이 컴퓨터에서 삭제" }; remove.SetBounds(225, 590, 184, 36);
            var close = new Button { Name = "closeButton", Text = "닫기", DialogResult = DialogResult.Cancel }; close.SetBounds(602, 590, 114, 36);
            var waitForCad = new System.Windows.Forms.Timer { Interval = 350 };
            Action endWaiting = () =>
            {
                waitForCad.Stop();
                install.Text = NextText;
                remove.Enabled = true;
            };
            Action startWaiting = () =>
            {
                install.Text = "CAD 종료 다시 확인";
                remove.Enabled = false;
                status.Text = "AutoCAD를 닫아 주세요. 창을 닫은 뒤 종료가 끝나면 자동으로 CAD 선택으로 넘어갑니다. 닫기로 대기를 취소할 수 있습니다.";
                waitForCad.Start();
            };
            agree.CheckedChanged += delegate
            {
                install.Enabled = agree.Checked && !busy;
                if (!agree.Checked && waitForCad.Enabled)
                {
                    endWaiting();
                    status.Text = ReadyText;
                }
            };
            Action<bool> run = removing =>
            {
                if(removing && !(confirmRemoval != null ? confirmRemoval() : MessageBox.Show(this,"이 컴퓨터에서 꿈플과 자동 확인 기능을 삭제할까요?\n\n도면과 개인 설정은 보관합니다. 다른 컴퓨터에는 영향을 주지 않습니다.","꿈플 삭제",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) == DialogResult.Yes)) return;
                busy = true; install.Enabled = false; remove.Enabled = false; agree.Enabled = false; close.Enabled = false;
                status.Text = removing ? "삭제를 준비하고 있습니다..." : "다음 단계로 이동하고 있습니다...";
                var worker = new Thread(delegate()
                {
                    Exception failure = null;
                    Action<string> progress = text => BeginInvoke((Action)(() => status.Text = text));
                    try { if(operation != null) operation(removing,progress); else if(removing)SetupTasks.Uninstall(root,progress);else SetupTasks.Install(root,progress); }
                    catch (Exception ex)
                    {
                        failure = ex;
                        if (operation == null && !(ex is CadStillRunningException)) SetupTasks.RecordError(ex);
                    }
                    BeginInvoke((Action)(() =>
                    {
                        busy = false; close.Enabled = true; remove.Enabled = true; agree.Enabled = true; install.Enabled = agree.Checked;
                        refreshVersion();
                        if (failure is CadStillRunningException && !removing) startWaiting();
                        else if (failure == null) status.Text = removing ? "삭제했습니다. 다시 설치하려면 ‘다음: CAD 선택’을 누르세요." : "설치를 마쳤습니다. 이제 평소처럼 CAD를 실행하세요.";
                        else
                        {
                            status.Text = removing ? "삭제를 끝내지 못했습니다. 안내를 확인하고 다시 시도해 주세요." : "설치를 끝내지 못했습니다. 안내를 확인하고 다시 시도해 주세요.";
                            if(reportFailure != null) reportFailure(SetupTasks.FriendlyError(failure));
                            else MessageBox.Show(this, SetupTasks.FriendlyError(failure), removing ? "삭제 안내" : "설치 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }));
                });
                worker.IsBackground = true; worker.Start();
            };
            Action continueWhenClosed = () =>
            {
                if (busy || !agree.Checked) return;
                if (InstallActions.IsCadRunning())
                {
                    if (!waitForCad.Enabled) startWaiting();
                    return;
                }
                endWaiting();
                run(false);
            };
            waitForCad.Tick += delegate { continueWhenClosed(); };
            Disposed += delegate { waitForCad.Dispose(); };
            install.Click += delegate { continueWhenClosed(); };
            remove.Click += delegate { run(true); };
            close.Click += delegate { if (!busy) Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!busy) return;
                e.Cancel = true;
                status.Text = "지금은 작업을 마무리하고 있습니다. CAD 선택 창이 열려 있다면 먼저 그 창을 닫아 주세요.";
            };
            Controls.AddRange(new Control[] { title, hint, helper, installed, body, agree, status, install, remove, close }); CancelButton = close;
        }
    }
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // CAD 시작 시 꿈플 메뉴 LISP가 화면 없이 실행: 기억해 둔 설치 도우미 파일을 최신으로 바꾼다.
            if (Array.IndexOf(args, "--refresh-installer") >= 0) return SelfUpdate.RefreshRecordedInstallers();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            // 켜자마자 새 설치 도우미를 확인한다. 새 버전을 실행했으면 이 프로세스는 끝낸다.
            if (SelfUpdate.Run(args)) return 0;
            try
            {
                using (var payload = EmbeddedPayload.OpenDefault())
                using (var form = new SetupForm(payload.Root)) Application.Run(form);
                return 0;
            }
            catch (Exception ex) { SetupTasks.RecordError(ex); MessageBox.Show(SetupTasks.FriendlyError(ex), "설치 안내"); return 1; }
        }
    }
}
