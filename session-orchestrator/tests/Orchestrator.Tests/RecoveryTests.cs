using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

public sealed class RecoveryTests
{
    static (Rig Rig, string Id) Crashed(bool promptArrived)
    {
        var t = new Rig();
        var id = t.Create("first");
        t.Runner.Gate = new TaskCompletionSource();
        t.Supervisor.Tick();
        SpinWait.SpinUntil(() => t.Turns.Count == 1, TimeSpan.FromSeconds(10));
        var marker = t.Store.ReadJson<TurnMarker>(id, StoreFiles.Turn)!;
        if (promptArrived)
            t.Store.Events.Append(EventKinds.SessionInput, Parties.Supervisor, Parties.Session(id), new Dictionary<string, object?> { ["text"] = "first" }, new Dictionary<string, object?> { ["turnStart"] = marker.Event }, id);
        return (t, id);
    }

    static Supervisor.Supervisor Restart(Rig t, FakeRunner runner) =>
        new(new FileStore(t.Folder, t.Clock), runner, t.Docs, new SupervisorOptions(2, 2, 2, Rig.TestPolicy));

    [Test]
    public async Task A_turn_whose_prompt_arrived_before_the_crash_is_not_sent_again()
    {
        var (t, id) = Crashed(promptArrived: true);
        using var _ = t;
        var runner = new FakeRunner(true);
        var restarted = Restart(t, runner);
        restarted.Tick();
        await restarted.Idle();
        Assert.That(runner.Turns, Is.Empty);
        Assert.That(t.Store.Pending(id).IsEmpty, Is.True);
        Assert.That(t.Store.ReadJson<TurnMarker>(id, StoreFiles.Turn)!.Ended, Is.Not.Null);
        Assert.That(t.Store.Events.All().Any(e => e.Kind == EventKinds.TurnEnd), Is.True);
        t.Runner.Gate!.SetResult();
    }

    [Test]
    public async Task A_turn_whose_prompt_did_not_arrive_is_sent_again_as_a_new_session()
    {
        var (t, id) = Crashed(promptArrived: false);
        using var _ = t;
        var runner = new FakeRunner(true);
        var restarted = Restart(t, runner);
        restarted.Tick();
        await restarted.Idle();
        Assert.That(runner.Turns.Select(turn => (turn.Prompt, turn.IsNew)), Is.EqualTo(new[] { ("first", true) }));
        t.Runner.Gate!.SetResult();
    }

    [Test]
    public async Task The_supervisor_waits_while_the_old_claude_process_still_beats()
    {
        var (t, id) = Crashed(promptArrived: false);
        using var _ = t;
        t.Store.SaveLease(id, new Lease(Holders.Supervisor, Times.Format(t.Clock.Now)));
        Files.Write(t.Store.PathOf(id, StoreFiles.Heartbeat), Times.Format(t.Clock.Now));
        var runner = new FakeRunner(true);
        var restarted = Restart(t, runner);
        restarted.Tick();
        await restarted.Idle();
        Assert.That(runner.Turns, Is.Empty);
        Assert.That(t.Store.ReadJson<TurnMarker>(id, StoreFiles.Turn)!.Ended, Is.Null);
        t.Clock.Advance(TimeSpan.FromSeconds(20));
        restarted.Tick();
        await restarted.Idle();
        Assert.That(runner.Turns, Has.Count.EqualTo(1));
        t.Runner.Gate!.SetResult();
    }

    [Test]
    public async Task A_finished_turn_leaves_an_ended_marker()
    {
        using var t = new Rig();
        var id = await t.Started();
        Assert.That(t.Store.ReadJson<TurnMarker>(id, StoreFiles.Turn)!.Ended, Is.Not.Null);
    }

    [Test]
    public void The_store_version_is_written_once_and_a_newer_store_is_refused()
    {
        using var t = new Rig();
        Assert.That(Home.CheckVersion(t.Folder, t.Clock.Now).Version, Is.EqualTo(StoreVersion.Current));
        Files.WriteJson(Path.Combine(t.Folder, StoreFiles.Version), new StoreVersion(StoreVersion.Current + 1, "x"));
        Assert.Throws<InvalidDataException>(() => Home.CheckVersion(t.Folder, t.Clock.Now));
    }

    [Test]
    public void The_cursor_never_moves_back()
    {
        using var t = new Rig();
        var subscription = t.Store.Events.Subscribe(Parties.Supervisor, "test", _ => true);
        subscription.Done("5000-aaaaaaaa-cli");
        subscription.Done("3000-bbbbbbbb-cli");
        Assert.That(subscription.Cursor, Is.EqualTo("5000-aaaaaaaa-cli"));
    }
}
