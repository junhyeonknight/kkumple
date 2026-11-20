using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using KkumpleServiceEnd;

internal static class AgentSafetyTests
{
    private static string root;
    private static Logger log;
    private static readonly List<string> passed = new List<string>();
    private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
    private static RSACryptoServiceProvider key;
    private const string Marker = "KKUMPLE_FREE_NONCOMMERCIAL_V2";

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        passed.Add(name);
    }

    private static string Bundle(string name, bool marker)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.dll"), "synthetic program");
        if (marker) File.WriteAllText(Path.Combine(dir, ".kkumple-owner"), Marker);
        return dir;
    }

    private static RemovalGuard Guard(string allowed)
    {
        return new RemovalGuard(new List<string> { allowed },
            new List<string> { ".dwg", ".dxf", ".dwt", ".dws", ".bak" }, true,
            ".kkumple-owner", Marker, log);
    }

    private static string Signed(string service, bool ended, string date)
    {
        string payload = serializer.Serialize(new { service = service, ended = ended, effectiveDateIso = date });
        byte[] signature;
        using (var sha = SHA256.Create())
        {
            var formatter = new RSAPKCS1SignatureFormatter(key);
            formatter.SetHashAlgorithm("SHA256");
            signature = formatter.CreateSignature(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
        return serializer.Serialize(new { payload = payload, signature = Convert.ToBase64String(signature) });
    }

    private static T Serve<T>(string body, Func<string, T> action)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Exception serverFailure = null;
        var thread = new Thread(delegate()
        {
            try
            {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    string headers = "";
                    while (!headers.EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int b = stream.ReadByte();
                        if (b < 0) break;
                        headers += (char)b;
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                        + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex) { serverFailure = ex; }
        });
        thread.IsBackground = true;
        thread.Start();
        try { return action("http://127.0.0.1:" + port + "/status.json"); }
        finally
        {
            listener.Stop();
            if (!thread.Join(10000)) throw new Exception("Loopback server did not stop.");
            if (serverFailure != null) throw new Exception("Loopback server failed", serverFailure);
        }
    }

    private static bool Remote(string body, string service)
    {
        return Serve(body, url => RemoteStatus.SaysEnded(new Config
        { ServiceName = service, StatusUrl = url, UpdatePublicKeyXml = key.ToXmlString(false) }, log));
    }

    private static int RunAgent(string url, string bundle, bool dryRun)
    {
        var cfg = new Config { ServiceName = "꿈플", StatusUrl = url,
            UpdatePublicKeyXml = key.ToXmlString(false), DryRun = dryRun, SelfRemove = false,
            NoticeMode = "silent", OwnerMarkerMustContain = Marker, ScheduledTaskName = "",
            AllowedBaseDirs = new List<string> { bundle }, PathsToRemove = new List<string> { bundle } };
        string file = Path.Combine(root, "fixture-config.json");
        File.WriteAllText(file, serializer.Serialize(cfg), Encoding.UTF8);
        return (int)typeof(KkumpleServiceEnd.Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { new[] { "--config", file } });
    }

    private static void Junction(string link, string target)
    {
        using (Process p = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("Could not create isolated junction fixture.");
        }
    }

    private static void Tests()
    {
        Check(!Remote(Signed("꿈플", false, ""), "꿈플"), "signed alive does not activate");
        Check(Remote(Signed("꿈플", true, ""), "꿈플"), "signed kill activates");
        Check(!Remote(Signed("꿈플", true, DateTime.Now.AddDays(2).ToString("yyyy-MM-dd")), "꿈플"), "future kill waits");
        Check(!Remote(Signed("꿈플", true, "invalid-date"), "꿈플"), "invalid effective date is refused");
        Check(!Remote(Signed("other", true, ""), "꿈플"), "wrong service is refused");
        Check(!Remote(Signed("", true, ""), "꿈플"), "empty service is refused");
        Check(Remote(Signed("꿈플".Normalize(NormalizationForm.FormD), true, ""), "꿈플"), "Hangul NFC and NFD match");
        Check(!Remote(Signed("꿈플", false, "").Replace("false", "true"), "꿈플"), "tampered signature is refused");
        Check(!Remote("{\"payload\":\"{}\",\"signature\":\"bad!\"}", "꿈플"), "malformed signature is refused");
        Check(!Remote("not json", "꿈플"), "malformed response is refused");
        Check(Remote("\uFEFF" + Signed("꿈플", true, ""), "꿈플"), "UTF8 BOM is accepted");
        Check(!RemoteStatus.SaysEnded(new Config { StatusUrl = "http://127.0.0.1:1/", UpdatePublicKeyXml = key.ToXmlString(false), CheckTimeoutSec = 3 }, log), "offline does not activate");
        Check(!RemoteStatus.SaysEnded(new Config { StatusUrl = "http://127.0.0.1:1/" }, log), "missing public key does not activate");
        Check(!new Config().IsExpired(log), "empty offline expiry is disabled");
        Check(!new Config { ExpiryDateIso = "invalid" }.IsExpired(log), "invalid offline expiry is disabled");

        string dir = Bundle("dry run 한글 and spaces", true);
        Check(Guard(dir).Remove(dir, true) && File.Exists(Path.Combine(dir, "plugin.dll")), "dry run preserves program files");
        Check(Guard(dir).Remove(dir, false) && !Directory.Exists(dir), "owned bundle is completely removed");
        Check(Guard(dir).Remove(dir, false), "already absent bundle succeeds");

        dir = Bundle("no-marker", false);
        Check(!Guard(dir).Remove(dir, false) && File.Exists(Path.Combine(dir, "plugin.dll")), "missing owner marker refuses removal");
        File.WriteAllText(Path.Combine(dir, ".kkumple-owner"), "wrong-owner");
        Check(!Guard(dir).Remove(dir, false) && File.Exists(Path.Combine(dir, "plugin.dll")), "wrong owner marker refuses removal");

        string allowed = Bundle("allow", true);
        string sibling = Bundle("allow-sibling", true);
        string reason;
        var guard = Guard(allowed);
        Check(!guard.Remove(sibling, false) && Directory.Exists(sibling), "similar prefix outside allowlist is preserved");
        Check(!new RemovalGuard(new List<string>(), new List<string>(), true, ".kkumple-owner", Marker, log).IsAllowed(allowed, out reason), "empty allowlist refuses removal");
        Check(!guard.IsAllowed(allowed + "\\*", out reason), "wildcards are refused");
        Check(!guard.IsAllowed("relative\\bundle", out reason), "relative paths are refused");
        Check(!guard.IsAllowed(allowed + "\\plugin.dll:stream", out reason), "alternate streams are refused");
        Check(!guard.IsAllowed(allowed + " ", out reason), "ambiguous trailing space is refused");
        Check(!guard.IsAllowed(@"\\server\share\bundle", out reason), "network paths are refused");
        string driveRoot = Path.GetPathRoot(root);
        Check(!Guard(driveRoot).IsAllowed(driveRoot, out reason), "drive root is refused even if allowlisted");
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Check(!Guard(user).IsAllowed(user, out reason), "user profile root is refused");
        string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "synthetic-bundle");
        Check(!Guard(system).IsAllowed(system, out reason), "Windows descendants are refused even if allowlisted");

        dir = Bundle("protected", true);
        string nested = Path.Combine(dir, "nested"); Directory.CreateDirectory(nested);
        byte[] document = new byte[] { 0, 1, 2, 255, 7, 8 };
        foreach (string ext in new[] { ".DWG", ".dxf", ".dwt", ".dws", ".bak" })
            File.WriteAllBytes(Path.Combine(nested, "drawing" + ext), document);
        Check(Guard(dir).Remove(dir, false), "removal succeeds while preserving protected drawings");
        foreach (string ext in new[] { ".DWG", ".dxf", ".dwt", ".dws", ".bak" })
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(nested, "drawing" + ext))) == Convert.ToBase64String(document), "protected bytes unchanged " + ext);
        Check(!File.Exists(Path.Combine(dir, "plugin.dll")) && File.Exists(Path.Combine(dir, ".kkumple-owner")), "only drawings and owner marker remain");

        dir = Bundle("locked", true);
        using (var locked = new FileStream(Path.Combine(dir, "plugin.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!Guard(dir).Remove(dir, false) && File.Exists(Path.Combine(dir, ".kkumple-owner")), "locked program keeps marker for retry");
        Check(Guard(dir).Remove(dir, false) && !Directory.Exists(dir), "retry succeeds after program unlocks");

        string external = Bundle("outside-junction", true);
        dir = Bundle("junction-parent", true);
        string link = Path.Combine(dir, "escape"); Junction(link, external);
        Check(!Guard(dir).Remove(dir, false) && File.Exists(Path.Combine(external, "plugin.dll")), "nested junction never deletes outside files");
        Check(File.Exists(Path.Combine(dir, ".kkumple-owner")), "refused junction preserves owner marker");
        Check(!Guard(link).Remove(link, false), "allowlisted junction itself is refused");
        Check(!Guard(Path.Combine(link, "plugin.dll")).Remove(Path.Combine(link, "plugin.dll"), false), "junction in an ancestor is refused");
        Directory.Delete(link, false);
        Check(Guard(dir).Remove(dir, false), "junction parent can be retried after link is removed");

        dir = Bundle("end-to-end-alive", true);
        Check(Serve(Signed("꿈플", false, ""), url => RunAgent(url, dir, false)) == 0 && File.Exists(Path.Combine(dir, "plugin.dll")), "full agent alive leaves bundle untouched");
        Check(Serve(Signed("꿈플", false, "").Replace("false", "true"), url => RunAgent(url, dir, false)) == 0 && File.Exists(Path.Combine(dir, "plugin.dll")), "full agent forged kill leaves bundle untouched");
        Check(Serve(Signed("꿈플", true, ""), url => RunAgent(url, dir, true)) == 0 && File.Exists(Path.Combine(dir, "plugin.dll")), "full agent dry run never removes files");
        Check(Serve(Signed("꿈플", true, ""), url => RunAgent(url, dir, false)) == 0 && !Directory.Exists(dir), "full agent real mode removes only synthetic bundle");
        dir = Bundle("end-to-end-locked", true);
        using (var locked = new FileStream(Path.Combine(dir, "plugin.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(Serve(Signed("꿈플", true, ""), url => RunAgent(url, dir, false)) == 2 && File.Exists(Path.Combine(dir, ".kkumple-owner")), "full agent incomplete exit preserves retry marker");
        Check(Serve(Signed("꿈플", true, ""), url => RunAgent(url, dir, false)) == 0 && !Directory.Exists(dir), "full agent completes on next retry");
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1) return 64;
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var output = Console.Out;
        Console.SetOut(new StringWriter());
        log = new Logger(Path.Combine(root, "safety-tests.log"));
        string failure = null;
        using (key = new RSACryptoServiceProvider(2048))
        {
            key.PersistKeyInCsp = false;
            try { Tests(); } catch (Exception ex) { failure = ex.ToString(); }
        }
        File.WriteAllText(Path.Combine(root, "result.json"), serializer.Serialize(new { passed = passed, count = passed.Count, failure = failure }), Encoding.UTF8);
        Console.SetOut(output);
        Console.WriteLine("Agent safety: " + passed.Count + " passed" + (failure == null ? "" : "; " + failure));
        return failure == null ? 0 : 1;
    }
}
