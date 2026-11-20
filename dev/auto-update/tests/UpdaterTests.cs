using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Kkumple.Update;

internal static class UpdaterTests
{
    private static string root, executable;
    private static string legacyExecutable;
    private static RSACryptoServiceProvider key;
    private static readonly List<string> passed = new List<string>();
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); passed.Add(name); }
    private static void Refused(Action action, string name)
    {
        bool refused = false; try { action(); } catch { refused = true; }
        Check(refused, name);
    }
    private static UpdateConfig Config()
    {
        return new UpdateConfig { AppId = "KKUMPLE_FREE_NONCOMMERCIAL_V2", Service = "꿈플",
            ManifestUrl = "https://example.invalid/update.json", ServiceEndStatusUrl = "https://example.invalid/status.json", PublicKeyXml = key.ToXmlString(false) };
    }
    private static UpdateManifest Manifest(string version)
    {
        return new UpdateManifest { schema = 1, appId = "KKUMPLE_FREE_NONCOMMERCIAL_V2", version = version,
            issuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"), expiresAtUtc = DateTimeOffset.UtcNow.AddDays(7).ToString("o"),
            packages = new List<UpdatePackage>() };
    }
    private static byte[] Signed(UpdateManifest manifest)
    {
        string payload = Encoding.UTF8.GetString(JsonXml.Bytes(manifest));
        return SignedPayload(payload);
    }
    private static byte[] SignedPayload(string payload)
    {
        byte[] signature;
        using (var sha = SHA256.Create())
        {
            var formatter = new RSAPKCS1SignatureFormatter(key); formatter.SetHashAlgorithm("SHA256");
            signature = formatter.CreateSignature(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
        return JsonXml.Bytes(new SignedEnvelope { payload = payload, signature = Convert.ToBase64String(signature) });
    }
    private sealed class Fixture
    {
        public string Bundle, Data, Family, Loader;
        public UpdateConfig Config;
        public UpdateEngine Engine;
    }
    private static Fixture Make(string name, bool gate)
    {
        var f = new Fixture { Bundle = Path.Combine(root, name, "bundle 한글 and spaces"), Data = Path.Combine(root, name, "updater 한글 and spaces"), Config = Config() };
        f.Family = Path.Combine(f.Bundle, "Contents", "Versions", "2015"); Directory.CreateDirectory(f.Family);
        f.Loader = Path.Combine(f.Bundle, "Contents", "Loaders", "original-loader.lsp"); Directory.CreateDirectory(Path.GetDirectoryName(f.Loader));
        File.WriteAllBytes(f.Loader, Encoding.ASCII.GetBytes("; original loader\r\n(setq fixture-original T)\r\n(princ)\r\n"));
        File.WriteAllText(Path.Combine(f.Bundle, ".kkumple-owner"), "KKUMPLE_FREE_NONCOMMERCIAL_V2", Encoding.UTF8);
        File.WriteAllText(Path.Combine(f.Bundle, "PackageContents.xml"),
            "<ApplicationPackage AppVersion=\"2.0.3\"><Components><RuntimeRequirements SupportPath=\"./Contents/Versions/2015\"/>"
            + "<ComponentEntry ModuleName=\"./Contents/Loaders/original-loader.lsp\" LoadOnAutoCADStartup=\"True\" PerDocument=\"True\"/>"
            + "</Components></ApplicationPackage>", Encoding.UTF8);
        File.WriteAllText(Path.Combine(f.Family, "KkCore0.dll"), "old program");
        File.WriteAllText(Path.Combine(f.Family, "user-note.txt"), "preserve user content");
        foreach (string ext in new[] { ".DWG", ".dxf", ".dwt", ".dws", ".bak", ".ini", ".cfg", ".settings" })
            File.WriteAllBytes(Path.Combine(f.Family, "user" + ext), new byte[] { 0, 3, 99, 255, 12 });
        GateInstaller.EnsureOwnedData(f.Data, true);
        f.Engine = new UpdateEngine(f.Bundle, f.Data, f.Config);
        if (gate) GateInstaller.Install(f.Bundle, f.Data, executable, f.Config);
        return f;
    }
    private static string Package(string name, params string[] files)
    {
        string file = Path.Combine(root, name + ".zip");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (string item in files)
            {
                var entry = archive.CreateEntry(item);
                using (var s = entry.Open()) { byte[] bytes = Encoding.UTF8.GetBytes("new program: " + item); s.Write(bytes, 0, bytes.Length); }
            }
        }
        return file;
    }
    private static void Bind(Fixture f, UpdateManifest manifest, string package)
    {
        if (package != null) manifest.packages.Add(new UpdatePackage { family = "2015", url = "https://example.invalid/package.zip",
            bytes = new FileInfo(package).Length, sha256 = Protocol.FileSha(package) });
        f.Engine.FetchMetadata = (url, seconds, max) => Signed(manifest);
        f.Engine.Download = (url, seconds, max, stream, progress) =>
        {
            byte[] bytes = File.ReadAllBytes(package); stream.Write(bytes, 0, bytes.Length); if (progress != null) progress(bytes.Length);
        };
    }
    private static bool LispBalanced(string text)
    {
        int depth = 0; bool quote = false, escaped = false, comment = false;
        foreach (char ch in text)
        {
            if (comment) { if (ch == '\n') comment = false; continue; }
            if (quote)
            {
                if (escaped) { escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == '"') quote = false;
                continue;
            }
            if (ch == ';') comment = true;
            else if (ch == '"') quote = true;
            else if (ch == '(') depth++;
            else if (ch == ')' && --depth < 0) return false;
        }
        return depth == 0 && !quote;
    }
    private static void Junction(string link, string target)
    {
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("Junction fixture creation failed.");
        }
    }
    private static void Tests()
    {
        var cfg = Config(); cfg.Validate();
        Check(!Protocol.VerifyEnded(SignedPayload("{\"service\":\"꿈플\",\"ended\":false}"), cfg), "signed normal service status permits updates");
        Check(Protocol.VerifyEnded(SignedPayload("{\"service\":\"꿈플\",\"ended\":true}"), cfg), "signed service end status blocks updates");
        Check(Protocol.VerifyEnded(SignedPayload(Encoding.UTF8.GetString(JsonXml.Bytes(new { service = "꿈플".Normalize(NormalizationForm.FormD), ended = true }))), cfg), "service status normalizes Hangul consistently");
        Check(!Protocol.VerifyEnded(SignedPayload(Encoding.UTF8.GetString(JsonXml.Bytes(new { service = "꿈플", ended = true, effectiveDateIso = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") }))), cfg), "future service end waits for its effective date");
        Refused(() => Protocol.VerifyEnded(SignedPayload("{\"service\":\"other\",\"ended\":true}"), cfg), "wrong service name cannot control updater startup");
        Refused(() => Protocol.VerifyEnded(SignedPayload("{\"service\":\"꿈플\",\"ended\":true,\"effectiveDateIso\":\"invalid\"}"), cfg), "invalid service end date is refused");
        Refused(() => Protocol.Verify(SignedPayload("{\"service\":\"꿈플\",\"ended\":true}"), cfg, DateTimeOffset.UtcNow), "signed service status cannot masquerade as an update manifest");
        Check(Protocol.VersionOf("2.0.3") == Protocol.VersionOf("2.0.3.0"), "versions with three and four fields compare equally");
        Refused(() => Protocol.VersionOf("v2.0.3"), "ambiguous version is refused");
        Check(Protocol.Verify(Signed(Manifest("2.0.3")), cfg, DateTimeOffset.UtcNow).version == "2.0.3", "valid signed baseline is accepted");
        var bad = Manifest("2.0.3"); bad.appId = "other";
        Refused(() => Protocol.Verify(Signed(bad), cfg, DateTimeOffset.UtcNow), "wrong product is refused");
        bad = Manifest("2.0.3"); bad.expiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1).ToString("o");
        Refused(() => Protocol.Verify(Signed(bad), cfg, DateTimeOffset.UtcNow), "expired metadata is refused");
        bad = Manifest("2.0.3"); bad.issuedAtUtc = DateTimeOffset.UtcNow.AddDays(1).ToString("o");
        Refused(() => Protocol.Verify(Signed(bad), cfg, DateTimeOffset.UtcNow), "future issued metadata is refused");
        string tampered = Encoding.UTF8.GetString(Signed(Manifest("2.0.3"))).Replace("2.0.3", "2.0.9");
        Refused(() => Protocol.Verify(Encoding.UTF8.GetBytes(tampered), cfg, DateTimeOffset.UtcNow), "tampered version signature is refused");
        Refused(() => Protocol.Verify(Encoding.UTF8.GetBytes("not json"), cfg, DateTimeOffset.UtcNow), "malformed metadata is refused");
        Refused(() => Net.RequireHttps("http://example.invalid/update.zip"), "HTTP downgrade is refused");
        Refused(() => Net.RequireHttps("https://user:secret@example.invalid/file"), "URL credentials are refused");
        var privateCfg = Config(); privateCfg.PublicKeyXml = key.ToXmlString(true);
        Refused(() => privateCfg.Validate(), "private key cannot be distributed in updater configuration");
        var shortCfg = Config(); shortCfg.CheckTimeoutSeconds = 0;
        Refused(() => shortCfg.Validate(), "invalid network limit is refused");

        var f = Make("startup-hook", false); byte[] loaderBefore = File.ReadAllBytes(f.Loader);
        GateInstaller.CheckPartnerAgent(f.Data);
        Check(true, "updater can be installed when no companion deletion agent exists");
        string partnerFile = Path.Combine(f.Data, "꿈플-서비스종료.exe");
        File.WriteAllText(partnerFile, "not a supported executable");
        File.WriteAllText(Path.Combine(f.Data, ".kkumple-service-end-owner"), "KKUMPLE_SERVICE_END_V1", Encoding.UTF8);
        Refused(() => GateInstaller.CheckPartnerAgent(f.Data), "unverifiable or old deletion agent is refused");
        if (legacyExecutable != null)
        {
            File.Copy(legacyExecutable, partnerFile, true);
            GateInstaller.CheckPartnerAgent(f.Data);
            Check(true, "current paired deletion agent version is accepted");
            var legacy = Assembly.LoadFile(legacyExecutable);
            string name = (string)legacy.GetType("KkumpleServiceEnd.BundleOperationLock").GetMethod("NameFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { f.Bundle });
            Check(name == GateInstaller.MutexName(f.Bundle), "deployed service-end agent and updater use exactly the same shared lock");
        }
        Check(GateInstaller.MutexName(f.Bundle) == GateInstaller.MutexName(f.Bundle.ToUpperInvariant()), "shared lock name is path-case independent");
        using (var mutex = new System.Threading.Mutex(false, GateInstaller.MutexName(f.Bundle)))
        {
            mutex.WaitOne(); bool otherThreadBlocked = false;
            var thread = new System.Threading.Thread(delegate()
            {
                using (var other = new System.Threading.Mutex(false, GateInstaller.MutexName(f.Bundle)))
                {
                    bool acquired = other.WaitOne(50); otherThreadBlocked = !acquired;
                    if (acquired) other.ReleaseMutex();
                }
            });
            thread.Start(); thread.Join(); mutex.ReleaseMutex();
            Check(otherThreadBlocked, "actual Windows shared lock excludes simultaneous mutations");
        }
        GateInstaller.Install(f.Bundle, f.Data, executable, f.Config);
        var installed = BundleInfo.Load(f.Bundle);
        string module = ((System.Xml.XmlElement)installed.Xml.SelectSingleNode("//ComponentEntry")).GetAttribute("ModuleName");
        Check(module.Contains("kkumple-update-gate-"), "PackageContents starts through the update gate");
        Check(Protocol.Sha(loaderBefore) == Protocol.FileSha(f.Loader), "original LISP bytes are never patched");
        string gateFile = SafePath.Under(f.Bundle, module.Substring(2)); string lisp = File.ReadAllText(gateFile, Encoding.ASCII);
        Check(lisp.All(ch => ch <= 127) && LispBalanced(lisp), "gate LISP is balanced ASCII source for legacy CAD");
        Check(lisp.Contains("'Run cmd 1 :vlax-true") && lisp.Contains("(= result 0)"), "LISP waits for helper success before loading the original");
        Check(lisp.Contains("vl-bb-ref") && lisp.Contains("vl-bb-set") && lisp.Contains("getenv \"APPDATA\""), "gate caches approval per CAD session and resolves user paths at runtime");
        Check(!lisp.Contains(root), "gate contains no developer machine absolute paths");
        GateInstaller.Install(f.Bundle, f.Data, executable, f.Config);
        Check(BundleInfo.Load(f.Bundle).Xml.SelectNodes("//ComponentEntry").Count == 1, "reinstall does not double-wrap the loader");
        Check(GateInstaller.Uninstall(f.Bundle, f.Data), "startup hook uninstall succeeds");
        Check(((System.Xml.XmlElement)BundleInfo.Load(f.Bundle).Xml.SelectSingleNode("//ComponentEntry")).GetAttribute("ModuleName") == "./Contents/Loaders/original-loader.lsp", "uninstall restores original autoload target");
        Check(!File.Exists(gateFile) && File.Exists(Path.Combine(f.Family, "KkCore0.dll")), "uninstall removes only the owned gate and retains body files");
        Check(!GateInstaller.Uninstall(f.Bundle, f.Data), "repeat startup hook uninstall is harmless");
        f = Make("body-reinstalled", true);
        var stateBeforeBodyInstall = JsonXml.Read<InstallState>(File.ReadAllBytes(Path.Combine(f.Data, "install-state.json")));
        File.WriteAllBytes(Path.Combine(f.Bundle, "PackageContents.xml"), Convert.FromBase64String(stateBeforeBodyInstall.originalXml));
        GateInstaller.Install(f.Bundle, f.Data, executable, f.Config);
        Check(((System.Xml.XmlElement)BundleInfo.Load(f.Bundle).Xml.SelectSingleNode("//ComponentEntry")).GetAttribute("ModuleName").Contains("kkumple-update-gate-"), "gate can reconnect after the original body installer recreates autoload XML");
        Refused(() => GateInstaller.CreateLisp("Contents/Loaders/한글.lsp"), "legacy gate rejects unsupported non-ASCII module filenames without corrupting them");

        f = Make("no-update", true); Bind(f, Manifest("2.0.3.0"), null);
        Check(f.Engine.Check() == null && File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program", "no update leaves installed files unchanged");
        f.Engine.FetchMetadata = (url, seconds, max) => { throw new IOException("offline fixture"); };
        Refused(() => f.Engine.Check(), "default mandatory online check blocks offline startup");
        f.Config.RequireOnlineCheck = false;
        Check(f.Engine.Check() == null, "explicit offline allowance permits verified installed version");

        string package = Package("valid-update", "KkCore0.dll", "new-feature.dll", "Images/new.png");
        f = Make("end-before-commit", true); int serviceChecks = 0;
        f.Engine.VerifyServiceActive = () => { serviceChecks++; if (serviceChecks == 2) throw new IOException("service ended fixture"); };
        bool serviceEndStoppedCommit = false;
        try { f.Engine.InstallZip(package, "2.0.4", null); }
        catch (IOException ex) { if (ex.Message == "service ended fixture") serviceEndStoppedCommit = true; else throw; }
        Check(serviceEndStoppedCommit, "service end detected during preparation prevents committing an update");
        Check(serviceChecks == 2 && File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program" && !File.Exists(Path.Combine(f.Data, "active-update.json")), "late service end preserves active files and creates no mutation journal");
        f = Make("full-update", true); Bind(f, Manifest("2.0.4"), package);
        var update = f.Engine.Check(); Check(update != null, "new signed version requires an update");
        f.Engine.Apply(update, null);
        Check(BundleInfo.Load(f.Bundle).Version == Protocol.VersionOf("2.0.4"), "successful update commits the installed version");
        Check(File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")).StartsWith("new program") && File.Exists(Path.Combine(f.Family, "new-feature.dll")), "verified package replaces old program and adds new files");
        Check(File.ReadAllText(Path.Combine(f.Family, "user-note.txt")) == "preserve user content", "unlisted user file is preserved");
        foreach (string ext in new[] { ".DWG", ".dxf", ".dwt", ".dws", ".bak", ".ini", ".cfg", ".settings" })
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(f.Family, "user" + ext))) == Convert.ToBase64String(new byte[] { 0, 3, 99, 255, 12 }), "user bytes preserved " + ext);
        Check(Directory.GetDirectories(Path.Combine(f.Bundle, "Contents", "Versions"), ".kkupd-backup-*").Length == 1, "old version is retained as a rollback backup");
        Check(!File.Exists(Path.Combine(f.Data, "active-update.json")), "completed update clears only its transaction journal");
        Check(GateInstaller.Uninstall(f.Bundle, f.Data) && BundleInfo.Load(f.Bundle).Version == Protocol.VersionOf("2.0.4"), "uninstall preserves the newly installed body version");

        f = Make("bad-hash", true); var wrongHash = Manifest("2.0.4"); Bind(f, wrongHash, package); wrongHash.packages[0].sha256 = new string('0', 64);
        Refused(() => f.Engine.Apply(f.Engine.Check(), null), "wrong package hash prevents installation");
        Check(File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program" && BundleInfo.Load(f.Bundle).Version == Protocol.VersionOf("2.0.3"), "hash failure preserves old files and version");
        f.Config.RequireOnlineCheck = false; f.Engine.FetchMetadata = (url, seconds, max) => { throw new IOException("offline"); };
        Refused(() => f.Engine.Check(), "cached mandatory update cannot be skipped by going offline");

        f = Make("bad-size", true); var wrongSize = Manifest("2.0.4"); Bind(f, wrongSize, package); wrongSize.packages[0].bytes++;
        Refused(() => f.Engine.Apply(f.Engine.Check(), null), "wrong package length prevents installation");
        f = Make("unsupported-family", true); Bind(f, Manifest("2.0.4"), null);
        Refused(() => f.Engine.Apply(f.Engine.Check(), null), "missing CAD-family package blocks old-version fallback");

        f = Make("rollback-metadata", true); Bind(f, Manifest("2.0.5"), null); f.Engine.Check(); Bind(f, Manifest("2.0.4"), null);
        Refused(() => f.Engine.Check(), "metadata rollback below highest seen version is refused");
        f = Make("expired-cache-refresh", true); var expired = Manifest("2.0.3"); expired.issuedAtUtc = DateTimeOffset.UtcNow.AddDays(-9).ToString("o"); expired.expiresAtUtc = DateTimeOffset.UtcNow.AddDays(-2).ToString("o");
        File.WriteAllBytes(Path.Combine(f.Data, "last-verified-update.json"), Signed(expired)); Bind(f, Manifest("2.0.3"), null);
        Check(f.Engine.Check() == null, "valid fresh metadata can replace an expired signed cache");

        foreach (string invalid in new[] { "../escape.dll", "nested/../../escape.dll", "C:/escape.dll", "file.dll:stream", "CON.dll", "folder./file.dll", "user.DWG", "user.ini", ".kkumple-update-transaction" })
        {
            f = Make("zip-refusal-" + passed.Count, true); string unsafeZip = Package("zip-" + passed.Count, invalid);
            Refused(() => f.Engine.InstallZip(unsafeZip, "2.0.4", null), "unsafe or protected ZIP entry refused: " + invalid);
            Check(File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program", "ZIP refusal does not alter active modules " + invalid);
        }
        f = Make("duplicate-zip", true); string duplicate = Package("duplicate", "Same.dll", "same.dll");
        Refused(() => f.Engine.InstallZip(duplicate, "2.0.4", null), "case-insensitive duplicate ZIP paths are refused");
        f = Make("expanded-limit", true); f.Config.MaxExpandedBytes = 2;
        Refused(() => f.Engine.InstallZip(package, "2.0.4", null), "expanded ZIP size limit is enforced");
        f = Make("entry-limit", true); f.Config.MaxEntries = 1;
        Refused(() => f.Engine.InstallZip(package, "2.0.4", null), "ZIP entry count limit is enforced");
        f = Make("locked-file", true);
        using (var locked = new FileStream(Path.Combine(f.Family, "KkCore0.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Refused(() => f.Engine.InstallZip(package, "2.0.4", null), "running CAD file locks prevent replacement");
        Check(File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program" && !File.Exists(Path.Combine(f.Data, "active-update.json")), "locked-file refusal happens before swapping folders");
        f.Engine.InstallZip(package, "2.0.4", null);
        Check(BundleInfo.Load(f.Bundle).Version == Protocol.VersionOf("2.0.4"), "update succeeds after lock is released");

        foreach (string checkpoint in new[] { "before-swap", "after-backup", "after-swap", "after-version" })
        {
            f = Make("rollback-" + checkpoint, true);
            f.Engine.TestCheckpoint = at => { if (at == checkpoint) throw new IOException("injected " + checkpoint); };
            Refused(() => f.Engine.InstallZip(package, "2.0.4", null), "injected failure is surfaced " + checkpoint);
            Check(BundleInfo.Load(f.Bundle).Version == Protocol.VersionOf("2.0.3") && File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program"
                && !File.Exists(Path.Combine(f.Data, "active-update.json")), "files metadata and journal roll back " + checkpoint);
            Check(GateInstaller.Uninstall(f.Bundle, f.Data), "startup ownership state also rolls back " + checkpoint);
        }

        f = Make("crash-recovery", true);
        string id = Guid.NewGuid().ToString("N"); string versions = Path.GetDirectoryName(f.Family);
        var journal = new UpdateJournal { id = id, family = "2015", oldXml = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(f.Bundle, "PackageContents.xml"))),
            oldInstallState = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(f.Data, "install-state.json"))) };
        File.WriteAllBytes(Path.Combine(f.Data, "active-update.json"), JsonXml.Bytes(journal));
        File.WriteAllText(Path.Combine(f.Family, ".kkumple-update-transaction"), id, Encoding.ASCII);
        SafePath.Move(versions, f.Family, Path.Combine(versions, ".kkupd-backup-" + id));
        f.Engine.Recover();
        Check(Directory.Exists(f.Family) && File.ReadAllText(Path.Combine(f.Family, "KkCore0.dll")) == "old program", "next startup recovers a crash between folder renames");

        f = Make("tampered-journal", true); File.WriteAllBytes(Path.Combine(f.Data, "active-update.json"), JsonXml.Bytes(new UpdateJournal { id = "../../outside", family = "2015" }));
        Refused(() => f.Engine.Recover(), "journal paths cannot escape the version folder");
        f = Make("foreign-data", false); string foreign = Path.Combine(root, "unmarked-data"); Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "keep.txt"), "keep");
        Refused(() => GateInstaller.EnsureOwnedData(foreign, true), "unmarked existing helper folder is preserved");
        Check(File.ReadAllText(Path.Combine(foreign, "keep.txt")) == "keep", "foreign helper folder bytes stay unchanged");
        f = Make("junction", true); string outside = Path.Combine(root, "outside-junction"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "keep.dll"), "external");
        string link = Path.Combine(f.Family, "escape"); Junction(link, outside);
        Refused(() => f.Engine.InstallZip(package, "2.0.4", null), "source junction cannot be copied into an update");
        Check(File.ReadAllText(Path.Combine(outside, "keep.dll")) == "external", "junction target remains unchanged"); Directory.Delete(link, false);

        f = Make("changed-gate", true); string changedGate = Directory.GetFiles(Path.GetDirectoryName(f.Loader), "kkumple-update-gate-*.lsp").Single(); File.AppendAllText(changedGate, "; user change");
        Refused(() => GateInstaller.Uninstall(f.Bundle, f.Data), "modified startup files are preserved on uninstall");
        f = Make("changed-xml", true); File.AppendAllText(Path.Combine(f.Bundle, "PackageContents.xml"), "<!-- user change -->");
        Refused(() => GateInstaller.Uninstall(f.Bundle, f.Data), "externally changed autoload XML is preserved");

        using (var form = new SetupForm())
        {
            var checkbox = form.Controls.OfType<CheckBox>().Single(); var button = form.Controls.OfType<Button>().Single(b => b.DialogResult == DialogResult.OK);
            Check(!checkbox.Checked && !button.Enabled, "update consent starts unchecked and install is disabled");
            checkbox.Checked = true; Check(button.Enabled, "update consent enables installation");
            checkbox.Checked = false; Check(!button.Enabled, "unchecking update consent disables installation");
            Check(form.Controls.OfType<TextBox>().Single().Text == GateInstaller.Disclosure, "exact mandatory-update disclosure is shown");
        }
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            foreach (int code in new[] { 0, 7 })
            {
                int result = Convert.ToInt32(shell.GetType().InvokeMember("Run", BindingFlags.InvokeMethod, null, shell,
                    new object[] { "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --child " + code, 0, true }));
                Check(result == code, "real WScript Run waits and returns child exit code " + code);
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--child") return int.Parse(args[1]);
        if (args.Length == 2 && args[0] == "--online")
        {
            try
            {
                var config = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(args[1])); config.Validate();
                bool ended = Protocol.VerifyEnded(Net.Get(config.ServiceEndStatusUrl, config.CheckTimeoutSeconds, 1048576), config);
                var verified = Protocol.Verify(Net.Get(config.ManifestUrl, config.CheckTimeoutSeconds, 1048576), config, DateTimeOffset.UtcNow);
                Console.WriteLine("Online verified: version=" + verified.version + "; ended=" + ended + "; packages=" + verified.packages.Count); return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        }
        if (args.Length == 3 && args[0] == "--verify")
        {
            try
            {
                var config = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(args[2])); config.Validate();
                var verified = Protocol.Verify(File.ReadAllBytes(args[1]), config, DateTimeOffset.UtcNow);
                Console.WriteLine("Verified manifest: " + verified.version + "; packages=" + verified.packages.Count); return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        }
        if (args.Length != 2 && args.Length != 3) return 64;
        root = Path.GetFullPath(args[0]); executable = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        legacyExecutable = args.Length == 3 ? Path.GetFullPath(args[2]) : null;
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string failure = null;
        using (key = new RSACryptoServiceProvider(2048))
        {
            key.PersistKeyInCsp = false;
            try { Tests(); } catch (Exception ex) { failure = ex.ToString(); }
        }
        File.WriteAllBytes(Path.Combine(root, "result.json"), JsonXml.Bytes(new { count = passed.Count, passed = passed, failure = failure }));
        Console.WriteLine("Updater: " + passed.Count + " passed" + (failure == null ? "" : "; " + failure));
        return failure == null ? 0 : 1;
    }
}
