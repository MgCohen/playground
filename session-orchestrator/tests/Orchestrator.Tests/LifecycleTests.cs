using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

public sealed class LifecycleTests
{
    [Test]
    public async Task A_stop_ends_the_running_turn_keeps_the_inbox_and_starts_no_new_turn()
    {
        using var t = new Rig();
        t.Runner.Gate = new TaskCompletionSource();
        var id = t.Create("work");
        t.Supervisor.Tick();
        Assert.That(t.Supervisor.IsRunning(id), Is.True);
        new Lifecycle(t.Store).Stop(id, Parties.User);
        await t.Cycle();
        Assert.That(t.Supervisor.IsRunning(id), Is.False);
        Assert.That(t.Store.Pending(id).Text, Is.EqualTo("work"));
        t.Runner.Gate = null;
        await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(1));
        new Lifecycle(t.Store).Resume(id, Parties.User);
        await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(2));
        Assert.That(t.Store.Pending(id).IsEmpty, Is.True);
    }

    [Test]
    public async Task A_session_restarts_after_the_cooldown_with_a_blocked_notice()
    {
        using var t = new Rig(ok: false);
        var id = t.Create("hello");
        for (var i = 0; i < 3; i++) await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(2));
        Assert.That(t.Store.Blocked(id)?.Reason, Does.Contain("Next try at"));
        t.Clock.Advance(TimeSpan.FromMinutes(6));
        await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(3));
        Assert.That(t.Turns[^1].Prompt, Does.Contain("The supervisor starts this session again after 2 failed turns"));
        Assert.That(t.Store.Events.All().Any(e => e.Kind == EventKinds.Unblocked), Is.True);
    }

    [Test]
    public async Task An_archive_waits_for_open_decisions_and_open_prs_unless_forced()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Decide(id, "d1", "high", null);
        var lifecycle = new Lifecycle(t.Store);
        Assert.Throws<MessageException>(() => lifecycle.Archive(id, Parties.User, false));
        t.Store.SaveAnswer(id, "d1", new Answer { Value = "A" });
        t.Store.SaveStack(new StackEntry("d1", id) { Versions = [new StackVersion(1, "orch/decision-d1", "main") { Pr = 7, State = VersionStates.Open }] });
        var refused = Assert.Throws<MessageException>(() => lifecycle.Archive(id, Parties.User, false))!;
        Assert.That(refused.Message, Does.Contain("#7"));
        lifecycle.Archive(id, Parties.User, true);
        Assert.That(t.Store.Sessions(), Does.Not.Contain(id));
        Assert.That(lifecycle.Control(id).State, Is.EqualTo(ControlStates.Archived));
    }

    [Test]
    public async Task Two_sessions_with_worktrees_get_their_own_folder_and_branch_in_one_repository()
    {
        using var t = new Rig();
        var repo = Directory.CreateTempSubdirectory("orch-wt-").FullName;
        var shell = new Shell(repo);
        shell.MustGit("init", "--quiet", "-b", "main");
        shell.MustGit("-c", "user.email=t@t", "-c", "user.name=t", "commit", "--quiet", "--allow-empty", "-m", "init");
        var a = t.Store.Create(new Meta("") { Cwd = repo, CreatedBy = "supervisor", Worktree = true }, "a");
        var b = t.Store.Create(new Meta("") { Cwd = repo, CreatedBy = "supervisor", Worktree = true }, "b");
        await t.Cycle();
        var cwds = t.Turns.Select(turn => turn.Cwd).ToList();
        Assert.That(cwds.Distinct().Count(), Is.EqualTo(2));
        foreach (var id in new[] { a, b })
        {
            var meta = t.Store.Meta(id);
            Assert.That(Directory.Exists(meta.Cwd), Is.True);
            Assert.That(new Shell(meta.Cwd!).CurrentBranch(), Is.EqualTo(meta.Branch));
            Assert.That(meta.Repo, Is.EqualTo(repo.Replace('\\', '/')).IgnoreCase);
        }
        var lines = new Lifecycle(t.Store).Archive(a, Parties.User, false);
        Assert.That(lines[^1], Does.StartWith("Removed the worktree"));
        Assert.That(Directory.Exists(t.Store.Meta(a).Cwd), Is.False);
        Assert.That(shell.BranchExists(t.Store.Meta(a).Branch!), Is.True);
    }

    [Test]
    public void The_open_command_starts_the_desktop_app_with_the_session()
    {
        var command = Lifecycle.OpenCommand("abc");
        Assert.That(command[^1], Is.EqualTo("Start-Process -FilePath 'claude.exe' -ArgumentList '--desktop','--resume','abc' -WindowStyle Hidden"));
    }
}
