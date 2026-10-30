// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace RtxMax {
    public static class Data {
        public const string Version="0.2.0";
        public static readonly string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RTX-MAX");
        public static JavaScriptSerializer Json(){return new JavaScriptSerializer { MaxJsonLength=64*1024*1024, RecursionLimit=100 };}
        public static string Hash(byte[] value){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
        public static string HashFile(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
        public static string Utc(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}
        public static void Save(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));if(File.Exists(path))throw new IOException("An existing report will not be overwritten.");File.WriteAllText(path,Json().Serialize(value),new UTF8Encoding(false));}
        public static T Read<T>(string path){return Json().Deserialize<T>(File.ReadAllText(path));}
        public static string Resource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new IOException("Missing embedded resource: "+name);using(var r=new StreamReader(s,Encoding.UTF8))return r.ReadToEnd();}}
        public static byte[] ResourceBytes(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new IOException("Missing embedded resource: "+name);using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
        public static string Unique(string category,string extension=".json"){return Path.Combine(Home,category,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+extension);}
        public static string Run(string exe,string args,int timeout=45000){
            var start=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(start)){var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(timeout)){try{p.Kill();}catch{}throw new TimeoutException("The diagnostic command timed out.");}System.Threading.Tasks.Task.WaitAll(output,error);if(p.ExitCode!=0)throw new InvalidOperationException("The diagnostic command was unavailable (exit "+p.ExitCode+").");return output.Result.Trim();}
        }
        public static string PowerShell(string body){return Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(body)));}
        public static bool Admin {get{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}}
        public static string Quote(string text){if(text.Contains("\"")||text.Contains("\n")||text.Contains("\r"))throw new ArgumentException("Invalid path.");return "\""+text+"\"";}
        public static bool IsInside(string child,string parent){return Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
        public static void NoReparseParents(string path){var p=new DirectoryInfo(Path.GetFullPath(path));while(p!=null){if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked/reparse directories are not allowed here.");p=p.Parent;}}
    }
    public sealed class Gpu {
        public int Index {get;set;} public string Name {get;set;} public string Bus {get;set;} public string Device {get;set;} public string Subsystem {get;set;}
        public string Driver {get;set;} public string Vbios {get;set;} public double? Vram {get;set;} public double? Used {get;set;} public double? Utilization {get;set;} public double? Temperature {get;set;} public double? PowerLimit {get;set;}
        public string DeviceKey {get;set;}
        public bool ReferenceSignature {get{return Name=="NVIDIA GeForce RTX 3060 Ti"&&String.Equals(Device,"0x248910DE",StringComparison.OrdinalIgnoreCase)&&String.Equals(Subsystem,"0x248910DE",StringComparison.OrdinalIgnoreCase)&&String.Equals(Vbios,"94.04.6b.00.f4",StringComparison.OrdinalIgnoreCase)&&Driver=="610.88"&&Vram==8192&&PowerLimit==200;}}
    }
    public sealed class Check {public string Name{get;set;}public string State{get;set;}public string Detail{get;set;}}
    public sealed class Health {
        public string CapturedUtc{get;set;} public string BootUtc{get;set;} public bool Elevated{get;set;} public string Windows{get;set;}
        public List<Gpu> Gpus=new List<Gpu>(); public List<Check> Checks=new List<Check>(); public bool VerifiedExistingInstallation{get;set;}
        public bool? SecureBoot{get;set;} public bool? FastStartup{get;set;} public string InstallRoot{get;set;}
        public RtxMaxBootEvidence.Evidence Boot{get;set;} public List<Partition> Partitions=new List<Partition>();
    }
    public sealed class Partition {public string DriveLetter{get;set;}public string Guid{get;set;}public bool IsSystem{get;set;}public bool IsBoot{get;set;}public string GptType{get;set;}public string BusType{get;set;}}
    public static class Scanner {
        public static List<Gpu> Telemetry(){
            var fields="index,name,pci.bus_id,pci.device_id,pci.sub_device_id,driver_version,vbios_version,memory.total,memory.used,utilization.gpu,temperature.gpu,power.limit,uuid";
            string system=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe"),legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation\\NVSMI\\nvidia-smi.exe");string smi=File.Exists(system)?system:File.Exists(legacy)?legacy:"nvidia-smi.exe";
            string output=Data.Run(smi,"--query-gpu="+fields+" --format=csv,noheader,nounits",15000);
            var result=new List<Gpu>();
            foreach(var line in output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
                var v=line.Split(',').Select(s=>s.Trim().Trim('"')).ToArray();if(v.Length!=13)throw new InvalidDataException("Unrecognized NVIDIA telemetry response.");
                Func<int,double?> number=i=>{double n;return Double.TryParse(v[i],NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:(double?)null;};
                result.Add(new Gpu{Index=Int32.Parse(v[0]),Name=v[1],Bus=v[2],Device=v[3],Subsystem=v[4],Driver=v[5],Vbios=v[6],Vram=number(7),Used=number(8),Utilization=number(9),Temperature=number(10),PowerLimit=number(11),DeviceKey=Data.Hash(Encoding.UTF8.GetBytes(v[12]))});
            }return result;
        }
        static void Add(Health h,string name,string state,string detail){h.Checks.Add(new Check{Name=name,State=state,Detail=detail});}
        public static Health Scan(){
            var h=new Health{CapturedUtc=Data.Utc(),Elevated=Data.Admin};
            try{using(var q=new ManagementObjectSearcher("SELECT Caption,LastBootUpTime FROM Win32_OperatingSystem"))foreach(ManagementObject item in q.Get()){h.Windows=Convert.ToString(item["Caption"]);h.BootUtc=ManagementDateTimeConverter.ToDateTime((string)item["LastBootUpTime"]).ToUniversalTime().ToString("o");}}
            catch{Add(h,"Windows inventory","UNKNOWN","Operating-system metadata unavailable.");}
            try{using(var q=new ManagementObjectSearcher("SELECT Name,ConfigManagerErrorCode FROM Win32_VideoController"))foreach(ManagementObject item in q.Get()){int code=Convert.ToInt32(item["ConfigManagerErrorCode"]);Add(h,Convert.ToString(item["Name"]),code==0?"PASS":"FAIL","Windows device error code "+code+".");}}
            catch{Add(h,"Device status","UNKNOWN","Device Manager status could not be read.");}
            try{h.Gpus=Telemetry();foreach(var gpu in h.Gpus){Add(h,"Unlock eligibility",gpu.ReferenceSignature?"REVIEW":"UNSUPPORTED",gpu.ReferenceSignature?"Reference signature matches. This PC still needs its own preboot qualification.":"This GPU/driver combination is not qualified by the published unlock port.");Add(h,"Temperature",gpu.Temperature==null?"UNKNOWN":gpu.Temperature>=85?"WARN":"PASS",gpu.Temperature==null?"Reading unavailable.":gpu.Temperature.Value.ToString("0",CultureInfo.InvariantCulture)+" °C at scan time; not a stress test.");}}
            catch{Add(h,"NVIDIA telemetry","UNKNOWN","Install an NVIDIA driver with nvidia-smi, then scan again.");}
            try{
                string command=@"$ErrorActionPreference='Stop';$sb=$null;try{$sb=[bool](Confirm-SecureBootUEFI)}catch{};$fs=$null;try{$fs=[bool](Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled)}catch{};$p=@();try{$p=@(Get-Partition|ForEach-Object{$d=$_|Get-Disk;[pscustomobject]@{DriveLetter=[string]$_.DriveLetter;Guid=[string]$_.Guid;IsSystem=[bool]$_.IsSystem;IsBoot=[bool]$_.IsBoot;GptType=[string]$_.GptType;BusType=[string]$d.BusType}})}catch{};$bl=@();try{$bl=@(Get-BitLockerVolume|ForEach-Object{[pscustomobject]@{Mount=[string]$_.MountPoint;Status=[string]$_.VolumeStatus;Protection=[string]$_.ProtectionStatus}})}catch{};[pscustomobject]@{SecureBoot=$sb;FastStartup=$fs;Partitions=$p;BitLocker=$bl}|ConvertTo-Json -Depth 5 -Compress";
                var state=Data.Json().Deserialize<Dictionary<string,object>>(Data.PowerShell(command));
                h.SecureBoot=state["SecureBoot"]==null?(bool?)null:Convert.ToBoolean(state["SecureBoot"]);h.FastStartup=state["FastStartup"]==null?(bool?)null:Convert.ToBoolean(state["FastStartup"]);
                h.Partitions=Data.Json().ConvertToType<List<Partition>>(state["Partitions"]);
                Add(h,"Secure Boot",h.SecureBoot==null?"UNKNOWN":"INFO",h.SecureBoot==null?"Elevation/UEFI needed for this check.":h.SecureBoot.Value?"Enabled; unsigned experimental EFI is blocked.":"Disabled; unsigned EFI can run.");
                Add(h,"Fast Startup",h.FastStartup==null?"UNKNOWN":h.FastStartup.Value?"WARN":"PASS",h.FastStartup==null?"Preference unavailable.":h.FastStartup.Value?"Hybrid startup is not qualified by this project.":"Disabled.");
                foreach(var item in (IEnumerable)state["BitLocker"]){var row=(Dictionary<string,object>)item;Add(h,"BitLocker "+row["Mount"],Convert.ToString(row["Status"])=="FullyEncrypted"&&Convert.ToString(row["Protection"])!="On"?"WARN":"INFO",row["Status"]+" / protection "+row["Protection"]+". No recovery key collected.");}
            }catch{Add(h,"Firmware / encryption","UNKNOWN","Additional checks unavailable. Try Scan as administrator.");}
            try{if(h.Elevated){h.Boot=RtxMaxBootEvidence.ReadCurrent();Add(h,"Actual boot route",h.Boot.UnlockPathMatched?"PASS":"INFO",h.Boot.UnlockPathMatched?"UEFI BootCurrent uses the automatic unlock loader.":"Another boot loader was used; an unlock is not established.");}}
            catch{Add(h,"Actual boot route","UNKNOWN","Firmware boot evidence unavailable.");}
            try{
                var providers=new HashSet<string>(new[]{"Display","nvlddmkm","Microsoft-Windows-WHEA-Logger","Microsoft-Windows-Kernel-WHEA","Microsoft-Windows-WER-SystemErrorReporting","BugCheck"},StringComparer.OrdinalIgnoreCase);int count=0;
                if(String.IsNullOrEmpty(h.BootUtc))throw new InvalidDataException("Boot time unavailable.");string utc=DateTime.Parse(h.BootUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
                var query=new EventLogQuery("System",PathType.LogName,"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '"+utc+"']]]");using(var reader=new EventLogReader(query)){EventRecord entry;while((entry=reader.ReadEvent())!=null)using(entry){if(providers.Contains(entry.ProviderName))count++;}}
                Add(h,"Current-boot events",count==0?"PASS":"WARN",count==0?"No matching GPU/WHEA/BugCheck warnings or errors returned.":count+" relevant events; investigate before calling the system stable.");
            }catch{Add(h,"Current-boot events","UNKNOWN","The relevant System event log could not be read.");}
            try{
                var reference=Data.Json().Deserialize<Dictionary<string,object>>(Data.Resource("reference-artifacts.json"));
                foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed)){
                    string path=Path.Combine(drive.RootDirectory.FullName,"EFI\\PalitUnlock\\AUTO.EFI");if(!File.Exists(path))continue;
                    bool hashes=Data.HashFile(path)==(string)reference["efi_sha256"]&&Data.HashFile(Path.Combine(drive.RootDirectory.FullName,"RTX-UNLOCK\\PROFILE.BIN"))==(string)reference["profile_sha256"];
                    foreach(Dictionary<string,object> asset in (IEnumerable)reference["assets"]){var file=Path.Combine(drive.RootDirectory.FullName,"RTX-UNLOCK\\ASSETS",(string)asset["name"]);hashes=hashes&&File.Exists(file)&&Data.HashFile(file)==(string)asset["sha256"];}
                    var partition=h.Partitions.SingleOrDefault(p=>p.DriveLetter.Equals(drive.Name.Substring(0,1),StringComparison.OrdinalIgnoreCase));
                    bool boot=h.Boot!=null&&h.Boot.UnlockPathMatched&&!h.Boot.UsbNodePresent&&partition!=null&&partition.BusType!="USB"&&String.Equals(partition.Guid.Trim('{','}'),h.Boot.PartitionGuid,StringComparison.OrdinalIgnoreCase);
                    Add(h,"Installed files",hashes?"PASS":"REVIEW",hashes?"EFI, profile and five assets match the tested reference artifacts.":"A different or modified installation needs its own qualification record.");
                    h.VerifiedExistingInstallation=hashes&&boot&&h.Gpus.Count==1&&h.Gpus[0].ReferenceSignature&&!h.Checks.Any(c=>c.State=="FAIL"||(c.Name=="Current-boot events"&&c.State!="PASS"));h.InstallRoot=drive.Name;
                }
            }catch{Add(h,"Installation inspection","UNKNOWN","Some EFI files/volumes could not be read. Try elevation.");}
            if(!h.VerifiedExistingInstallation)Add(h,"Firmware setup","REVIEW","No verified existing internal installation established. A GPU name match never enables automatic setup.");
            Add(h,"Compute unlock","UNKNOWN","Only a matched, correctness-checked before/after measurement can establish a gain.");
            return h;
        }
        public static string Eligibility(Health h){if(h==null)return "NOT SCANNED";if(h.VerifiedExistingInstallation)return "VERIFIED EXISTING INSTALLATION";if(h.Gpus.Any(g=>g.ReferenceSignature))return "PREBOOT QUALIFICATION REQUIRED";return h.Gpus.Count==0?"DETECTION INCOMPLETE":"UNSUPPORTED BY THIS UNLOCK PORT";}
    }
    public static class SourcePackage {
        public static string Extract(){
            byte[] source=Data.ResourceBytes("source.zip");string root=Path.Combine(Data.Home,Data.Version,"source-"+Data.Hash(source).Substring(0,12));Data.NoReparseParents(root);Directory.CreateDirectory(root);
            using(var memory=new MemoryStream(source))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)){
                foreach(var entry in zip.Entries){if(String.IsNullOrEmpty(entry.Name))continue;string dest=Path.GetFullPath(Path.Combine(root,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));if(!Data.IsInside(dest,root))throw new InvalidDataException("Unsafe embedded source path.");Data.NoReparseParents(Path.GetDirectoryName(dest));byte[] bytes;using(var s=entry.Open())using(var b=new MemoryStream()){s.CopyTo(b);bytes=b.ToArray();}Directory.CreateDirectory(Path.GetDirectoryName(dest));if(File.Exists(dest)){if(Data.HashFile(dest)!=Data.Hash(bytes))throw new IOException("The extracted source was modified. Choose a fresh version directory before using setup tools.");}else File.WriteAllBytes(dest,bytes);}
            }return root;
        }
    }
}
