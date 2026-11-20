using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using KkumpleInstallConsent;

internal static class ConsentTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fail") return 7;
        if (args.Length != 2 && args.Length != 3) return 64;
        bool preview = args.Length == 2 || args[2] != "--no-preview";
        string cfgPath = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var ser = new JavaScriptSerializer();
        ConsentConfig cfg = ser.Deserialize<ConsentConfig>(File.ReadAllText(cfgPath, Encoding.UTF8));
        int passed = 0;
        Action<bool, string> check = (value, name) => { if (!value) throw new Exception(name); passed++; };
        using (var form = new ConsentForm(cfg))
        {
            var checkbox = form.Controls.OfType<CheckBox>().Single();
            var approve = form.Controls.OfType<Button>().Single(b => b.DialogResult == DialogResult.OK);
            var cancel = form.Controls.OfType<Button>().Single(b => b.DialogResult == DialogResult.Cancel);
            check(!checkbox.Checked && !approve.Enabled, "consent checkbox starts unchecked and approval disabled");
            checkbox.Checked = true;
            check(approve.Enabled, "approval requires checked consent");
            checkbox.Checked = false;
            check(!approve.Enabled, "unchecking consent disables approval");
            check(cancel.Enabled && form.CancelButton == cancel, "cancellation remains available");
            check(form.Controls.OfType<TextBox>().Single().Text == cfg.DisclosureText, "exact disclosure is displayed");
            if (preview)
            {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(output, "consent-preview.png")); }
            form.Close();
            }
        }
        cfg.ConsentRecordPath = Path.Combine(output, "consent.json");
        string recorded = (string)typeof(KkumpleInstallConsent.Program).GetMethod("RecordConsent", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { cfg });
        var record = ser.Deserialize<ConsentRecord>(File.ReadAllText(recorded, Encoding.UTF8));
        check(record.agreed && record.version == cfg.Version && record.service == cfg.ServiceName, "record contains exact accepted service and version");
        using (var sha = SHA256.Create())
            check(record.disclosureSha256 == BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(cfg.DisclosureText))).Replace("-", ""), "record hash matches disclosure bytes");
        var run = typeof(KkumpleInstallConsent.Program).GetMethod("RunStep", BindingFlags.NonPublic | BindingFlags.Static);
        bool missingRefused = false;
        try { run.Invoke(null, new object[] { Path.Combine(output, "missing.exe"), "", output, true }); }
        catch (TargetInvocationException ex) { missingRefused = ex.InnerException is FileNotFoundException; }
        check(missingRefused, "missing real installer fails");
        bool failedRefused = false;
        try { run.Invoke(null, new object[] { Application.ExecutablePath, "--fail", output, true }); }
        catch (TargetInvocationException ex) { failedRefused = ex.InnerException is InvalidOperationException; }
        check(failedRefused, "failed real installer does not report success");
        File.WriteAllText(Path.Combine(output, "result.json"), ser.Serialize(new { count = passed, failure = (string)null }), Encoding.UTF8);
        Console.WriteLine("Consent: " + passed + " passed");
        return 0;
    }
}
