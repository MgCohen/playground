using System.Diagnostics;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

/// <summary>One open handle to a process. While it is open, the id of the process is not given to another process.</summary>
public interface IProcessHandle : IDisposable
{
    int Pid { get; }
    int Parent { get; }
    DateTime Start { get; }
    bool Terminate();
}

/// <summary>The process list that a <see cref="ProcessTree"/> reads. The tests give a fake one to make each race exact.</summary>
public interface IProcessTable
{
    /// <summary>Each process with the parent id that Windows keeps, also for an orphan.</summary>
    IReadOnlyList<(int Pid, int Parent)> Snapshot();

    /// <summary>Opens the process that has this id now. Null: no such process, or no access.</summary>
    IProcessHandle? Open(int pid);
}

public sealed class WindowsProcessTable : IProcessTable
{
    public static readonly WindowsProcessTable Instance = new();

    public IReadOnlyList<(int Pid, int Parent)> Snapshot() => Processes.Snapshot();

    public IProcessHandle? Open(int pid) => Processes.Open(pid) is { } handle ? new Handle(handle) : null;

    sealed class Handle(Processes.ProcessHandle handle) : IProcessHandle
    {
        public int Pid => handle.Pid;
        public int Parent => handle.Parent;
        public DateTime Start => handle.Start;
        public bool Terminate() => handle.Terminate();
        public void Dispose() => handle.Dispose();
    }
}

/// <summary>
/// Records the descendants of a root process while it runs, so that a kill also ends a descendant
/// whose parent stopped before the kill. Windows keeps the parent id of such an orphan, but
/// <see cref="Process.Kill(bool)"/> cannot find it from the root.
/// </summary>
/// <remarks>
/// The tree keeps an open handle to each process that it records, until <see cref="Dispose"/>. Windows does not give the
/// id of a process to another process while a handle to it is open. So a later process with that parent id is a real
/// child, and a kill through the held handle never ends another process. The parent id and the start time of a new
/// child come from its own handle, not from the snapshot, because the snapshot can be old.
/// </remarks>
public sealed class ProcessTree : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    readonly Process? root;
    readonly IProcessTable table;
    readonly Dictionary<int, IProcessHandle> known = [];
    readonly Lock gate = new();
    readonly Timer? timer;
    bool disposed;

    public ProcessTree(Process root, TimeSpan interval) : this(root.Id, WindowsProcessTable.Instance, interval)
    {
        this.root = root;
    }

    /// <summary>A tree on a process table. With no interval, the tree scans only when <see cref="Scan"/> is called.</summary>
    public ProcessTree(int rootId, IProcessTable table, TimeSpan? interval = null)
    {
        this.table = table;
        if (table.Open(rootId) is { } handle) known[rootId] = handle;
        if (interval is { } every) timer = new Timer(_ => Scan(), null, TimeSpan.Zero, every);
    }

    public IReadOnlyList<int> Known
    {
        get { lock (gate) return [.. known.Keys]; }
    }

    static bool Unsupported(IProcessTable table) => table is WindowsProcessTable && !OperatingSystem.IsWindows();

    public void Scan()
    {
        if (Unsupported(table)) return;
        lock (gate)
        {
            if (disposed) return;
            var entries = table.Snapshot();
            bool added;
            do
            {
                added = false;
                foreach (var (pid, parent) in entries)
                {
                    if (known.ContainsKey(pid) || !known.TryGetValue(parent, out var parentHandle)) continue;
                    if (table.Open(pid) is not { } handle) continue;
                    // The snapshot can be old: the id can belong to a new process now. Its own handle tells its parent and start.
                    if (handle.Parent != parent || handle.Start < parentHandle.Start)
                    {
                        handle.Dispose();
                        continue;
                    }
                    known[pid] = handle;
                    added = true;
                }
            }
            while (added);
        }
    }

    public void KillAll()
    {
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (Unsupported(table))
        {
            try { root?.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return;
        }
        for (var round = 0; round < 5; round++)
        {
            Scan();
            List<IProcessHandle> targets;
            lock (gate) targets = [.. known.Values];
            var killed = targets.Count(handle => handle.Terminate());
            if (killed == 0) return;
        }
    }

    public void Dispose()
    {
        timer?.Dispose();
        lock (gate)
        {
            disposed = true;
            foreach (var handle in known.Values) handle.Dispose();
        }
    }

    public static DateTime? StartTime(int pid) => Processes.StartTime(pid);

    public static IReadOnlyList<(int Pid, int Parent)> Snapshot() => Processes.Snapshot();
}
