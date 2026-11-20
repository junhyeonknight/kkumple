using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Kkumple.Update;

namespace Kkumple.Distribution
{
    internal sealed class EmbeddedPayload : IDisposable
    {
        public const string ResourceName = "Kkumple.InstallPayload.zip";
        private const string Marker = ".kkumple-setup-cache";
        private readonly string parent;
        private readonly string id;
        public string Root { get; private set; }
        private EmbeddedPayload(string baseDirectory)
        {
            parent = SafePath.Full(baseDirectory); SafePath.Check(parent);
            Directory.CreateDirectory(parent); SafePath.Check(parent);
            id = Guid.NewGuid().ToString("N"); Root = SafePath.Under(parent, id);
            if (Directory.Exists(Root) || File.Exists(Root)) throw new IOException("설치 임시 폴더가 이미 있습니다.");
            Directory.CreateDirectory(Root); SafePath.Check(Root);
            File.WriteAllText(SafePath.Under(Root, Marker), id, Encoding.ASCII);
        }
        public static EmbeddedPayload OpenDefault()
        {
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kkumple", "SetupCache");
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("설치 구성 파일이 없습니다. 정식 설치 파일을 다시 받아 주세요.");
                return Open(stream, cache);
            }
        }
        public static EmbeddedPayload Open(Stream stream, string baseDirectory)
        {
            var result = new EmbeddedPayload(baseDirectory);
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    if (zip.Entries.Count == 0 || zip.Entries.Count > 128) throw new InvalidDataException("설치 구성 개수가 올바르지 않습니다.");
                    foreach (var entry in zip.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/", StringComparison.Ordinal)) throw new InvalidDataException("빈 폴더 항목은 사용할 수 없습니다.");
                        if (name != "payload-hashes.json" && !name.StartsWith("payload/", StringComparison.Ordinal))
                            throw new InvalidDataException("알 수 없는 설치 구성입니다.");
                        if (!names.Add(name)) throw new InvalidDataException("중복된 설치 구성입니다.");
                        if (entry.Length < 0 || entry.Length > 268435456 || (total += entry.Length) > 536870912)
                            throw new InvalidDataException("설치 구성 크기가 너무 큽니다.");
                        int mode = (entry.ExternalAttributes >> 16) & 0xf000;
                        if (mode != 0 && mode != 0x8000) throw new InvalidDataException("연결 파일은 설치할 수 없습니다.");
                        string target = SafePath.Under(result.Root, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)); SafePath.Check(target);
                        using (Stream input = entry.Open())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[65536]; long copied = 0; int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                copied += count;
                                if (copied > entry.Length) throw new InvalidDataException("설치 구성 길이가 맞지 않습니다.");
                                output.Write(buffer, 0, count);
                            }
                            if (copied != entry.Length) throw new InvalidDataException("설치 구성이 불완전합니다.");
                        }
                    }
                }
                SetupTasks.VerifyPayload(result.Root);
                var manifest = JsonXml.Read<Dictionary<string, string>>(File.ReadAllBytes(Path.Combine(result.Root, "payload-hashes.json")));
                if (names.Count != manifest.Count + 1) throw new InvalidDataException("검증되지 않은 설치 구성이 있습니다.");
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose()
        {
            try
            {
                string full = SafePath.Full(Root);
                if (!string.Equals(full, SafePath.Under(parent, id), StringComparison.OrdinalIgnoreCase)) return;
                SafePath.Check(full);
                string marker = SafePath.Under(full, Marker);
                if (!File.Exists(marker) || File.ReadAllText(marker, Encoding.ASCII) != id) return;
                CheckTree(full); DeleteTree(full);
            }
            catch { /* Leave the owned cache for diagnosis when a file is still in use. */ }
        }
        private static void CheckTree(string directory)
        {
            SafePath.Check(directory);
            foreach (string file in Directory.GetFiles(directory)) SafePath.Check(file);
            foreach (string child in Directory.GetDirectories(directory)) CheckTree(child);
        }
        private static void DeleteTree(string directory)
        {
            SafePath.Check(directory);
            foreach (string file in Directory.GetFiles(directory)) { SafePath.Check(file); File.Delete(file); }
            foreach (string child in Directory.GetDirectories(directory)) DeleteTree(child);
            SafePath.Check(directory); Directory.Delete(directory);
        }
    }
}
