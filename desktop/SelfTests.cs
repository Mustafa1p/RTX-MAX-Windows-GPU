// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RtxMax {
    public static class SelfTests {
        static int count;
        static void Require(bool value,string message){if(!value)throw new Exception("Self-test failed: "+message);count++;}
        static void Refuses(Action action,string label){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}catch(InvalidOperationException){refused=true;}Require(refused,label);}
        static byte[] Profile(){var bytes=new byte[176];Encoding.ASCII.GetBytes("GUPROF17").CopyTo(bytes,0);BitConverter.GetBytes(1U).CopyTo(bytes,8);BitConverter.GetBytes(176U).CopyTo(bytes,12);BitConverter.GetBytes(0x248910deU).CopyTo(bytes,16);BitConverter.GetBytes(0x248910deU).CopyTo(bytes,20);BitConverter.GetBytes(0xb74000a1U).CopyTo(bytes,24);string hash=Data.Hash(bytes.Take(144).ToArray());for(int i=0;i<32;i++)bytes[144+i]=Convert.ToByte(hash.Substring(i*2,2),16);return bytes;}
        static BenchResult Result(string stage){var result=new BenchResult{Stage=stage,Passed=true,StockBootObserved=stage=="Before",BootUtc=stage=="Before"?"2026-01-01T00:00:00Z":"2026-01-02T00:00:00Z",EngineHash="fixture-engine",Gpu=new Gpu{DeviceKey="synthetic-test-only",Bus="00000000:05:00.0",Driver="test",Vbios="test",PowerLimit=200},Libraries=new Dictionary<string,string>{{"test.dll","fixture"}}};for(int i=0;i<3;i++){double ms=stage=="Before"?4:2;result.Measurements.Add(new Measurement{Mode="FP32",N=4096,Samples=30,WarmupSeconds=2,MedianMs=ms,Tflops=2.0*4096*4096*4096/(ms*1e9),ElementsChecked=4096L*4096,Correct=true});}result.Measurements.Add(new Measurement{Mode="FP32",N=128,ElementsChecked=128*128,Nonconstant=true,Correct=true});return result;}
        public static int Run(){
            Require(NativeCuda.NormalizeBus("00000000:05:00.0")=="0000:05:00.0","PCI bus normalization");Require(NativeCuda.Median(new[]{1.0,4.0,2.0})==2,"odd median");Require(NativeCuda.Median(new[]{1.0,4.0,2.0,3.0})==2.5,"even median");
            Setup.ValidateProfile(Profile());Require(true,"valid profile structure");var corrupt=Profile();corrupt[100]^=1;Refuses(()=>Setup.ValidateProfile(corrupt),"corrupt profile");Refuses(()=>Setup.ValidateProfile(new byte[175]),"short profile");var wrong=Profile();wrong[16]^=1;Refuses(()=>Setup.ValidateProfile(wrong),"wrong target/checksum");Refuses(()=>Setup.ValidateEfi(new byte[512]),"invalid EFI");
            Require(Compare.Results(Result("Before"),Result("After"))[0].Gain==100,"matched comparison");
            var after=Result("After");after.Gpu.Driver="changed";Refuses(()=>Compare.Results(Result("Before"),after),"driver mismatch");
            after=Result("After");after.EngineHash="changed";Refuses(()=>Compare.Results(Result("Before"),after),"engine mismatch");
            after=Result("After");after.Measurements[0].Tflops=999;Refuses(()=>Compare.Results(Result("Before"),after),"tampered timing");
            after=Result("After");after.Measurements[0].Correct=false;Refuses(()=>Compare.Results(Result("Before"),after),"correctness failure");
            after=Result("After");after.Smoke=true;Refuses(()=>Compare.Results(Result("Before"),after),"smoke is not a full benchmark");
            after=Result("After");after.BootUtc=Result("Before").BootUtc;Refuses(()=>Compare.Results(Result("Before"),after),"same boot");
            var before=Result("Before");before.StockBootObserved=false;Refuses(()=>Compare.Results(before,Result("After")),"unverified stock baseline");
            after=Result("After");after.Libraries["test.dll"]="changed";Refuses(()=>Compare.Results(Result("Before"),after),"library mismatch");
            after=Result("After");after.Measurements.RemoveAt(3);Refuses(()=>Compare.Results(Result("Before"),after),"missing independent reference");
            Require(Scanner.Eligibility(null)=="NOT SCANNED","empty eligibility");Require(Scanner.Eligibility(new Health())=="DETECTION INCOMPLETE","unknown is not supported");
            var unsupported=new Health{Gpus=new List<Gpu>{new Gpu{Name="NVIDIA GeForce RTX 4090"}}};Require(Scanner.Eligibility(unsupported)=="UNSUPPORTED BY THIS UNLOCK PORT","other RTX firmware gate");
            Refuses(()=>Setup.Stage(unsupported,"missing","missing","missing","missing",true),"unqualified GPU staging refusal");
            Require(!Data.IsInside(Path.Combine(Path.GetTempPath(),"outside"),Path.Combine(Path.GetTempPath(),"inside")),"output path containment");
            using(var memory=new MemoryStream(Data.ResourceBytes("source.zip")))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)){Require(zip.GetEntry("README.md")!=null&&zip.GetEntry("desktop/MainWindow.xaml")!=null,"embedded source and UI are present");Require(zip.Entries.All(e=>!e.FullName.Contains("..")&&!e.FullName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&!e.FullName.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)),"embedded source excludes payloads");}
            Console.WriteLine("PASS: "+count+" native backend checks; synthetic data only, no GPU/boot changes.");return 0;
        }
    }
}
