// GPL-2.0-only. Read-only UEFI startup evidence; no firmware write API.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class RtxMaxBootEvidence {
    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct Privilege { public uint Count; public Luid Id; public uint Attributes; }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref Privilege state, uint length, out Privilege previous, out uint returnedLength);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFirmwareEnvironmentVariable(string name, string vendorGuid, byte[] buffer, uint size);
    public sealed class Evidence {
        public string BootVariable;
        public string Description;
        public string FilePath;
        public string PartitionGuid;
        public bool UsbNodePresent;
        public bool UnlockPathMatched;
    }
    static byte[] Read(string name) {
        byte[] data = new byte[16384];
        uint size = GetFirmwareEnvironmentVariable(name, "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}", data, (uint)data.Length);
        if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (size > data.Length) throw new InvalidOperationException("Firmware value exceeds bound");
        Array.Resize(ref data, (int)size);
        return data;
    }
    public static Evidence ReadCurrent() {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Privilege old = new Privilege(); bool changed = false;
        try {
            Luid id; uint length;
            if (!LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out id)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Privilege wanted = new Privilege { Count=1, Id=id, Attributes=2 };
            if (!AdjustTokenPrivileges(token, false, ref wanted, (uint)Marshal.SizeOf(typeof(Privilege)), out old, out length) || Marshal.GetLastWin32Error()!=0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            changed = true;
            byte[] current = Read("BootCurrent");
            if (current.Length != 2) throw new InvalidOperationException("Invalid BootCurrent");
            string variable = "Boot" + BitConverter.ToUInt16(current,0).ToString("X4");
            Evidence evidence = ParseOption(Read(variable));
            evidence.BootVariable = variable;
            return evidence;
        } finally {
            if (changed && old.Count != 0) {
                Privilege ignored; uint count;
                AdjustTokenPrivileges(token, false, ref old, (uint)Marshal.SizeOf(typeof(Privilege)), out ignored, out count);
            }
            CloseHandle(token);
        }
    }
    // Exposed separately for tests: parsing has no OS or hardware access.
    public static Evidence ParseOption(byte[] option) {
        if (option == null || option.Length < 8) throw new ArgumentException("Truncated boot option");
        int end=6;
        while (end+1<option.Length && (option[end]!=0 || option[end+1]!=0)) end+=2;
        if (end+1>=option.Length) throw new ArgumentException("Unterminated description");
        int start=end+2, size=BitConverter.ToUInt16(option,4);
        if (size<4 || start+size>option.Length) throw new ArgumentException("Invalid device path bounds");
        Evidence result=new Evidence { Description=Encoding.Unicode.GetString(option,6,end-6), FilePath="" };
        int position=start, nodes=0; bool ended=false;
        while (position<start+size && ++nodes<=64) {
            if (position+4>start+size) throw new ArgumentException("Truncated node");
            int len=BitConverter.ToUInt16(option,position+2);
            if (len<4 || position+len>start+size) throw new ArgumentException("Invalid node length");
            int type=option[position], sub=option[position+1];
            if (type==3 && (sub==5 || sub==15 || sub==16)) result.UsbNodePresent=true;
            if (type==4 && sub==1) {
                if (len!=42 || result.PartitionGuid!=null) throw new ArgumentException("Invalid/ambiguous hard drive node");
                if (option[position+41]==2) {
                    byte[] guid=new byte[16]; Array.Copy(option,position+24,guid,0,16);
                    result.PartitionGuid=new Guid(guid).ToString();
                }
            }
            if (type==4 && sub==4) {
                if ((len-4)%2!=0) throw new ArgumentException("Invalid file path encoding");
                result.FilePath+=Encoding.Unicode.GetString(option,position+4,len-4).TrimEnd('\0');
            }
            if (type==127) {
                if (sub!=255 || len!=4 || position+len!=start+size) throw new ArgumentException("Invalid path terminator");
                ended=true; break;
            }
            position+=len;
        }
        if (!ended) throw new ArgumentException("Missing path terminator");
        result.UnlockPathMatched=String.Equals(result.FilePath,"\\EFI\\PalitUnlock\\AUTO.EFI",StringComparison.OrdinalIgnoreCase)
            || String.Equals(result.FilePath,"\\EFI\\RTXMAX\\AUTO.EFI",StringComparison.OrdinalIgnoreCase);
        return result;
    }
}
