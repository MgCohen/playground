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

    /// <summary>
    /// Opens a handle to a process and reads its parent id and its start time (UTC) from that handle. While the handle is
    /// open, Windows does not give the id to another process, also after the process ended. Null: the process is gone or
    /// not readable.
    /// </summary>
    public static ProcessHandle? Open(int pid)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = OpenProcess(QueryLimited | TerminateRight | Synchronize, false, (uint)pid);
        if (handle == IntPtr.Zero) handle = OpenProcess(QueryLimited | Synchronize, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        var info = new BasicInformation();
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)
            || NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<BasicInformation>(), out _) != 0)
        {
            CloseHandle(handle);
            return null;
        }
        return new ProcessHandle(handle, pid, (int)info.InheritedFromUniqueProcessId, DateTime.FromFileTimeUtc(created));
    }

    /// <summary>An open handle to one process. It keeps the id of the process from reuse until <see cref="Dispose"/>.</summary>
    public sealed class ProcessHandle : IDisposable
    {
        IntPtr handle;

        internal ProcessHandle(IntPtr handle, int pid, int parent, DateTime start)
        {
            this.handle = handle;
            Pid = pid;
            Parent = parent;
            Start = start;
        }

        public int Pid { get; }
        public int Parent { get; }
        public DateTime Start { get; }

        public bool HasExited => handle == IntPtr.Zero || WaitForSingleObject(handle, 0) == 0;

        /// <summary>Ends this process (never another one: the handle names it). False when it already ended.</summary>
        public bool Terminate()
        {
            if (HasExited || !TerminateProcess(handle, 1)) return false;
            WaitForSingleObject(handle, 2000);
            return true;
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }

    const uint QueryLimited = 0x1000;
    const uint TerminateRight = 0x0001;
    const uint Synchronize = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    struct BasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int informationClass, ref BasicInformation information, int length, out int returned);

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
