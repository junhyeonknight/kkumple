// 꿈플 AutoCAD 2008/2009 시험판 페이로드 생성기.
// 원본 꿈플.exe에 들어 있는 2010 계열(2010–2012) 모듈을 해시 검증 후 꺼내고,
// 2008/2009에서 읽을 수 있는 CUI 메뉴와 BITMAP 아이콘 리소스 DLL, LISP 로더 템플릿을 만든다.
// 원본 DLL은 바이트 단위로 그대로 복사한다(수정하지 않음).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

static class LegacyBuilder
{
    const string GroupId = "2010";

    static int Main(string[] args)
    {
        try
        {
            string body = Arg(args, "--body"), outDir = Arg(args, "--out");
            if (body == null || outDir == null) { Console.Error.WriteLine("usage: LegacyBuilder --body <꿈플.exe> --out <dir>"); return 2; }
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            string payload = Path.Combine(outDir, "payload");
            string modules = Path.Combine(payload, "modules");
            Directory.CreateDirectory(modules);

            // 1) 원본 페이로드 읽기
            byte[] outer;
            var asm = Assembly.ReflectionOnlyLoadFrom(body);
            using (var s = asm.GetManifestResourceStream("Kkumple.Payload.zip"))
            {
                if (s == null) throw new InvalidDataException("꿈플.exe 안에 Kkumple.Payload.zip이 없습니다.");
                outer = ReadAll(s);
            }
            Dictionary<string, object> group; string aliasesJson, commandsJson, notice;
            byte[] groupZip;
            using (var z = new ZipArchive(new MemoryStream(outer), ZipArchiveMode.Read))
            {
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var manifest = js.Deserialize<Dictionary<string, object>>(Text(z, "manifest.json"));
                group = ((System.Collections.ArrayList)manifest["Groups"]).Cast<Dictionary<string, object>>().Single(g => (string)g["Id"] == GroupId);
                aliasesJson = Text(z, "aliases.json"); commandsJson = Text(z, "commands.json"); notice = Text(z, "notice.txt");
                groupZip = Bytes(z, (string)group["ArchivePath"]);
            }
            Expect(Sha(groupZip) == ((string)group["ArchiveSha256"]).ToLowerInvariant(), "2010 계열 압축 SHA-256 불일치");
            Expect(groupZip.LongLength == Convert.ToInt64(group["ArchiveBytes"]), "2010 계열 압축 크기 불일치");

            // 2) 모듈 추출 + 파일별 해시 검증
            var expected = ((System.Collections.ArrayList)group["Files"]).Cast<Dictionary<string, object>>()
                .ToDictionary(f => (string)f["Path"], f => ((string)f["Sha256"]).ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
            int count = 0;
            using (var z = new ZipArchive(new MemoryStream(groupZip), ZipArchiveMode.Read))
            {
                foreach (var e in z.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    if (e.FullName.Contains("..") || e.FullName.Contains(":") || e.FullName.StartsWith("/")) throw new InvalidDataException("잘못된 압축 경로: " + e.FullName);
                    byte[] data; using (var s = e.Open()) data = ReadAll(s);
                    string want; Expect(expected.TryGetValue(e.FullName, out want), "목록에 없는 파일: " + e.FullName);
                    Expect(Sha(data) == want, "파일 SHA-256 불일치: " + e.FullName);
                    string dest = Path.Combine(modules, e.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, data); count++;
                }
            }
            Expect(count == expected.Count, "파일 수 불일치 " + count + "/" + expected.Count);
            File.WriteAllText(Path.Combine(payload, "KKUMPLE-NONCOMMERCIAL.txt"), notice, new UTF8Encoding(true));

            // 3) CUI 변환 (2008: 리본 없음 / 2009: 리본 포함)
            string cuix = Path.Combine(modules, "Kkumple.cuix");
            var parts = new Dictionary<string, XmlDocument>(StringComparer.OrdinalIgnoreCase);
            using (var z = ZipFile.OpenRead(cuix))
                foreach (var e in z.Entries.Where(x => x.Name.EndsWith(".cui", StringComparison.OrdinalIgnoreCase)))
                {
                    var d = new XmlDocument { PreserveWhitespace = false };
                    using (var s = e.Open()) d.Load(s);
                    parts[Path.GetFileNameWithoutExtension(e.Name)] = d;
                }
            var images = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlAttribute a in parts["MenuGroup"].SelectNodes("//SmallImage/@Name|//LargeImage/@Name")) if (a.Value.Length > 0) images.Add(a.Value);

            string[] common = { "PopMenuRoot", "ToolbarRoot", "ScreenMenuRoot", "ImageMenuRoot", "TabletMenuRoot", "DigitizerButtonRoot", "MouseButtonRoot", "AcceleratorRoot", "DoubleClickRoot" };
            var r2008 = WriteCui(parts, common, "4", Path.Combine(payload, "menus", "2008", "Kkumple.cui"));
            var r2009 = WriteCui(parts, common.Concat(new[] { "RibbonRoot" }).ToArray(), "5", Path.Combine(payload, "menus", "2009", "Kkumple.cui"));

            // 4) 아이콘: RCDATA(ICO 32bpp) → RT_BITMAP(24bpp, 버튼 회색 배경 합성)
            string resDll = Path.Combine(outDir, "Kkumple-legacy-res.dll");
            int converted = BuildBitmapDll(Path.Combine(modules, "Kkumple.dll"), resDll, images);
            foreach (var y in new[] { "2008", "2009" }) File.Copy(resDll, Path.Combine(payload, "menus", y, "Kkumple.dll"), true);
            File.Delete(resDll);

            // 5) 별칭 목록 (로더 생성용, ASCII 검증)
            var js2 = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var aliases = js2.Deserialize<List<Dictionary<string, string>>>(aliasesJson);
            var sb = new StringBuilder();
            foreach (var a in aliases)
            {
                string al = a["Alias"], cmd = a["GlobalCommand"];
                Expect(Regex.IsMatch(al, @"^[\x21-\x7E]+$") && Regex.IsMatch(cmd, @"^DREAM_[\x21-\x7E]+$") && !al.Contains("\"") && !al.Contains("(") && !al.Contains(")") && !al.Contains(";"), "로더에 넣을 수 없는 별칭: " + al);
                sb.Append(al).Append('\t').Append(cmd).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(payload, "aliases.tsv"), sb.ToString(), Encoding.ASCII);

            // 6) 파일 목록 + 해시
            var list = new StringBuilder();
            foreach (string f in Directory.GetFiles(payload, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                string rel = f.Substring(payload.Length + 1).Replace('\\', '/');
                list.Append(Sha(File.ReadAllBytes(f))).Append(' ').Append(new FileInfo(f).Length).Append(' ').Append(rel).Append("\n");
            }
            File.WriteAllText(Path.Combine(outDir, "payload-files.txt"), list.ToString(), new UTF8Encoding(false));
            File.Copy(Path.Combine(outDir, "payload-files.txt"), Path.Combine(payload, "payload-files.txt"));

            string zipPath = Path.Combine(outDir, "LegacyPayload.zip");
            ZipFile.CreateFromDirectory(payload, zipPath, CompressionLevel.Optimal, false);

            Console.WriteLine("modules=" + count + " aliases=" + aliases.Count + " images=" + images.Count + " bitmaps=" + converted);
            Console.WriteLine("cui2008 " + r2008); Console.WriteLine("cui2009 " + r2009);
            Console.WriteLine("payload " + new FileInfo(zipPath).Length + " sha256=" + Sha(File.ReadAllBytes(zipPath)));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAILED: " + ex); return 1; }
    }

    static string WriteCui(Dictionary<string, XmlDocument> parts, string[] roots, string minor, string path)
    {
        var doc = new XmlDocument();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
        var cs = doc.CreateElement("CustSection"); doc.AppendChild(cs);
        var fv = doc.CreateElement("FileVersion");
        fv.SetAttribute("MajorVersion", "0"); fv.SetAttribute("MinorVersion", minor); fv.SetAttribute("IncrementalVersion", "1"); fv.SetAttribute("UserVersion", "1");
        cs.AppendChild(fv);
        cs.AppendChild(doc.ImportNode(parts["Header"].SelectSingleNode("/CustSection/Header"), true));
        var mg = (XmlElement)doc.ImportNode(parts["MenuGroup"].DocumentElement, true);
        cs.AppendChild(mg);
        var mr = doc.CreateElement("MenuRoot"); mg.AppendChild(mr);
        foreach (string r in roots) mr.AppendChild(doc.ImportNode(parts[r].DocumentElement, true));
        // 2010(R18) 수정 표식은 구버전에 의미가 없으므로 제거한다.
        foreach (XmlNode n in doc.SelectNodes("//ModifiedRev").Cast<XmlNode>().ToList()) n.ParentNode.RemoveChild(n);

        // 참조 무결성 검사: 모든 MacroRef/MenuMacroID/PanelId/pUID가 실제 대상과 연결되는지
        var macros = new HashSet<string>(doc.SelectNodes("//MenuMacro/@UID").Cast<XmlAttribute>().Select(a => a.Value));
        foreach (XmlAttribute a in doc.SelectNodes("//MacroRef/@MenuMacroID|//@MenuMacroID")) Expect(macros.Contains(a.Value), "없는 매크로 참조: " + a.Value);
        var pops = new HashSet<string>(doc.SelectNodes("//PopMenu/@UID").Cast<XmlAttribute>().Select(a => a.Value));
        foreach (XmlAttribute a in doc.SelectNodes("//PopMenuRef/@pUID")) Expect(pops.Contains(a.Value), "없는 하위 메뉴 참조: " + a.Value);
        var panels = new HashSet<string>(doc.SelectNodes("//RibbonPanelSource/@UID").Cast<XmlAttribute>().Select(a => a.Value));
        foreach (XmlAttribute a in doc.SelectNodes("//RibbonPanelSourceReference/@PanelId")) Expect(panels.Contains(a.Value), "없는 리본 패널 참조: " + a.Value);

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(true), Indent = true };
        using (var w = XmlWriter.Create(path, settings)) doc.Save(w);
        return "macros=" + macros.Count + " popmenus=" + pops.Count + " toolbars=" + doc.SelectNodes("//Toolbar").Count
            + " ribbonTabs=" + doc.SelectNodes("//RibbonTabSource").Count + " ribbonPanels=" + panels.Count;
    }

    // --- 리소스 DLL ---
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string f, IntPtr r, uint flags);
    [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr h);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr h, string name, IntPtr type);
    [DllImport("kernel32")] static extern IntPtr LoadResource(IntPtr h, IntPtr r);
    [DllImport("kernel32")] static extern IntPtr LockResource(IntPtr h);
    [DllImport("kernel32")] static extern int SizeofResource(IntPtr h, IntPtr r);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr BeginUpdateResource(string f, bool deleteExisting);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool UpdateResource(IntPtr h, IntPtr type, string name, ushort lang, byte[] data, int size);
    [DllImport("kernel32", SetLastError = true)] static extern bool EndUpdateResource(IntPtr h, bool discard);

    static int BuildBitmapDll(string source, string dest, IEnumerable<string> names)
    {
        var dibs = new List<KeyValuePair<string, byte[]>>();
        IntPtr h = LoadLibraryEx(source, IntPtr.Zero, 0x2 | 0x20);
        if (h == IntPtr.Zero) throw new IOException("리소스 DLL을 열 수 없습니다: " + Marshal.GetLastWin32Error());
        try
        {
            foreach (string name in names)
            {
                IntPtr r = FindResource(h, name.ToUpperInvariant(), (IntPtr)10);
                Expect(r != IntPtr.Zero, "아이콘 리소스 없음: " + name);
                int n = SizeofResource(h, r); var buf = new byte[n];
                Marshal.Copy(LockResource(LoadResource(h, r)), buf, 0, n);
                dibs.Add(new KeyValuePair<string, byte[]>(name.ToUpperInvariant(), IcoToDib(buf, name)));
            }
        }
        finally { FreeLibrary(h); }
        File.Copy(source, dest, true);
        IntPtr u = BeginUpdateResource(dest, true);
        if (u == IntPtr.Zero) throw new IOException("BeginUpdateResource 실패: " + Marshal.GetLastWin32Error());
        foreach (var kv in dibs)
            if (!UpdateResource(u, (IntPtr)2, kv.Key, 0, kv.Value, kv.Value.Length)) { EndUpdateResource(u, true); throw new IOException("UpdateResource 실패: " + kv.Key); }
        if (!EndUpdateResource(u, false)) throw new IOException("EndUpdateResource 실패: " + Marshal.GetLastWin32Error());

        // 결과 검증: 모든 이름이 RT_BITMAP으로 들어갔는지 다시 읽어 확인
        IntPtr v = LoadLibraryEx(dest, IntPtr.Zero, 0x2 | 0x20);
        try { foreach (var kv in dibs) Expect(FindResource(v, kv.Key, (IntPtr)2) != IntPtr.Zero, "BITMAP 확인 실패: " + kv.Key); }
        finally { FreeLibrary(v); }
        return dibs.Count;
    }

    static byte[] IcoToDib(byte[] ico, string name)
    {
        Bitmap src;
        using (var ms = new MemoryStream(ico)) using (var icon = new Icon(ms)) src = icon.ToBitmap();
        using (src)
        using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(192, 192, 192)); g.DrawImage(src, 0, 0, src.Width, src.Height); }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Bmp);
                byte[] file = ms.ToArray();
                Expect(file.Length > 14 && file[0] == 'B' && file[1] == 'M', "BMP 변환 실패: " + name);
                var dib = new byte[file.Length - 14]; Buffer.BlockCopy(file, 14, dib, 0, dib.Length); // BITMAPFILEHEADER 제거
                return dib;
            }
        }
    }

    // --- 유틸 ---
    static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static void Expect(bool c, string m) { if (!c) throw new InvalidDataException(m); }
    static byte[] ReadAll(Stream s) { using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); } }
    static string Sha(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static byte[] Bytes(ZipArchive z, string n) { var e = z.GetEntry(n); if (e == null) throw new InvalidDataException("없음: " + n); using (var s = e.Open()) return ReadAll(s); }
    static string Text(ZipArchive z, string n) { return new UTF8Encoding(false).GetString(Bytes(z, n)).TrimStart('﻿'); }
}
