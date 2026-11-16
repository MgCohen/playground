using Orchestrator.Cli;
using Orchestrator.Cli.Git;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class CliTests
{
    static string NewRepo()
    {
        var dir = Directory.CreateTempSubdirectory("orch-repo-").FullName;
        var shell = new Shell(dir);
        shell.MustGit("init", "--quiet", "-b", "main");
        shell.MustGit("config", "user.email", "test@example.com");
        shell.MustGit("config", "user.name", "test");
        Commit(shell, "init.txt", "init");
        return dir;
    }

    static void Commit(Shell shell, string file, string text)
    {
        File.WriteAllText(Path.Combine(shell.Cwd, file), text);
        shell.MustGit("add", file);
        shell.MustGit("commit", "--quiet", "-m", $"write {file}");
    }

    static (Rig Rig, Shell Shell, StackOps Ops, string Session) Setup()
    {
        var rig = new Rig();
        var repo = NewRepo();
        var shell = new Shell(repo);
        var session = rig.Create("start");
        return (rig, shell, new StackOps(rig.Store, shell, TextWriter.Null), session);
    }

    [Test]
    public void Decision_start_makes_a_stack_of_branches_and_records_each_base()
    {
        var (rig, shell, ops, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "medium", "A");
        rig.Decide(session, "d2", "medium", "B");
        ops.Start("d1");
        Commit(shell, "shape.txt", "A");
        ops.Start("d2");
        Commit(shell, "size.txt", "B");
        Assert.That(shell.CurrentBranch(), Is.EqualTo("orch/decision-d2"));
        var d2 = rig.Store.Stack(session, "d2")!.Current!;
        Assert.That((d2.Base, d2.BaseDecision, d2.State), Is.EqualTo(("orch/decision-d1", "d1", VersionStates.Started)));
        ops.OpenPr("d1");
        Assert.That(rig.Store.Stack(session, "d1")!.Current!.Pr, Is.Null);
        Assert.That(rig.Store.Events.All().Count(e => e.Kind == EventKinds.BranchCreate), Is.EqualTo(2));
    }

    [Test]
    public void Stack_rebase_moves_each_branch_above_a_change()
    {
        var (rig, shell, ops, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "medium", "A");
        rig.Decide(session, "d2", "medium", "B");
        rig.Decide(session, "d3", "medium", "C");
        ops.Start("d1");
        Commit(shell, "a.txt", "A");
        ops.Start("d2");
        Commit(shell, "b.txt", "B");
        ops.Start("d3");
        Commit(shell, "c.txt", "C");
        shell.Checkout("orch/decision-d1");
        Commit(shell, "a.txt", "A2");
        ops.Rebase("d1");
        Assert.That(shell.CurrentBranch(), Is.EqualTo("orch/decision-d3"));
        Assert.That(shell.Git("merge-base", "--is-ancestor", "orch/decision-d1", "orch/decision-d3").Ok, Is.True);
        Assert.That(File.ReadAllText(Path.Combine(shell.Cwd, "a.txt")), Is.EqualTo("A2"));
    }

    [Test]
    public void Stack_close_closes_the_chain_plans_v2_branches_and_checks_out_the_base()
    {
        var (rig, shell, ops, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "medium", "A");
        rig.Decide(session, "d2", "medium", "B");
        rig.Decide(session, "d3", "medium", "C");
        ops.Start("d1");
        Commit(shell, "a.txt", "A");
        ops.Start("d2");
        Commit(shell, "b.txt", "B");
        ops.Start("d3");
        Commit(shell, "c.txt", "C");
        ops.Close("d2", "use MEDIUM");
        Assert.That(shell.CurrentBranch(), Is.EqualTo("orch/decision-d1"));
        var d2 = rig.Store.Stack(session, "d2")!;
        var d3 = rig.Store.Stack(session, "d3")!;
        Assert.That(d2.Versions.Select(v => v.State), Is.EqualTo(new[] { VersionStates.Closed, VersionStates.Planned }));
        Assert.That((d2.Newest!.Branch, d2.Newest.Base), Is.EqualTo(("orch/decision-d2-v2", "orch/decision-d1")));
        Assert.That((d3.Newest!.Branch, d3.Newest.Base), Is.EqualTo(("orch/decision-d3-v2", "orch/decision-d2-v2")));
        Assert.That(rig.Store.Stack(session, "d1")!.Current!.State, Is.EqualTo(VersionStates.Started));
        ops.Start("d2");
        Commit(shell, "a.txt", "A MEDIUM");
        ops.Start("d3");
        Assert.That(shell.CurrentBranch(), Is.EqualTo("orch/decision-d3-v2"));
        Assert.That(rig.Store.Stack(session, "d3")!.Current!.Base, Is.EqualTo("orch/decision-d2-v2"));
    }

    [Test]
    public void A_review_after_a_refuse_names_the_newest_version()
    {
        var (rig, shell, ops, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "medium", "A");
        ops.Start("d1");
        Commit(shell, "a.txt", "A");
        rig.Supervisor.Review(session, "d1", Verdicts.Refuse, "use B");
        ops.Close("d1", "use B");
        ops.Start("d1");
        Commit(shell, "a.txt", "B");
        var inbox = rig.Supervisor.Review(session, "d1", Verdicts.Change, "use C");
        var text = File.ReadAllText(rig.Store.PathOf(session, StoreFiles.Inbox, inbox));
        Assert.That(text, Does.Contain("orch/decision-d1-v2"));
        Assert.That(rig.Store.Review(session, "d1-v2")!.Verdict, Is.EqualTo(Verdicts.Change));
        Assert.That(rig.Store.Review(session, "d1")!.Verdict, Is.EqualTo(Verdicts.Refuse));
    }

    [Test]
    public void The_refuse_text_names_the_v2_branch()
    {
        var (rig, shell, ops, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "medium", "A");
        ops.Start("d1");
        var inbox = rig.Supervisor.Review(session, "d1", Verdicts.Refuse, "use B");
        var text = File.ReadAllText(rig.Store.PathOf(session, StoreFiles.Inbox, inbox));
        Assert.That(text, Does.Contain("orch stack close d1"));
        Assert.That(text, Does.Contain("orch/decision-d1-v2"));
        Assert.That(text, Does.Contain("main"));
    }

    [Test]
    public void Answer_and_events_run_through_the_command()
    {
        using var rig = new Rig();
        var session = rig.Create("start", "root");
        rig.Decide(session, "d1", "high", null);
        Environment.SetEnvironmentVariable("ORCH_STORE", rig.Folder);
        try
        {
            var output = new StringWriter();
            Assert.That(Cli.Cli.Run(["decisions"], output, output, rig.Folder), Is.EqualTo(0));
            Assert.That(output.ToString(), Does.Contain("d1 [high]"));
            Assert.That(Cli.Cli.Run(["answer", "d1", "GREEN", "please"], output, output, rig.Folder), Is.EqualTo(0));
            Assert.That(rig.Store.Answer(session, "d1")!.Value, Is.EqualTo("GREEN please"));
            var events = new StringWriter();
            Assert.That(Cli.Cli.Run(["events", "--kind", "answer", "--session", session[..8]], events, events, rig.Folder), Is.EqualTo(0));
            Assert.That(events.ToString(), Does.Contain("GREEN please"));
            Assert.That(Cli.Cli.Run(["review", "d1", "maybe"], output, output, rig.Folder), Is.EqualTo(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }
}
