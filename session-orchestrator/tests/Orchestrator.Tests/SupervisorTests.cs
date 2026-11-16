using Orchestrator.Core.Contract;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

public sealed class SupervisorTests
{
    [Test]
    public async Task A_new_session_starts_with_its_brief_as_a_new_session()
    {
        using var t = new Rig();
        var id = t.Create("Do the work.", "root");
        await t.Cycle();
        Assert.That(t.Turns, Is.EqualTo(new[] { new TurnRequest(id, "C:/x", "Do the work.", true) { Title = "root" } }));
        Assert.That(t.Store.Meta(id).Started, Is.True);
        Assert.That(t.Store.Pending(id).Text, Is.Empty);
    }

    [Test]
    public async Task The_next_turn_is_a_resume_with_all_inbox_files_in_order()
    {
        using var t = new Rig();
        var id = t.Create("one", "root");
        await t.Cycle();
        Files.Write(t.Store.PathOf(id, StoreFiles.Inbox, "10.txt"), "three");
        Files.Write(t.Store.PathOf(id, StoreFiles.Inbox, "2.txt"), "two");
        await t.Cycle();
        Assert.That(t.Turns[1].IsNew, Is.False);
        Assert.That(t.Turns[1].Prompt, Is.EqualTo("two\n\n---\n\nthree"));
    }

    [Test]
    public async Task A_live_session_gets_no_supervisor_turn()
    {
        using var t = new Rig();
        var id = t.Create("hello");
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.Zero);
        await t.Cycle();
        Assert.That(t.Turns, Is.Empty);
    }

    [Test]
    public async Task A_live_lease_with_a_stale_heartbeat_is_released_and_the_session_runs()
    {
        using var t = new Rig();
        var id = t.Create("hello");
        t.MakeLive(id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        await t.Cycle();
        Assert.That(t.Store.Lease(id).Holder, Is.EqualTo(Holders.None));
        Assert.That(t.Turns, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_new_live_lease_with_an_old_heartbeat_counts_as_live()
    {
        using var t = new Rig();
        var id = t.Create("hello");
        t.MakeLive(id, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        await t.Cycle();
        Assert.That(t.Store.Lease(id).Holder, Is.EqualTo(Holders.Live));
        Assert.That(t.Turns, Is.Empty);
    }

    [Test]
    public async Task An_answer_to_a_pending_decision_is_sent_once_and_marked_delivered()
    {
        using var t = new Rig();
        var id = await t.Started();
        t.Store.SaveDecision(new Decision("d1", id) { Question = "Pick", Risk = "low" });
        Assert.That(t.Store.OpenDecisions(id), Has.Count.EqualTo(1));
        t.Store.SaveAnswer(id, "d1", new Answer { Value = "LIME" });
        await t.Cycle();
        await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(2));
        Assert.That(t.Turns[1].Prompt, Is.EqualTo("Answer to decision d1 (Pick): LIME"));
        Assert.That(t.Store.IsDelivered(id, "d1"), Is.True);
    }

    [Test]
    public async Task A_done_report_of_a_child_goes_to_the_parent_inbox_once()
    {
        using var t = new Rig();
        var parent = t.Create("p", "parent");
        var child = t.Create("c", "child", parent, "spawn_child");
        await t.Cycle();
        t.Store.WriteJson(child, new Report(ReportStates.Done, "All good.", "t1"), StoreFiles.Report);
        await t.Cycle();
        await t.Cycle();
        var toParent = t.Turns.Where(turn => turn.Id == parent).Select(turn => turn.Prompt);
        Assert.That(toParent, Is.EqualTo(new[] { "p", $"Report from child \"child\" ({child}): done. All good." }));
    }

    [Test]
    public async Task An_open_question_of_a_child_goes_to_the_parent_inbox_once()
    {
        using var t = new Rig();
        var parent = t.Create("p", "parent");
        var child = t.Create("c", "child", parent, "spawn_child");
        await t.Cycle();
        t.Decide(child, "d1", "low", "A");
        await t.Cycle();
        await t.Cycle();
        var toParent = t.Turns.Where(turn => turn.Id == parent).Select(turn => turn.Prompt).ToList();
        Assert.That(toParent, Has.Count.EqualTo(2));
        Assert.That(toParent[1], Does.StartWith($"Question from child \"child\" ({child}), decision d1: Q d1"));
        Assert.That(toParent[1], Does.Contain("Options: A; B"));
        Assert.That(toParent[1], Does.Contain("If nobody answers in 60 s, the policy answers \"A\"."));
        Assert.That(toParent[1], Does.Contain("mcp__orch__answer"));
    }

    [Test]
    public async Task A_closed_question_sends_one_close_notice_to_the_parent()
    {
        using var t = new Rig();
        var parent = t.Create("p", "parent");
        var child = t.Create("c", "child", parent, "spawn_child");
        await t.Cycle();
        t.Decide(child, "d1", "high", null);
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromSeconds(1));
        new Messages(t.Store).Answer(child, "d1", "B");
        await t.Cycle();
        await t.Cycle();
        var toParent = string.Join(" | ", t.Turns.Where(turn => turn.Id == parent).Select(turn => turn.Prompt));
        var closed = $"Question closed: child \"child\" ({child}), decision d1: Q d1 Answer: B (from user).";
        Assert.That(toParent.Split(closed).Length - 1, Is.EqualTo(1));
    }

    [Test]
    public async Task An_open_question_with_no_default_gets_reminders_at_the_set_times()
    {
        using var t = new Rig();
        var parent = t.Create("p", "parent");
        var child = t.Create("c", "child", parent, "spawn_child");
        await t.Cycle();
        t.Decide(child, "d1", "high", null);
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(9));
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(2));
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(30));
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(31));
        await t.Cycle();
        var reminders = t.Turns.Where(turn => turn.Id == parent && turn.Prompt.StartsWith("Reminder:")).Select(turn => turn.Prompt).ToList();
        Assert.That(reminders, Has.Count.EqualTo(2));
        Assert.That(reminders[0], Does.Contain("for 11 min"));
        Assert.That(reminders[1], Does.Contain("for 1.2 h"));
    }

    [Test]
    public async Task A_question_that_the_policy_answers_later_gets_no_reminder()
    {
        using var t = new Rig();
        var parent = t.Create("p", "parent");
        var child = t.Create("c", "child", parent, "spawn_child");
        await t.Cycle();
        t.Decide(child, "d1", "medium", "A");
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(9.5));
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromMinutes(1));
        await t.Cycle();
        Assert.That(t.Turns.Any(turn => turn.Prompt.StartsWith("Reminder:")), Is.False);
    }

    [Test]
    public async Task A_question_of_a_root_session_is_not_forwarded()
    {
        using var t = new Rig();
        var root = t.Create("r", "root");
        await t.Cycle();
        t.Decide(root, "d1", "high", null);
        await t.Cycle();
        Assert.That(t.Turns.Count(turn => turn.Id == root), Is.EqualTo(1));
    }

    [Test]
    public async Task A_failed_turn_keeps_the_inbox_and_stops_after_the_failure_limit()
    {
        using var t = new Rig(ok: false);
        var id = t.Create("hello");
        for (var i = 0; i < 4; i++) await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(2));
        Assert.That(t.Store.Pending(id).Text, Is.EqualTo("hello"));
        Assert.That(t.Turns.All(turn => turn.IsNew), Is.True);
        Assert.That(t.Store.Blocked(id)?.Reason, Does.Contain("failed 2 times"));
    }

    [Test]
    public async Task No_more_than_MaxRuns_turns_run_at_one_time()
    {
        using var t = new Rig();
        t.Runner.Gate = new TaskCompletionSource();
        for (var i = 0; i < 3; i++) t.Create($"s{i}");
        t.Supervisor.Tick();
        Assert.That(t.Supervisor.Running, Is.EqualTo(2));
        t.Runner.Gate.SetResult();
        await t.Supervisor.Idle();
        await t.Cycle();
        Assert.That(t.Turns, Has.Count.EqualTo(3));
    }
}
