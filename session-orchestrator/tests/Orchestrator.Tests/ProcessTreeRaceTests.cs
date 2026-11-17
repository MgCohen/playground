using System.Diagnostics;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

/// <summary>
/// The races of the process tree with a fake process table. Windows gives the id of an ended process to a new process;
/// the fake does the same, but never while a handle to the old process is open (as Windows).
/// </summary>
public sealed class ProcessTreeRaceTests
{
    static readonly DateTime T0 = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    sealed class FakeProcess(int pid, int parent, DateTime start)
    {
        public int Pid => pid;
        public int Parent => parent;
        public DateTime Start => start;
        public bool Alive { get; set; } = true;
        public bool Killed { get; set; }
        public int Holds { get; set; }
    }

    sealed class FakeTable : IProcessTable
    {
        readonly Dictionary<int, FakeProcess> byPid = [];
        readonly List<FakeProcess> all = [];

        /// <summary>The snapshot that the next Scan reads. Null: the current processes.</summary>
        public List<(int Pid, int Parent)>? StaleSnapshot { get; set; }

        public FakeProcess Start(int pid, int parent, DateTime start)
        {
            if (byPid.TryGetValue(pid, out var old) && (old.Alive || old.Holds > 0))
                throw new InvalidOperationException($"pid {pid} is in use or held: Windows does not give it again");
            var process = new FakeProcess(pid, parent, start);
            byPid[pid] = process;
            all.Add(process);
            return process;
        }

        public void End(FakeProcess process) => process.Alive = false;

        public bool CanReuse(int pid) => !byPid.TryGetValue(pid, out var p) || !p.Alive && p.Holds == 0;

        public IReadOnlyList<(int Pid, int Parent)> Snapshot() =>
            StaleSnapshot ?? [.. byPid.Values.Where(p => p.Alive).Select(p => (p.Pid, p.Parent))];

        public IProcessHandle? Open(int pid)
        {
            if (!byPid.TryGetValue(pid, out var process) || !process.Alive) return null;
            process.Holds++;
            return new Handle(process);
        }

        sealed class Handle(FakeProcess process) : IProcessHandle
        {
            bool open = true;
            public int Pid => process.Pid;
            public int Parent => process.Parent;
            public DateTime Start => process.Start;
            public bool HasExited => !process.Alive;

            public bool Terminate()
            {
                if (!open) throw new ObjectDisposedException("handle", "a kill used a handle that Dispose closed");
                if (!process.Alive) return false;
                process.Alive = false;
                process.Killed = true;
                return true;
            }

            public void Dispose()
            {
                if (open) process.Holds--;
                open = false;
            }
        }
    }

    [Test]
    public void A_pid_that_a_new_process_took_after_the_snapshot_is_not_recorded_and_not_killed()
    {
        var table = new FakeTable();
        var root = table.Start(100, 1, T0);
        var child = table.Start(200, 100, T0.AddSeconds(1));
        var snapshot = table.Snapshot().ToList();
        table.End(child);
        var stranger = table.Start(200, 7, T0.AddSeconds(5));
        table.StaleSnapshot = snapshot;
        using var tree = new ProcessTree(root.Pid, table);
        tree.Scan();
        Assert.That(tree.Known, Is.EqualTo(new[] { 100 }));
        Assert.That(stranger.Holds, Is.Zero, "the tree closed the handle of the stranger");
        tree.KillAll();
        Assert.That(stranger.Killed, Is.False);
        Assert.That(root.Killed, Is.True);
    }

    [Test]
    public void A_process_older_than_the_holder_of_its_parent_id_is_not_a_child()
    {
        var table = new FakeTable();
        var old = table.Start(300, 50, T0);
        var root = table.Start(50, 1, T0.AddSeconds(10));
        using var tree = new ProcessTree(root.Pid, table);
        tree.Scan();
        Assert.That(tree.Known, Is.EqualTo(new[] { 50 }), "300 names pid 50 as parent, but it started before this pid 50");
        tree.KillAll();
        Assert.That(old.Killed, Is.False);
    }

    [Test]
    public void An_orphan_is_killed_and_the_id_of_its_ended_parent_stays_held_until_dispose()
    {
        var table = new FakeTable();
        var root = table.Start(100, 1, T0);
        var middle = table.Start(110, 100, T0.AddSeconds(1));
        var tree = new ProcessTree(root.Pid, table);
        tree.Scan();
        var orphan = table.Start(120, 110, T0.AddSeconds(2));
        table.End(middle);
        Assert.That(table.CanReuse(110), Is.False, "the tree holds the ended parent: its id cannot name a new process");
        tree.Scan();
        Assert.That(tree.Recorded, Is.EquivalentTo(new[] { 100, 110, 120 }));
        Assert.That(tree.Known, Is.EquivalentTo(new[] { 100, 120 }), "review of PR #14, item 6: the scan after its end closed the handle of the parent");
        Assert.That(table.CanReuse(110), Is.True);
        tree.KillAll();
        Assert.That((orphan.Killed, root.Killed, middle.Killed), Is.EqualTo((true, true, false)));
        tree.Dispose();
        Assert.That(root.Holds + middle.Holds + orphan.Holds, Is.Zero);
    }

    [Test]
    public void A_parent_that_ends_after_the_snapshot_keeps_its_handle_until_the_next_scan()
    {
        var table = new FakeTable();
        var root = table.Start(100, 1, T0);
        using var tree = new ProcessTree(root.Pid, table);
        var middle = table.Start(110, 100, T0.AddSeconds(1));
        tree.Scan();
        var late = table.Start(120, 110, T0.AddSeconds(2));
        table.End(middle);
        Assert.That(tree.Known, Does.Contain(110), "it ended after the last snapshot: a child can still be unseen");
        tree.Scan();
        Assert.That(tree.Recorded, Does.Contain(late.Pid));
        Assert.That(tree.Known, Does.Not.Contain(110));
    }

    [Test]
    public void Kill_all_and_dispose_at_the_same_time_never_use_a_closed_handle()
    {
        for (var i = 0; i < 200; i++)
        {
            var table = new FakeTable();
            var root = table.Start(100, 1, T0);
            for (var c = 0; c < 20; c++) table.Start(200 + c, 100, T0.AddSeconds(1));
            var tree = new ProcessTree(root.Pid, table);
            tree.Scan();
            var kill = Task.Run(tree.KillAll);
            var dispose = Task.Run(tree.Dispose);
            Assert.DoesNotThrowAsync(async () => await Task.WhenAll(kill, dispose));
        }
    }

    [Test]
    public void A_process_that_ended_before_the_first_scan_is_not_recorded_and_its_later_reuse_is_safe()
    {
        var table = new FakeTable();
        var root = table.Start(100, 1, T0);
        using var tree = new ProcessTree(root.Pid, table);
        var quick = table.Start(130, 100, T0.AddSeconds(1));
        table.End(quick);
        var other = table.Start(130, 9, T0.AddSeconds(3));
        var grandchild = table.Start(140, 130, T0.AddSeconds(4));
        tree.Scan();
        Assert.That(tree.Known, Is.EqualTo(new[] { 100 }));
        tree.KillAll();
        Assert.That((other.Killed, grandchild.Killed), Is.EqualTo((false, false)));
    }

    [Test, Platform("Win")]
    public void A_real_tree_never_records_the_test_process_or_its_parent()
    {
        using var self = Process.GetCurrentProcess();
        var info = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true };
        using var root = Process.Start(info)!;
        using var tree = new ProcessTree(root, TimeSpan.FromMilliseconds(50));
        Thread.Sleep(300);
        tree.KillAll();
        root.WaitForExit(5000);
        var parent = Processes.ParentOf(self.Id);
        Assert.That(tree.Recorded, Does.Contain(root.Id));
        Assert.That(tree.Recorded, Does.Not.Contain(self.Id));
        if (parent is { } p) Assert.That(tree.Recorded, Does.Not.Contain(p));
        Assert.That(self.HasExited, Is.False);
    }

    [Test, Platform("Win")]
    public void A_handle_reads_the_parent_and_the_start_of_one_process()
    {
        using var self = Process.GetCurrentProcess();
        using var handle = Processes.Open(self.Id)!;
        Assert.That(handle.Parent, Is.EqualTo(Processes.ParentOf(self.Id)));
        Assert.That(handle.Start, Is.EqualTo(self.StartTime.ToUniversalTime()).Within(TimeSpan.FromMilliseconds(1)));
        Assert.That(handle.HasExited, Is.False);
    }
}
