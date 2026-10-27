using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskPanel.Interop;

internal sealed class ProcEntry
{
    public uint Pid;
    public string Exe = "";
    public string Path = "";
    public string CmdLine = "";
}

/// <summary>进程快照 + 命令行读取（同用户进程，非提权）。</summary>
internal static class ProcessProbe
{
    public static List<ProcEntry> Snapshot()
    {
        var list = new List<ProcEntry>();
        IntPtr snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;

        try
        {
            var pe = new Native.PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<Native.PROCESSENTRY32W>(),
            };
            if (!Native.Process32FirstW(snap, ref pe)) return list;

            do
            {
                var e = new ProcEntry { Pid = pe.th32ProcessID, Exe = pe.szExeFile ?? "" };
                list.Add(e);
            }
            while (Native.Process32NextW(snap, ref pe));
        }
        finally
        {
            Native.CloseHandle(snap);
        }

        return list;
    }

    public static string TryGetPath(uint pid)
    {
        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Native.QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        catch { return ""; }
        finally { Native.CloseHandle(h); }
    }

    /// <summary>读 64 位进程的 PEB → CommandLine。失败返回空串。</summary>
    public static string TryGetCmdLine(uint pid)
    {
        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var pbi = new Native.PROCESS_BASIC_INFORMATION();
            int len;
            if (Native.NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf<Native.PROCESS_BASIC_INFORMATION>(), out len) != 0)
                return "";
            if (pbi.PebBaseAddress == IntPtr.Zero) return "";

            IntPtr paramsPtr = ReadPtr(h, pbi.PebBaseAddress + 0x20);
            if (paramsPtr == IntPtr.Zero) return "";

            // UNICODE_STRING 在 RTL_USER_PROCESS_PARAMETERS + 0x70
            IntPtr usPtr = paramsPtr + 0x70;
            int usLen = ReadU16(h, usPtr);
            IntPtr bufPtr = ReadPtr(h, usPtr + 8);
            if (usLen <= 0 || usLen > 0x8000 || bufPtr == IntPtr.Zero) return "";

            IntPtr mem = Marshal.AllocHGlobal(usLen);
            try
            {
                if (!Native.ReadProcessMemory(h, bufPtr, mem, new IntPtr(usLen), out _)) return "";
                return Marshal.PtrToStringUni(mem, usLen / 2) ?? "";
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
        catch { return ""; }
        finally { Native.CloseHandle(h); }
    }

    private static IntPtr ReadPtr(IntPtr h, IntPtr addr)
    {
        IntPtr buf = Marshal.AllocHGlobal(8);
        try
        {
            return Native.ReadProcessMemory(h, addr, buf, new IntPtr(8), out _) ? Marshal.ReadIntPtr(buf) : IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static int ReadU16(IntPtr h, IntPtr addr)
    {
        IntPtr buf = Marshal.AllocHGlobal(4);
        try
        {
            if (!Native.ReadProcessMemory(h, addr, buf, new IntPtr(4), out _)) return 0;
            return (ushort)Marshal.ReadInt16(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
