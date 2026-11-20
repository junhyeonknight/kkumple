using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kkumple.Update;
using KkumpleServiceEnd;

namespace Kkumple.Distribution
{
    internal static class LocalRemoval
    {
        private static readonly string[] UpdaterFiles = { "Kkumple.Updater.exe", "updater-config.json", "install-state.json", "last-verified-update.json", "last-verified-service-status.json" };
        public static void Preflight(string bundle, string data, string agent)
        {
            SafePath.Check(bundle); SafePath.Check(data); SafePath.Check(agent);
            if (Directory.Exists(bundle))
            {
                BundleInfo.LoadRootMarkerOnly(bundle);
                ProbeTree(bundle);
            }
            CheckOwnedFolder(data, ".kkumple-updater-owner", GateInstaller.Owner);
            CheckOwnedFolder(agent, ".kkumple-service-end-owner", "KKUMPLE_SERVICE_END_V1");
            if (File.Exists(SafePath.Under(data,"active-update.json")))
                throw new IOException("An update recovery is pending; recover before local removal.");
            foreach(string name in UpdaterFiles)
            {
                string file=SafePath.Under(data,name);
                if(Directory.Exists(file))throw new IOException("A managed updater file was replaced by a directory.");
                if(File.Exists(file))using(File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)){}
            }
        }
        private static void CheckOwnedFolder(string directory,string markerName,string owner)
        {
            if(!Directory.Exists(directory)||Directory.GetFileSystemEntries(directory).Length==0)return;
            string marker=SafePath.Under(directory,markerName);
            if(!File.Exists(marker)||File.ReadAllText(marker,Encoding.UTF8).Trim()!=owner)
                throw new IOException("An existing companion folder has no matching ownership marker.");
        }
        private static void ProbeTree(string directory)
        {
            SafePath.Check(directory);
            foreach(string item in Directory.GetFileSystemEntries(directory))
            {
                SafePath.Check(item);
                if(Directory.Exists(item))ProbeTree(item);
                else if(!SafePath.Protected(item))using(File.Open(item,FileMode.Open,FileAccess.Read,FileShare.None)){}
            }
        }
        public static void RemoveBundle(string bundle,string log)
        {
            var protectedFiles=new List<string>{".dwg",".dxf",".dwt",".dws",".bak",".ini",".cfg",".settings"};
            var guard=new RemovalGuard(new List<string>{bundle},protectedFiles,true,".kkumple-owner","KKUMPLE_FREE_NONCOMMERCIAL_V2",new Logger(log));
            if(!guard.Remove(bundle,false))throw new IOException("Local removal did not complete; ownership marker was kept for retry.");
        }
        public static void RemoveUpdater(string data)
        {
            CheckOwnedFolder(data,".kkumple-updater-owner",GateInstaller.Owner);
            if(!Directory.Exists(data))return;
            foreach(string name in UpdaterFiles)
            {
                string file=SafePath.Under(data,name);
                if(File.Exists(file))File.Delete(file);
            }
            string marker=SafePath.Under(data,".kkumple-updater-owner");
            var remaining=Directory.GetFileSystemEntries(data);
            if(remaining.Length==1&&File.Exists(marker)) { File.Delete(marker);Directory.Delete(data,false); }
        }
    }
}
