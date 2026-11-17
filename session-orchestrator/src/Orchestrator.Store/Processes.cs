using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Orchestrator.Core.Leases;

namespace Orchestrator.Store;

/// <summary>The process list of Windows: the parent of each process, the start time, and the alive check of a lease.</summary>
public static class Processes
{
    /// <summary>The tolerance of a start time compare: the lease keeps milliseconds only.</summary>
    static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    public static DateTime? StartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>The parent process id of <paramref name="pid"/>, or null.</summary>
    public static int? ParentOf(int pid)
    {
        foreach (var (id, parent) in Snapshot())
            if (id == pid) return parent;
        return null;
    }

    /// <summary>True when the process <paramref name="pid"/> is alive and started at <paramref name="started"/> (an ISO time).</summary>
    public static bool IsAlive(int pid, string? started)
    {
        if (StartTime(pid) is not { } start) return false;
        if (Times.Parse(started) is not { } recorded) return true;
        return (start.ToUniversalTime() - recorded.UtcDateTime).Duration() < Tolerance;
    }

    public static IReadOnlyList<(int Pid, int Parent)> Snapshot()
    {
        var entries = new List<(int, int)>();
        if (!OperatingSystem.IsWindows()) return entries;
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle) return entries;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                entries.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return entries;
    }

    const uint SnapProcess = 0x2;
    static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
