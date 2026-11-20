using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kkumple.Update;

namespace Kkumple.Distribution
{
    internal static class SettingsBackup
    {
        public static string Save(string bundle,string backupBase)
        {
            if(!Directory.Exists(bundle))return null;
            BundleInfo.LoadRootMarkerOnly(bundle);
            var files=new List<string>();Collect(bundle,files);
            if(files.Count==0)return null;
            SafePath.Check(backupBase);Directory.CreateDirectory(backupBase);
            string backup=SafePath.Under(backupBase,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
            SafePath.Atomic(SafePath.Under(backup,".kkumple-settings-backup"),Encoding.ASCII.GetBytes("KKUMPLE_SETTINGS_BACKUP_V1"));
            var saved=new Dictionary<string,string>();
            foreach(string file in files)
            {
                string relative=file.Substring(SafePath.Full(bundle).Length+1).Replace('\\','/');
                string target=SafePath.Under(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));SafePath.Check(target);
                File.Copy(file,target,false);saved[relative]=Protocol.FileSha(target);
            }
            SafePath.Atomic(SafePath.Under(backup,"files.json"),JsonXml.Bytes(saved));return backup;
        }
        private static void Collect(string directory,List<string> files)
        {
            SafePath.Check(directory);
            foreach(string file in Directory.GetFiles(directory)){SafePath.Check(file);if(SafePath.Protected(file))files.Add(file);}
            foreach(string child in Directory.GetDirectories(directory))Collect(child,files);
        }
        public static void RestoreMissing(string bundle,string backup)
        {
            if(backup==null)return;
            string marker=SafePath.Under(backup,".kkumple-settings-backup");
            if(File.ReadAllText(marker,Encoding.ASCII)!="KKUMPLE_SETTINGS_BACKUP_V1")throw new IOException("Settings backup ownership differs.");
            var files=JsonXml.Read<Dictionary<string,string>>(File.ReadAllBytes(SafePath.Under(backup,"files.json")));
            if(!Directory.Exists(bundle))
            {
                SafePath.Check(bundle);Directory.CreateDirectory(bundle);
                SafePath.Atomic(SafePath.Under(bundle,".kkumple-owner"),Encoding.ASCII.GetBytes("KKUMPLE_FREE_NONCOMMERCIAL_V2"));
            }
            BundleInfo.LoadRootMarkerOnly(bundle);
            foreach(var file in files)
            {
                string source=SafePath.Under(backup,file.Key),target=SafePath.Under(bundle,file.Key);
                if(!SafePath.Protected(file.Key)||Protocol.FileSha(source)!=file.Value)throw new IOException("Settings backup verification failed.");
                if(File.Exists(target))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));SafePath.Check(target);File.Copy(source,target,false);
            }
        }
    }
}
