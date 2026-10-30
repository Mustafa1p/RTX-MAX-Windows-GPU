// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RtxMax {
    public sealed class SetupRequest {public string BuildDirectory{get;set;}public string ProfilePath{get;set;}public string AssetsDirectory{get;set;}public string Destination{get;set;}public bool EvidenceReviewed{get;set;}}
    public static class Setup {
        public static void ValidateProfile(byte[] data){
            if(data.Length!=176||Encoding.ASCII.GetString(data,0,8)!="GUPROF17"||BitConverter.ToUInt32(data,8)!=1||BitConverter.ToUInt32(data,12)!=176)throw new InvalidDataException("Invalid v17 runtime profile format.");
            if(!data.Skip(144).SequenceEqual(Hex(Data.Hash(data.Take(144).ToArray()))))throw new InvalidDataException("Runtime profile checksum mismatch.");
            if(BitConverter.ToUInt32(data,16)!=0x248910de||BitConverter.ToUInt32(data,20)!=0x248910de||BitConverter.ToUInt32(data,24)!=0xb74000a1)throw new InvalidDataException("This source port only implements the captured Palit 2489 target.");
        }
        static byte[] Hex(string text){return Enumerable.Range(0,text.Length/2).Select(i=>Convert.ToByte(text.Substring(i*2,2),16)).ToArray();}
        public static void ValidateEfi(byte[] data){
            if(data.Length<512||data[0]!='M'||data[1]!='Z')throw new InvalidDataException("Not a PE image.");int pe=BitConverter.ToInt32(data,0x3c);if(pe<64||pe>data.Length-264||BitConverter.ToUInt32(data,pe)!=0x4550||BitConverter.ToUInt16(data,pe+4)!=0x8664)throw new InvalidDataException("Not an AMD64 PE image.");int optional=pe+24;if(BitConverter.ToUInt16(data,optional)!=0x20b||BitConverter.ToUInt16(data,optional+68)!=10)throw new InvalidDataException("Not a PE32+ EFI application.");foreach(int index in new[]{1,12,13})if(BitConverter.ToUInt64(data,optional+112+8*index)!=0)throw new InvalidDataException("The EFI has imports/IAT/delayed imports.");
        }
        public static string Stage(Health health,string buildDir,string profilePath,string assetsDir,string destination,bool reviewed){
            if(health==null||health.Gpus.Count!=1||!health.Gpus[0].ReferenceSignature)throw new InvalidOperationException("This PC is outside the reference signature. Firmware preparation is blocked by this port.");
            if(!reviewed)throw new InvalidOperationException("Review the local ownership/profile evidence before preparing boot files.");
            var build=Data.Read<Dictionary<string,object>>(Path.Combine(buildDir,"build.json"));string mode=Convert.ToString(build["mode"]);if(mode!="Manual"&&mode!="Internal")throw new InvalidDataException("Unrecognized build mode.");
            if(mode=="Internal"){
                if(!health.VerifiedExistingInstallation||health.Boot==null)throw new InvalidOperationException("New internal automatic installations require the documented manual hardware qualification. This tool only prepares an internal rebuild for an already verified installation on this PC.");
                var windows=health.Partitions.SingleOrDefault(p=>p.IsSystem);if(windows==null||Convert.ToString(build["internal_partition_guid"]).Trim('{','}')!=health.Boot.PartitionGuid||!String.Equals(Convert.ToString(build["windows_partition_guid"]).Trim('{','}'),windows.Guid.Trim('{','}'),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Internal build partition identities do not match this PC's verified boot route.");
            }
            byte[] efi=File.ReadAllBytes(Path.Combine(buildDir,"BOOTX64.EFI"));ValidateEfi(efi);if(Data.Hash(efi)!=Convert.ToString(build["efi_sha256"]))throw new InvalidDataException("EFI hash differs from build.json.");
            byte[] profile=File.ReadAllBytes(profilePath);ValidateProfile(profile);
            var reference=Data.Json().Deserialize<Dictionary<string,object>>(Data.Resource("reference-artifacts.json"));
            var assets=((IEnumerable)reference["assets"]).Cast<Dictionary<string,object>>().ToList();
            foreach(var row in assets){string name=(string)row["name"],file=Path.Combine(assetsDir,name);if(!File.Exists(file)||new FileInfo(file).Length!=Convert.ToInt64(row["bytes"])||Data.HashFile(file)!=(string)row["sha256"])throw new InvalidDataException("Missing or mismatched firmware asset: "+name);}
            if(!File.Exists(Path.Combine(assetsDir,"NVIDIA-LICENSE")))throw new InvalidDataException("Keep NVIDIA-LICENSE from the local firmware extraction.");
            string target=Path.GetFullPath(destination);if(Directory.Exists(target)||File.Exists(target))throw new IOException("Choose a new output folder; nothing is overwritten.");string parent=Path.GetDirectoryName(target);if(!Directory.Exists(parent))throw new IOException("The destination parent does not exist.");Data.NoReparseParents(parent);
            var drive=new DriveInfo(Path.GetPathRoot(target));if(drive.DriveType!=DriveType.Fixed)throw new IOException("Prepare a new folder on a fixed data drive, then follow the reviewed USB/ESP copy procedure.");
            if(health.Partitions.Any(p=>p.DriveLetter.Equals(drive.Name.Substring(0,1),StringComparison.OrdinalIgnoreCase)&&p.GptType.Equals("{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",StringComparison.OrdinalIgnoreCase)))throw new IOException("Direct writes into an EFI system partition are blocked by this preparation step.");
            string efiDir=Path.Combine(target,mode=="Manual"?"EFI\\BOOT":"EFI\\PalitUnlock"),runtime=Path.Combine(target,"RTX-UNLOCK");Directory.CreateDirectory(efiDir);Directory.CreateDirectory(Path.Combine(runtime,"ASSETS"));
            File.WriteAllBytes(Path.Combine(efiDir,mode=="Manual"?"BOOTX64.EFI":"AUTO.EFI"),efi);File.WriteAllBytes(Path.Combine(runtime,"PROFILE.BIN"),profile);
            foreach(var row in assets){string name=(string)row["name"],dest=Path.Combine(runtime,"ASSETS",name);File.Copy(Path.Combine(assetsDir,name),dest,false);if(Data.HashFile(dest)!=(string)row["sha256"])throw new InvalidDataException("Post-copy asset verification failed.");}
            File.Copy(Path.Combine(assetsDir,"NVIDIA-LICENSE"),Path.Combine(runtime,"ASSETS\\NVIDIA-LICENSE"),false);
            string source=SourcePackage.Extract();File.Copy(Path.Combine(source,"LICENSE"),Path.Combine(runtime,"LICENSE"),false);string licenses=Path.Combine(runtime,"LICENSES");Directory.CreateDirectory(licenses);foreach(var f in Directory.GetFiles(Path.Combine(source,"firmware\\LICENSES")))File.Copy(f,Path.Combine(licenses,Path.GetFileName(f)),false);
            Data.Save(Path.Combine(runtime,"installation.json"),new{schema_version=1,mode=mode,status="staged_only_not_installed",efi_sha256=Data.Hash(efi),profile_sha256=Data.Hash(profile),assets=assets,hardware_tested=false,automatic_boot_installed=false});
            return target;
        }
    }
    public sealed class DownloadSpec {public string component{get;set;}public string version{get;set;}public string url{get;set;}public string sha256{get;set;}public long archive_bytes{get;set;}}
    public static class Libraries {
        public static async Task<string> Download(Action<string> progress,CancellationToken cancel){
            var specs=Data.Json().Deserialize<List<DownloadSpec>>(Data.Resource("benchmark-packages.json"));string root=Path.Combine(Data.Home,"libraries","cuda-12.8.1-"+Guid.NewGuid().ToString("N").Substring(0,6));Data.NoReparseParents(root);Directory.CreateDirectory(root);ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            using(var client=new HttpClient()){client.Timeout=TimeSpan.FromMinutes(30);
                foreach(var spec in specs){var uri=new Uri(spec.url);if(uri.Scheme!="https"||uri.Host!="developer.download.nvidia.com")throw new InvalidDataException("Unapproved dependency source.");string archive=Path.Combine(root,spec.component+".zip");
                    using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancel)){response.EnsureSuccessStatusCode();using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] buffer=new byte[1024*1024];long done=0;int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0){await output.WriteAsync(buffer,0,count,cancel);done+=count;if(done>spec.archive_bytes)throw new InvalidDataException("Download exceeds the pinned package size.");progress(spec.component+": "+(100.0*done/spec.archive_bytes).ToString("0")+"% of "+(spec.archive_bytes/1024/1024)+" MiB");}if(done!=spec.archive_bytes)throw new InvalidDataException("Dependency download is incomplete.");}}
                    progress("Verifying SHA-256 for "+spec.component+"...");if(Data.HashFile(archive)!=spec.sha256)throw new InvalidDataException("NVIDIA package checksum mismatch; no DLLs were loaded.");
                    using(var zip=ZipFile.OpenRead(archive)){long total=0;foreach(var entry in zip.Entries){cancel.ThrowIfCancellationRequested();if(String.IsNullOrEmpty(entry.Name))continue;total+=entry.Length;if(total>3L*1024*1024*1024)throw new InvalidDataException("Unexpected extraction size.");string dest=Path.GetFullPath(Path.Combine(root,spec.component,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));if(!Data.IsInside(dest,Path.Combine(root,spec.component)))throw new InvalidDataException("Unsafe dependency archive path.");Data.NoReparseParents(Path.GetDirectoryName(dest));Directory.CreateDirectory(Path.GetDirectoryName(dest));entry.ExtractToFile(dest,false);}}
                }
            }progress("Verified NVIDIA libraries are ready. No driver was installed or changed.");return root;
        }
    }
}
