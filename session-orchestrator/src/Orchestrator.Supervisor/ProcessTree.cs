using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orchestrator.Supervisor;

/// <summary>
/// Records the descendants of a root process while it runs, so that a kill also ends a descendant
/// whose parent stopped before the kill. Windows keeps the parent id of such an orphan, but
/// <see cref="Process.Kill(bool)"/> cannot find it from the root.
/// </summary>
public sealed class ProcessTree : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    readonly Process root;
    readonly Dictionary<int, DateTime> known = [];
    readonly Lock gate = new();
    readonly Timer timer;

    public ProcessTree(Process root, TimeSpan interval)
    {
        this.root = root;
        if (StartTime(root.Id) is { } start) known[root.Id] = start;
        timer = new Timer(_ => Scan(), null, TimeSpan.Zero, interval);
    }

    public IReadOnlyList<int> Known
    {
        get { lock (gate) return [.. known.Keys]; }
    }

    public void Scan()
    {
        if (!OperatingSystem.IsWindows()) return;
        var entries = Snapshot();
        lock (gate)
        {
            foreach (var (pid, _) in entries)
                if (known.TryGetValue(pid, out var recorded) && StartTime(pid) is { } now && now != recorded) known.Remove(pid);
            bool added;
            do
            {
                added = false;
                foreach (var (pid, parent) in entries)
                {
                    if (known.ContainsKey(pid) || !known.TryGetValue(parent, out var parentStart)) continue;
                    if (StartTime(pid) is not { } start || start < parentStart) continue;
                    known[pid] = start;
                    added = true;
                }
            }
            while (added);
        }
    }

    public void KillAll()
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (!OperatingSystem.IsWindows())
        {
            try { root.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return;
        }
        for (var round = 0; round < 5; round++)
        {
            Scan();
            List<KeyValuePair<int, DateTime>> targets;
            lock (gate) targets = [.. known];
            var killed = 0;
            foreach (var (pid, start) in targets)
                if (Kill(pid, start)) killed++;
            if (killed == 0) return;
        }
    }

    public void Dispose() => timer.Dispose();

    static bool Kill(int pid, DateTime start)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime != start) return false;
            process.Kill();
            process.WaitForExit(2000);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

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

    public static IReadOnlyList<(int Pid, int Parent)> Snapshot()
    {
        var entries = new List<(int, int)>();
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
