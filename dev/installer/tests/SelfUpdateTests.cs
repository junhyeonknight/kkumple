using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Kkumple.Update;

namespace Kkumple.Distribution
{
    internal static class SelfUpdateTests
    {
        static int failed;
        static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
        static void Throws(Action a, string name) { try { a(); Check(false, name); } catch (InvalidDataException) { Check(true, name); } catch (Exception ex) { Check(false, name + " (" + ex.GetType().Name + ")"); } }

        static byte[] Sign(string payload, RSACryptoServiceProvider rsa)
        {
            byte[] sig;
            using (var sha = SHA256.Create())
            {
                var f = new RSAPKCS1SignatureFormatter(rsa); f.SetHashAlgorithm("SHA256");
                sig = f.CreateSignature(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
            return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new { payload = payload, signature = Convert.ToBase64String(sig) }));
        }

        static string Payload(string installerJson)
        {
            var now = DateTimeOffset.UtcNow;
            return "{\"schema\":1,\"appId\":\"KKUMPLE_FREE_NONCOMMERCIAL_V2\",\"version\":\"2.0.7\",\"issuedAtUtc\":\"" + now.ToString("o") +
                   "\",\"expiresAtUtc\":\"" + now.AddDays(30).ToString("o") + "\",\"packages\":[]" + (installerJson == null ? "" : ",\"installer\":" + installerJson) + "}";
        }

        static int Main(string[] args)
        {
            var cfg = JsonXml.Read<UpdateConfig>(File.ReadAllBytes(args[0]));
            using (var rsa = new RSACryptoServiceProvider(2048))
            using (var other = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false; other.PersistKeyInCsp = false;
                cfg.PublicKeyXml = rsa.ToXmlString(false);
                string good = "{\"version\":\"2.2.5\",\"url\":\"https://github.com/x/y/releases/download/installer-v2.2.5/kkumple-setup-2.2.5.exe\",\"sha256\":\"" + new string('A', 64) + "\",\"bytes\":76999680}";
                var info = SelfUpdate.ReadInstallerInfo(Sign(Payload(good), rsa), cfg, DateTimeOffset.UtcNow);
                Check(info != null && info.Version == "2.2.5" && info.Bytes == 76999680 && info.Sha256 == new string('a', 64), "signed installer entry is read");
                Check(SelfUpdate.ReadInstallerInfo(Sign(Payload(null), rsa), cfg, DateTimeOffset.UtcNow) == null, "manifest without installer entry means no installer update");
                Throws(() => SelfUpdate.ReadInstallerInfo(Sign(Payload(good), other), cfg, DateTimeOffset.UtcNow), "manifest signed by another key is rejected");
                Throws(() => SelfUpdate.ReadInstallerInfo(Sign(Payload(good.Replace("https://", "http://")), rsa), cfg, DateTimeOffset.UtcNow), "non-HTTPS installer URL is rejected");
                Throws(() => SelfUpdate.ReadInstallerInfo(Sign(Payload(good.Replace(new string('A', 64), "abc")), rsa), cfg, DateTimeOffset.UtcNow), "malformed SHA-256 is rejected");
                Throws(() => SelfUpdate.ReadInstallerInfo(Sign(Payload(good.Replace("2.2.5\",\"url", "two\",\"url")), rsa), cfg, DateTimeOffset.UtcNow), "malformed version is rejected");
                Throws(() => SelfUpdate.ReadInstallerInfo(Sign(Payload(good.Replace("76999680", "999999999999")), rsa), cfg, DateTimeOffset.UtcNow), "oversized installer is rejected");
                byte[] tampered = Sign(Payload(good), rsa);
                string t = Encoding.UTF8.GetString(tampered).Replace("2.2.5", "9.9.9");
                Throws(() => SelfUpdate.ReadInstallerInfo(Encoding.UTF8.GetBytes(t), cfg, DateTimeOffset.UtcNow), "edited signed payload is rejected");
            }
            Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            return failed == 0 ? 0 : 1;
        }
    }
}
