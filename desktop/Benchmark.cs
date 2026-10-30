// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RtxMax {
    public sealed class BenchRequest {public string Stage{get;set;}public string LibraryRoot{get;set;}public string DeviceKey{get;set;}public bool Smoke{get;set;}}
    public sealed class Measurement {public string Mode{get;set;}public int N{get;set;}public int Samples{get;set;}public double WarmupSeconds{get;set;}public double MedianMs{get;set;}public double Tflops{get;set;}public long ElementsChecked{get;set;}public bool Correct{get;set;}public bool Nonconstant{get;set;}}
    public sealed class BenchResult {
        public int Schema{get;set;}=1;public string Protocol{get;set;}="rtxmax-native-gemm-v1";public string Stage{get;set;}public string CapturedUtc{get;set;}public string CompletedUtc{get;set;}
        public string BootUtc{get;set;}public string BootPath{get;set;}public bool StockBootObserved{get;set;}public string EngineHash{get;set;}public bool Passed{get;set;}public string Error{get;set;}
        public Gpu Gpu{get;set;}public int CudaVersion{get;set;}public int BlasVersion{get;set;}public int ComputeMajor{get;set;}public int ComputeMinor{get;set;}public bool Smoke{get;set;}
        public Dictionary<string,string> Libraries=new Dictionary<string,string>();public List<Measurement> Measurements=new List<Measurement>();public List<string> Skipped=new List<string>();public double MaxSampledTemperature{get;set;}
        public string Limitation{get;set;}="Finite ordinary CUDA measurement. A throughput change is not by itself proof of an unlock. No tuning, firmware or driver settings are changed.";
    }
    public sealed class ComparisonRow {public string Mode{get;set;}public double Before{get;set;}public double After{get;set;}public double Gain{get;set;}}
    public static class Compare {
        public static List<ComparisonRow> Results(BenchResult before,BenchResult after){
            if(before==null||after==null||!before.Passed||!after.Passed||before.Smoke||after.Smoke)throw new InvalidDataException("Two completed full measurements are required.");
            if(before.Stage!="Before"||after.Stage!="After")throw new InvalidDataException("Select a Before file and an After file.");
            if(!before.StockBootObserved)throw new InvalidDataException("The Before file does not establish a normal Windows boot with Fast Startup off.");
            DateTime oldBoot,newBoot;if(!DateTime.TryParse(before.BootUtc,null,DateTimeStyles.RoundtripKind,out oldBoot)||!DateTime.TryParse(after.BootUtc,null,DateTimeStyles.RoundtripKind,out newBoot)||newBoot.ToUniversalTime()<=oldBoot.ToUniversalTime())throw new InvalidDataException("The After measurement must follow a newer Windows boot.");
            var a=before.Gpu;var b=after.Gpu;
            if(a==null||b==null||String.IsNullOrEmpty(a.DeviceKey)||a.DeviceKey!=b.DeviceKey||a.Bus!=b.Bus||a.Device!=b.Device||a.Subsystem!=b.Subsystem||a.Driver!=b.Driver||a.Vbios!=b.Vbios||a.PowerLimit!=b.PowerLimit)throw new InvalidDataException("GPU identity, driver, VBIOS, bus or power limit changed.");
            if(before.Protocol!=after.Protocol||before.EngineHash!=after.EngineHash||before.CudaVersion!=after.CudaVersion||before.BlasVersion!=after.BlasVersion||before.Libraries.Count!=after.Libraries.Count||before.Libraries.Any(p=>!after.Libraries.ContainsKey(p.Key)||after.Libraries[p.Key]!=p.Value))throw new InvalidDataException("Benchmark engine or CUDA/cuBLAS libraries changed.");
            var output=new List<ComparisonRow>();
            foreach(string mode in before.Measurements.Where(m=>!m.Nonconstant).Select(m=>m.Mode).Distinct()){
                var first=before.Measurements.Where(m=>m.Mode==mode&&!m.Nonconstant).ToList();var last=after.Measurements.Where(m=>m.Mode==mode&&!m.Nonconstant).ToList();
                if(first.Count!=3||last.Count!=3||first.Concat(last).Any(m=>!m.Correct||m.N!=4096||m.Samples!=30||m.WarmupSeconds!=2||m.ElementsChecked!=4096L*4096))throw new InvalidDataException("Workload/correctness evidence is incomplete or inconsistent.");
                if(first.Concat(last).Any(m=>m.MedianMs<=0||Double.IsNaN(m.MedianMs)||Double.IsInfinity(m.MedianMs)||Double.IsNaN(m.Tflops)||Double.IsInfinity(m.Tflops)||Math.Abs(m.Tflops-2.0*m.N*m.N*m.N/(m.MedianMs*1e9))>Math.Max(1e-8,m.Tflops*1e-8)))throw new InvalidDataException("Throughput does not match the recorded timing.");
                if(!before.Measurements.Any(m=>m.Mode==mode&&m.Nonconstant&&m.Correct&&m.N==128&&m.ElementsChecked==128*128)||!after.Measurements.Any(m=>m.Mode==mode&&m.Nonconstant&&m.Correct&&m.N==128&&m.ElementsChecked==128*128))throw new InvalidDataException("Independent nonconstant output verification is missing.");
                double x=NativeCuda.Median(first.Select(m=>m.Tflops)),y=NativeCuda.Median(last.Select(m=>m.Tflops));if(x<=0||Double.IsNaN(x)||Double.IsInfinity(y))throw new InvalidDataException("Invalid timing values.");output.Add(new ComparisonRow{Mode=mode,Before=x,After=y,Gain=(y/x-1)*100});
            }if(output.Count==0)throw new InvalidDataException("No matched modes.");return output;
        }
    }
    public sealed class NativeCuda : IDisposable {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr AddDllDirectory(string path);
        [DllImport("kernel32.dll")]static extern bool RemoveDllDirectory(IntPtr cookie);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int IntOut(out int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int OneInt(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Zero();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PointerOut(out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PointerOne(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int TwoPointer(IntPtr a,IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MemAlloc(out IntPtr value,UIntPtr bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Copy(IntPtr dest,IntPtr src,UIntPtr bytes,int kind);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Elapsed(out float ms,IntPtr start,IntPtr end);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Attr(out int value,int attr,int device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int BusId(StringBuilder text,int length,int device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int BlasInt(IntPtr handle,int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int BlasVersion(IntPtr handle,out int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Gemm(IntPtr handle,int opA,int opB,int m,int n,int k,ref float alpha,IntPtr a,int typeA,int lda,IntPtr b,int typeB,int ldb,ref float beta,IntPtr c,int typeC,int ldc,int compute,int algo);
        readonly List<IntPtr> modules=new List<IntPtr>();readonly List<IntPtr> cookies=new List<IntPtr>();
        IntPtr runtime,blas,handle;MemAlloc malloc;Copy copy;PointerOne free,eventDestroy,eventSync;PointerOut eventCreate;TwoPointer eventRecord;Elapsed elapsed;Zero sync;BlasInt math;Gemm gemm;
        public int CudaVersion,LibraryVersion,Major,Minor;public Dictionary<string,string> Hashes=new Dictionary<string,string>();
        T Symbol<T>(IntPtr module,string name)where T:class{var address=GetProcAddress(module,name);if(address==IntPtr.Zero)throw new InvalidDataException("CUDA library export missing: "+name);return (T)(object)Marshal.GetDelegateForFunctionPointer(address,typeof(T));}
        static void Check(int code,string action){if(code!=0)throw new InvalidOperationException(action+" failed (CUDA/cuBLAS status "+code+"). The GPU/library combination may not support this mode.");}
        static IEnumerable<string> Dlls(string root){var pending=new Queue<Tuple<string,int>>();pending.Enqueue(Tuple.Create(Path.GetFullPath(root),0));int seen=0;while(pending.Count>0){var row=pending.Dequeue();if(++seen>3000)throw new IOException("Choose the CUDA installation/library directory, not a whole drive.");foreach(var file in Directory.EnumerateFiles(row.Item1,"*.dll"))yield return file;if(row.Item2<7)foreach(var child in Directory.EnumerateDirectories(row.Item1)){if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Enqueue(Tuple.Create(child,row.Item2+1));}}}
        public NativeCuda(string directory,Gpu gpu){
            if(IntPtr.Size!=8)throw new InvalidOperationException("Use the Windows x64 application.");
            if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES"))||!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("NVIDIA_TF32_OVERRIDE")))throw new InvalidOperationException("Unset CUDA_VISIBLE_DEVICES and NVIDIA_TF32_OVERRIDE for a matched measurement.");
            var files=Dlls(directory).ToList();string runtimePath=files.FirstOrDefault(f=>Path.GetFileName(f).Equals("cudart64_13.dll",StringComparison.OrdinalIgnoreCase))??files.FirstOrDefault(f=>Path.GetFileName(f).Equals("cudart64_12.dll",StringComparison.OrdinalIgnoreCase));
            if(runtimePath==null)throw new FileNotFoundException("Select CUDA 12/13 libraries containing cudart64_12.dll or cudart64_13.dll.");string suffix=runtimePath.EndsWith("_13.dll",StringComparison.OrdinalIgnoreCase)?"13":"12";
            string blasPath=files.FirstOrDefault(f=>Path.GetFileName(f).Equals("cublas64_"+suffix+".dll",StringComparison.OrdinalIgnoreCase));string ltPath=files.FirstOrDefault(f=>Path.GetFileName(f).Equals("cublasLt64_"+suffix+".dll",StringComparison.OrdinalIgnoreCase));
            if(blasPath==null||ltPath==null)throw new FileNotFoundException("Matching cuBLAS and cuBLASLt DLLs are required.");
            foreach(var folder in new[]{Path.GetDirectoryName(runtimePath),Path.GetDirectoryName(blasPath),Path.GetDirectoryName(ltPath)}.Distinct()){var cookie=AddDllDirectory(folder);if(cookie==IntPtr.Zero)throw new IOException("CUDA library search directory could not be registered.");cookies.Add(cookie);}
            foreach(var file in new[]{ltPath,runtimePath,blasPath}){var module=LoadLibraryEx(Path.GetFullPath(file),IntPtr.Zero,0x100|0x1000);if(module==IntPtr.Zero)throw new IOException("Cannot load CUDA libraries. Install the matching Microsoft VC++ x64 runtime if needed.");modules.Add(module);Hashes.Add(Path.GetFileName(file),Data.HashFile(file));if(file==runtimePath)runtime=module;if(file==blasPath)blas=module;}
            int count;Check(Symbol<IntOut>(runtime,"cudaGetDeviceCount")(out count),"Device enumeration");int selected=-1;
            for(int i=0;i<count;i++){var id=new StringBuilder(80);Check(Symbol<BusId>(runtime,"cudaDeviceGetPCIBusId")(id,80,i),"PCI bus query");if(NormalizeBus(id.ToString())==NormalizeBus(gpu.Bus))selected=i;}
            if(selected<0)throw new InvalidOperationException("CUDA and NVIDIA telemetry did not identify the same PCI device.");Check(Symbol<OneInt>(runtime,"cudaSetDevice")(selected),"Device selection");
            Check(Symbol<Attr>(runtime,"cudaDeviceGetAttribute")(out Major,75,selected),"Compute capability");Check(Symbol<Attr>(runtime,"cudaDeviceGetAttribute")(out Minor,76,selected),"Compute capability");Check(Symbol<IntOut>(runtime,"cudaRuntimeGetVersion")(out CudaVersion),"CUDA version");
            Check(Symbol<PointerOut>(blas,"cublasCreate_v2")(out handle),"cuBLAS context");Check(Symbol<BlasVersion>(blas,"cublasGetVersion_v2")(handle,out LibraryVersion),"cuBLAS version");
            malloc=Symbol<MemAlloc>(runtime,"cudaMalloc");free=Symbol<PointerOne>(runtime,"cudaFree");copy=Symbol<Copy>(runtime,"cudaMemcpy");eventCreate=Symbol<PointerOut>(runtime,"cudaEventCreate");eventDestroy=Symbol<PointerOne>(runtime,"cudaEventDestroy");eventSync=Symbol<PointerOne>(runtime,"cudaEventSynchronize");eventRecord=Symbol<TwoPointer>(runtime,"cudaEventRecord");elapsed=Symbol<Elapsed>(runtime,"cudaEventElapsedTime");sync=Symbol<Zero>(runtime,"cudaDeviceSynchronize");math=Symbol<BlasInt>(blas,"cublasSetMathMode");gemm=Symbol<Gemm>(blas,"cublasGemmEx");
        }
        public static string NormalizeBus(string bus){var p=bus.ToLowerInvariant().Split(':');if(p.Length!=3)return bus.ToLowerInvariant();return Convert.ToInt32(p[0],16).ToString("x4")+":"+p[1]+":"+p[2];}
        public static double Median(IEnumerable<double> values){var x=values.OrderBy(v=>v).ToArray();if(x.Length==0)throw new InvalidDataException("No timings.");return x.Length%2==1?x[x.Length/2]:(x[x.Length/2-1]+x[x.Length/2])/2;}
        static byte[] Input(float[] values,string mode){if(mode=="FP32"||mode=="TF32"){var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);return bytes;}var output=new byte[values.Length*2];for(int i=0;i<values.Length;i++){ushort v;if(mode=="BF16")v=(ushort)(BitConverter.ToUInt32(BitConverter.GetBytes(values[i]),0)>>16);else{int quarter=(int)Math.Round(values[i]*4);int sign=quarter<0?0x8000:0;int a=Math.Abs(quarter);if(a>4)throw new InvalidOperationException("Unexpected validation input.");v=(ushort)(sign+new[]{0,0x3400,0x3800,0x3a00,0x3c00}[a]);}output[2*i]=(byte)v;output[2*i+1]=(byte)(v>>8);}return output;}
        public Measurement Measure(string mode,int n,int samples,double warmup,bool nonconstant){
            float[] a=new float[n*n],b=new float[n*n],expected=nonconstant?new float[n*n]:null;var random=new Random(2489);
            for(int i=0;i<a.Length;i++){a[i]=nonconstant?(random.Next(9)-4)/4f:1;b[i]=nonconstant?(random.Next(9)-4)/4f:1;}
            if(nonconstant)for(int col=0;col<n;col++)for(int row=0;row<n;row++){double sum=0;for(int k=0;k<n;k++)sum+=(double)a[row+k*n]*b[k+col*n];expected[row+col*n]=(float)sum;}
            byte[] inputA=Input(a,mode),inputB=Input(b,mode);float[] output=new float[n*n];IntPtr da=IntPtr.Zero,db=IntPtr.Zero,dc=IntPtr.Zero,start=IntPtr.Zero,stop=IntPtr.Zero;
            try{
                Check(malloc(out da,(UIntPtr)(uint)inputA.Length),"Allocate A");Check(malloc(out db,(UIntPtr)(uint)inputB.Length),"Allocate B");Check(malloc(out dc,(UIntPtr)(uint)(output.Length*4)),"Allocate output");
                var pinA=GCHandle.Alloc(inputA,GCHandleType.Pinned);var pinB=GCHandle.Alloc(inputB,GCHandleType.Pinned);try{Check(copy(da,pinA.AddrOfPinnedObject(),(UIntPtr)(uint)inputA.Length,1),"Upload A");Check(copy(db,pinB.AddrOfPinnedObject(),(UIntPtr)(uint)inputB.Length,1),"Upload B");}finally{pinA.Free();pinB.Free();}
                Check(math(handle,mode=="FP32"?2:mode=="TF32"?3:1),"Math mode");int dataType=mode=="FP16"?2:mode=="BF16"?14:0;int compute=mode=="FP32"?69:mode=="TF32"?77:68;float alpha=1,beta=0;
                Action multiply=()=>Check(gemm(handle,0,0,n,n,n,ref alpha,da,dataType,n,db,dataType,n,ref beta,dc,0,n,compute,mode=="FP32"?-1:99),mode+" GEMM");
                var watch=Stopwatch.StartNew();do{multiply();Check(sync(),"Warmup synchronization");}while(watch.Elapsed.TotalSeconds<warmup);
                Check(eventCreate(out start),"Create timing event");Check(eventCreate(out stop),"Create timing event");var times=new List<double>();
                for(int i=0;i<samples;i++){Check(eventRecord(start,IntPtr.Zero),"Start timing");multiply();Check(eventRecord(stop,IntPtr.Zero),"Stop timing");Check(eventSync(stop),"Timing synchronization");float ms;Check(elapsed(out ms,start,stop),"Elapsed time");if(ms<=0||Single.IsNaN(ms)||Single.IsInfinity(ms))throw new InvalidDataException("Invalid CUDA timing.");times.Add(ms);}
                var pin=GCHandle.Alloc(output,GCHandleType.Pinned);try{Check(copy(pin.AddrOfPinnedObject(),dc,(UIntPtr)(uint)(output.Length*4),2),"Read output");}finally{pin.Free();}
                for(int i=0;i<output.Length;i++)if(output[i]!=(nonconstant?expected[i]:n)||Single.IsNaN(output[i])||Single.IsInfinity(output[i]))throw new InvalidDataException(mode+": full output correctness check failed.");
                double median=Median(times);return new Measurement{Mode=mode,N=n,Samples=samples,WarmupSeconds=warmup,MedianMs=median,Tflops=2.0*n*n*n/(median*1e9),ElementsChecked=(long)n*n,Correct=true,Nonconstant=nonconstant};
            }finally{if(start!=IntPtr.Zero)eventDestroy(start);if(stop!=IntPtr.Zero)eventDestroy(stop);if(da!=IntPtr.Zero)free(da);if(db!=IntPtr.Zero)free(db);if(dc!=IntPtr.Zero)free(dc);}
        }
        public void Dispose(){if(handle!=IntPtr.Zero){Symbol<PointerOne>(blas,"cublasDestroy_v2")(handle);handle=IntPtr.Zero;}foreach(var module in modules.AsEnumerable().Reverse())FreeLibrary(module);foreach(var cookie in cookies)RemoveDllDirectory(cookie);}
    }
    public static class BenchmarkWorker {
        public static int Run(string requestPath,string outputPath){
            var request=Data.Read<BenchRequest>(requestPath);var result=new BenchResult{Stage=request.Stage,CapturedUtc=Data.Utc(),Smoke=request.Smoke,EngineHash=Data.Hash(Encoding.UTF8.GetBytes(Data.Resource("engine-source")))};NativeCuda cuda=null;
            try{
                if(!new[]{"Before","After","Current"}.Contains(request.Stage))throw new InvalidDataException("Unknown measurement stage.");
                var health=Scanner.Scan();result.BootUtc=health.BootUtc;result.BootPath=health.Boot==null?null:health.Boot.FilePath;
                result.StockBootObserved=health.FastStartup==false&&result.BootPath!=null&&result.BootPath.Equals("\\EFI\\Microsoft\\Boot\\bootmgfw.efi",StringComparison.OrdinalIgnoreCase);
                if(request.Stage=="Before"&&!result.StockBootObserved)throw new InvalidOperationException("A Before baseline requires an elevated scan confirming normal Windows Boot Manager and Fast Startup off. Use Current to test performance without claiming a stock baseline.");
                var gpu=health.Gpus.SingleOrDefault(g=>g.DeviceKey==request.DeviceKey);if(gpu==null)throw new InvalidDataException("Selected GPU is no longer present.");if(!gpu.Name.Contains("RTX"))throw new InvalidOperationException("This desktop workflow is intended for NVIDIA RTX GPUs.");
                var until=DateTime.UtcNow.AddSeconds(60);while(!request.Smoke&&(gpu.Utilization>5||gpu.Temperature>65||gpu.Used>2048)){Console.WriteLine("Waiting for idle GPU (<=5% use, <=65 C, <=2048 MiB VRAM used)...");if(DateTime.UtcNow>until)throw new InvalidOperationException("GPU remained busy. Close GPU workloads and retry.");System.Threading.Thread.Sleep(2000);gpu=Scanner.Telemetry().Single(g=>g.DeviceKey==request.DeviceKey);}
                if(gpu.Temperature==null||gpu.PowerLimit==null||gpu.Vram==null||gpu.Utilization==null)throw new InvalidOperationException("Required temperature/power/memory telemetry is unavailable.");
                if(gpu.Temperature>=85)throw new InvalidOperationException("Temperature guard: GPU is at or above 85 C.");result.Gpu=gpu;
                cuda=new NativeCuda(request.LibraryRoot,gpu);result.CudaVersion=cuda.CudaVersion;result.BlasVersion=cuda.LibraryVersion;result.ComputeMajor=cuda.Major;result.ComputeMinor=cuda.Minor;result.Libraries=cuda.Hashes;
                var modes=new List<string>{"FP32","FP16"};if(cuda.Major>=8){modes.Add("BF16");modes.Add("TF32");}else{result.Skipped.Add("BF16: compute capability below 8.0");result.Skipped.Add("TF32: compute capability below 8.0");}
                foreach(string mode in modes){Console.WriteLine("Checking "+mode+" against an independent seeded CPU reference...");result.Measurements.Add(cuda.Measure(mode,128,1,0,true));}
                for(int trial=0;trial<(request.Smoke?1:3);trial++)foreach(string mode in modes){var reading=Scanner.Telemetry().Single(g=>g.DeviceKey==request.DeviceKey);if(reading.Temperature==null||reading.Temperature>=85||reading.Driver!=gpu.Driver||reading.Vbios!=gpu.Vbios||reading.PowerLimit!=gpu.PowerLimit)throw new InvalidOperationException("Temperature or configuration guard stopped the measurement.");result.MaxSampledTemperature=Math.Max(result.MaxSampledTemperature,reading.Temperature.Value);var measurement=cuda.Measure(mode,request.Smoke?256:4096,request.Smoke?3:30,request.Smoke?0:2,false);result.Measurements.Add(measurement);Console.WriteLine("Trial "+(trial+1)+" / "+mode+": "+measurement.Tflops.ToString("0.00",CultureInfo.InvariantCulture)+" TFLOPS; all output elements passed.");}
                var end=Scanner.Telemetry().Single(g=>g.DeviceKey==request.DeviceKey);if(end.Temperature==null||end.Temperature>=85||end.Driver!=gpu.Driver||end.PowerLimit!=gpu.PowerLimit||end.Vbios!=gpu.Vbios)throw new InvalidOperationException("Final temperature/configuration guard failed.");result.MaxSampledTemperature=Math.Max(result.MaxSampledTemperature,end.Temperature.Value);result.Passed=true;
            }catch(Exception error){result.Error=error.Message;Console.WriteLine("Stopped: "+error.Message);}finally{if(cuda!=null)cuda.Dispose();result.CompletedUtc=Data.Utc();Data.Save(outputPath,result);}
            return result.Passed?0:1;
        }
    }
}
