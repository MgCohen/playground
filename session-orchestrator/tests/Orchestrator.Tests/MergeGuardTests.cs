using Orchestrator.Cli.Git;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class MergeGuardTests
{
    static (Rig Rig, Shell Shell, StackOps Ops, MergeGuard Guard, string Session) Setup(bool allowDefaults = false)
    {
        var rig = new Rig();
        var shell = new Shell(Directory.CreateTempSubdirectory("orch-merge-").FullName);
        shell.MustGit("init", "--quiet", "-b", "main");
        shell.MustGit("config", "user.email", "test@example.com");
        shell.MustGit("config", "user.name", "test");
        Commit(shell, "init.txt", "init");
        var session = rig.Create("start");
        if (allowDefaults) rig.Store.Change(session, e => e with { Settings = e.Settings.With(SettingTiers.Permissions, SettingTiers.AllowDefaults, true) });
        return (rig, shell, new StackOps(rig.Store, shell, TextWriter.Null), new MergeGuard(rig.Store, shell, TextWriter.Null), session);
    }

    static void Commit(Shell shell, string file, string text)
    {
        File.WriteAllText(Path.Combine(shell.Cwd, file), text);
        shell.MustGit("add", file);
        shell.MustGit("commit", "--quiet", "-m", $"write {file}");
    }

    static void Default(Rig rig, StackOps ops, Shell shell, string session, string decision)
    {
        rig.Decide(session, decision, "medium", "A");
        rig.Store.SaveAnswer(session, decision, new Answer { Value = "A", By = Parties.Policy });
        ops.Start(decision);
        Commit(shell, $"{decision}.txt", "A");
    }

    static void Work(Shell shell)
    {
        shell.MustGit("checkout", "--quiet", "-b", "work");
        Commit(shell, "work.txt", "work");
    }

    [Test]
    public void A_branch_on_an_unreviewed_default_is_not_merged()
    {
        var (rig, shell, ops, guard, session) = Setup();
        using var _ = rig;
        Default(rig, ops, shell, session, "d1");
        Work(shell);
        Assert.That(guard.Merge(new MergeTarget("work", "main", null)), Is.EqualTo(1));
        Assert.That(shell.Git("merge-base", "--is-ancestor", "work", "main").Ok, Is.False);
        Assert.That(rig.Store.Events.All().Count(e => e.Kind == EventKinds.MergeBlock), Is.EqualTo(1));
    }

    [Test]
    public void A_branch_on_an_approved_default_is_merged_and_the_default_is_marked_merged()
    {
        var (rig, shell, ops, guard, session) = Setup();
        using var _ = rig;
        Default(rig, ops, shell, session, "d1");
        Work(shell);
        rig.Store.SaveReview(session, "d1", Verdicts.Approve, "");
        Assert.That(guard.Merge(new MergeTarget("work", "main", null)), Is.EqualTo(0));
        Assert.That(shell.Git("merge-base", "--is-ancestor", "work", "main").Ok, Is.True);
        Assert.That(rig.Store.Stack(session, "d1")!.Current!.State, Is.EqualTo(VersionStates.Merged));
        Assert.That(rig.Store.Events.All().Count(e => e.Kind == EventKinds.PrMerge), Is.EqualTo(1));
    }

    [Test]
    public void A_change_review_still_blocks_the_merge()
    {
        var (rig, shell, ops, guard, session) = Setup();
        using var _ = rig;
        Default(rig, ops, shell, session, "d1");
        Work(shell);
        rig.Store.SaveReview(session, "d1", Verdicts.Change, "use B");
        Assert.That(guard.Merge(new MergeTarget("work", "main", null)), Is.EqualTo(1));
    }

    [Test]
    public void A_decision_of_the_user_does_not_block_the_merge()
    {
        var (rig, shell, ops, guard, session) = Setup();
        using var _ = rig;
        rig.Decide(session, "d1", "high", null);
        rig.Store.SaveAnswer(session, "d1", new Answer { Value = "B", By = Parties.User });
        ops.Start("d1");
        Commit(shell, "d1.txt", "B");
        Assert.That(guard.Merge(new MergeTarget("orch/decision-d1", "main", null)), Is.EqualTo(0));
    }

    [Test]
    public void A_brief_with_allow_defaults_permits_the_merge_for_its_children()
    {
        var (rig, shell, ops, guard, root) = Setup(allowDefaults: true);
        using var _ = rig;
        var child = rig.Create("child", parent: root);
        Default(rig, ops, shell, child, "d1");
        Work(shell);
        Assert.That(guard.Merge(new MergeTarget("work", "main", null)), Is.EqualTo(0));
    }

    [Test]
    public void A_default_that_is_already_in_the_base_does_not_block()
    {
        var (rig, shell, ops, guard, session) = Setup();
        using var _ = rig;
        Default(rig, ops, shell, session, "d1");
        shell.Checkout("main");
        shell.MustGit("merge", "--quiet", "--no-ff", "-m", "merged by the user", "orch/decision-d1");
        Work(shell);
        Assert.That(guard.Carried(new MergeTarget("work", "main", null), remote: false), Is.Empty);
    }
}
