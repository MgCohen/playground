using System.Diagnostics;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

[Platform("Win")]
public sealed class ProcessTreeTests
{
    readonly List<int> leftovers = [];

    [TearDown]
    public void EndLeftovers()
    {
        foreach (var pid in leftovers)
            try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
        leftovers.Clear();
    }

    [Test]
    public void The_kill_of_dotnet_misses_a_descendant_whose_parent_ended()
    {
        var folder = Directory.CreateTempSubdirectory("orch-tree-").FullName;
        using var root = StartOrphanMaker(folder);
        var orphan = WaitForOrphan(folder);
        root.Kill(entireProcessTree: true);
        root.WaitForExit();
        Assert.That(IsAlive(orphan), Is.True);
    }

    [Test]
    public void Kill_all_ends_a_descendant_whose_parent_ended_before_the_kill()
    {
        var folder = Directory.CreateTempSubdirectory("orch-tree-").FullName;
        using var root = StartOrphanMaker(folder);
        using var tree = new ProcessTree(root, TimeSpan.FromMilliseconds(100));
        var orphan = WaitForOrphan(folder);
        tree.KillAll();
        Assert.That(tree.Known, Does.Contain(orphan));
        Assert.That(tree.Known.Where(IsAlive), Is.Empty);
    }

    [Test]
    public void Archive_removes_the_worktree_after_kill_all_ends_a_process_that_held_it()
    {
        using var t = new Rig();
        var id = WorktreeSession(t);
        var worktree = t.Store.Meta(id).Cwd!;
        using var root = StartOrphanMaker(worktree);
        using var tree = new ProcessTree(root, TimeSpan.FromMilliseconds(100));
        WaitForOrphan(worktree);
        var lifecycle = new Lifecycle(t.Store);
        lifecycle.Stop(id, Parties.User);
        tree.KillAll();
        var lines = lifecycle.Archive(id, Parties.User, false);
        Assert.That(lines[^1], Does.StartWith("Removed the worktree"));
        Assert.That(Directory.Exists(worktree), Is.False);
    }

    [Test]
    public void Archive_tries_again_while_a_file_in_the_worktree_is_held_for_a_short_time()
    {
        using var t = new Rig();
        var id = WorktreeSession(t);
        var worktree = t.Store.Meta(id).Cwd!;
        File.WriteAllText(Path.Combine(worktree, "held.txt"), "held");
        new Shell(worktree).MustGit("add", "held.txt");
        new Shell(worktree).MustGit("-c", "user.email=t@t", "-c", "user.name=t", "commit", "--quiet", "-m", "held");
        var held = new FileStream(Path.Combine(worktree, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var delays = Lifecycle.RemoveDelays;
        Lifecycle.RemoveDelays = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(300), 10)];
        try
        {
            _ = Task.Delay(900).ContinueWith(_ => held.Dispose());
            var lines = new Lifecycle(t.Store).Archive(id, Parties.User, false);
            Assert.That(lines[^1], Does.StartWith("Removed the worktree"));
            Assert.That(Directory.Exists(worktree), Is.False);
        }
        finally
        {
            Lifecycle.RemoveDelays = delays;
            held.Dispose();
        }
    }

    static string WorktreeSession(Rig t)
    {
        var repo = Directory.CreateTempSubdirectory("orch-wt-").FullName;
        var shell = new Shell(repo);
        shell.MustGit("init", "--quiet", "-b", "main");
        shell.MustGit("-c", "user.email=t@t", "-c", "user.name=t", "commit", "--quiet", "--allow-empty", "-m", "init");
        var id = t.Store.Create(new Meta("") { Cwd = repo, CreatedBy = "supervisor", Worktree = true }, "a");
        new Lifecycle(t.Store).MakeWorktree(id);
        return id;
    }

    static Process StartOrphanMaker(string cwd)
    {
        var scripts = Directory.CreateTempSubdirectory("orch-tree-scripts-").FullName;
        var pidFile = Path.Combine(scripts, "orphan.pid");
        File.WriteAllText(Path.Combine(scripts, "middle.ps1"),
            $"$p = Start-Process powershell -ArgumentList '-NoProfile','-Command','Start-Sleep 120' -NoNewWindow -PassThru; Set-Content -Path '{pidFile}' -Value $p.Id");
        File.WriteAllText(Path.Combine(scripts, "root.ps1"),
            $"& powershell -NoProfile -File '{Path.Combine(scripts, "middle.ps1")}'; Start-Sleep 120");
        var info = new ProcessStartInfo("powershell") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(scripts, "root.ps1") }) info.ArgumentList.Add(argument);
        PidFiles[cwd] = pidFile;
        return Process.Start(info)!;
    }

    static readonly Dictionary<string, string> PidFiles = [];

    int WaitForOrphan(string cwd)
    {
        var pidFile = PidFiles[cwd];
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (File.Exists(pidFile) && int.TryParse(ReadShared(pidFile).Trim(), out var pid))
            {
                leftovers.Add(pid);
                var parent = ProcessTree.Snapshot().FirstOrDefault(e => e.Pid == pid).Parent;
                while (parent != 0 && IsAlive(parent) && watch.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(100);
                Assert.That(IsAlive(pid), Is.True);
                return pid;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("the orphan did not start");
    }

    static string ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(stream).ReadToEnd();
        }
        catch (IOException)
        {
            return "";
        }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
