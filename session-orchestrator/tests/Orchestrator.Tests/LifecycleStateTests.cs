using System.Diagnostics;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Lifecycle;
using Orchestrator.Core.Policy;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>The supervisor runs the state machine at each tick: state.json, session.state, and the action of each state.</summary>
public sealed class LifecycleStateTests
{
    static string? State(Rig t, string id) => new StateReader(t.Store).State(id).File?.State;

    static List<string?> Changes(Rig t, string id) =>
        [.. t.Store.Events.All().Where(e => e.Kind == EventKinds.SessionState && e.Session == id).Select(e => t.Store.Events.Fields(e).GetValueOrDefault("to") as string)];

    static List<string?> NoticesTo(Rig t, string id) =>
        [.. t.Store.Events.All().Where(e => e.Kind == EventKinds.Notice && e.To == Parties.Session(id)).Select(e => t.Store.Events.Fields(e).GetValueOrDefault("kind") as string)];

    static List<string> MessagesToUser(Rig t, string id) =>
        [.. t.Store.Events.All().Where(e => e.Kind == EventKinds.Message && e.To == Parties.User && e.Session == id && e.From == Parties.Supervisor).Select(e => t.Store.Events.Fields(e).GetValueOrDefault("text") as string ?? "")];

    static async Task Cycles(Rig t, int count)
    {
        for (var i = 0; i < count; i++) await t.Cycle();
    }

    [Test]
    public async Task A_new_session_goes_start_running_idle_and_each_change_is_a_session_state_event()
    {
        using var t = new Rig();
        t.Runner.Gate = new TaskCompletionSource();
        var id = t.Create("work");
        t.Supervisor.Tick();
        Assert.That(State(t, id), Is.EqualTo(States.Start));
        t.Supervisor.Tick();
        Assert.That(State(t, id), Is.EqualTo(States.Running));
        t.Runner.Gate.SetResult();
        await t.Supervisor.Idle();
        await t.Cycle();
        Assert.That(State(t, id), Is.EqualTo(States.Idle));
        Assert.That(Changes(t, id), Is.EqualTo(new[] { States.Start, States.Running, States.Idle }));
        var e = t.Store.Events.All().Last(e => e.Kind == EventKinds.SessionState);
        Assert.That(t.Store.Events.Fields(e).GetValueOrDefault("guard"), Is.EqualTo("turn-end"));
        Assert.That(t.Store.Events.Fields(e).GetValueOrDefault("from"), Is.EqualTo(States.Running));
    }

    [Test]
    public async Task Two_reports_in_one_turn_give_one_forward_to_the_parent_at_the_end_of_the_turn()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        t.Runner.Gate = new TaskCompletionSource();
        t.Runner.OnRun = r =>
        {
            if (r.Id != child) return;
            t.Store.SaveReport(child, ReportStates.Done, "step 1 is done");
            t.Store.SaveReport(child, ReportStates.Done, "all done");
        };
        t.Supervisor.Tick();
        Assert.That(t.Store.Events.All().Where(e => e.IsForward && e.Kind == EventKinds.Report), Is.Empty, "no forward while the turn runs");
        t.Runner.Gate.SetResult();
        await t.Supervisor.Idle();
        t.Runner.OnRun = null;
        await t.Cycle();
        var forwards = t.Store.Events.All().Where(e => e.IsForward && e.Kind == EventKinds.Report && e.To == Parties.Session(parent)).ToList();
        Assert.That(forwards, Has.Count.EqualTo(1));
        Assert.That(t.Store.Events.Fields(forwards[0]).GetValueOrDefault("summary"), Is.EqualTo("all done"));
        var first = t.Store.Events.All().First(e => e.Kind == EventKinds.Report && !e.IsForward);
        Assert.That(t.Store.Events.IsAcked(first.Id), Is.True, "the first report is superseded");
        Assert.That(new Inbox(t.Store).Cards().Select(c => c.Envelope.Id), Does.Not.Contain(first.Id), "the superseded report has no user card");
        await t.Cycle();
        Assert.That(State(t, child), Is.EqualTo(States.Done));
    }

    [Test]
    public async Task A_forgotten_session_gets_one_nudge_then_its_parent_gets_one_notice()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await Cycles(t, 3);
        Assert.That(State(t, child), Is.EqualTo(States.Idle));
        t.Clock.Advance(TimeSpan.FromMinutes(16));
        await t.Cycle();
        Assert.That(Changes(t, child), Does.Contain(States.Forgotten));
        Assert.That(NoticesTo(t, child), Is.EqualTo(new[] { "report-missing" }));
        await Cycles(t, 3);
        Assert.That(t.Turns.Last().Prompt, Does.Contain("no final report"));
        Assert.That(State(t, child), Is.EqualTo(States.Idle));
        t.Clock.Advance(TimeSpan.FromMinutes(16));
        await Cycles(t, 2);
        Assert.That(State(t, child), Is.EqualTo(States.Forgotten));
        Assert.That(NoticesTo(t, child), Is.EqualTo(new[] { "report-missing" }), "one nudge only");
        Assert.That(NoticesTo(t, parent), Does.Contain("child-forgotten"));
        await Cycles(t, 2);
        Assert.That(NoticesTo(t, parent).Count(k => k == "child-forgotten"), Is.EqualTo(1));
    }

    [Test]
    public async Task The_nudge_turn_goes_forgotten_to_running_and_a_new_input_starts_a_new_forgotten_episode()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await Cycles(t, 3);
        t.Clock.Advance(TimeSpan.FromMinutes(16));
        t.Runner.Gate = new TaskCompletionSource();
        t.Supervisor.Tick();
        t.Supervisor.Tick();
        t.Runner.Gate.SetResult();
        t.Runner.Gate = null;
        await t.Supervisor.Idle();
        await Cycles(t, 2);
        var guards = t.Store.Events.All().Where(e => e.Kind == EventKinds.SessionState && e.Session == child).Select(e => t.Store.Events.Fields(e).GetValueOrDefault("guard") as string).ToList();
        Assert.That(guards, Does.Contain("nudge-turn"), "review of PR #14, item 3");
        Assert.That(guards, Does.Not.Contain("remembered"));
        t.Clock.Advance(TimeSpan.FromMinutes(16));
        await Cycles(t, 2);
        Assert.That(NoticesTo(t, parent).Count(k => k == "child-forgotten"), Is.EqualTo(1));
        t.Store.Send(child, "one more task", Parties.Session(parent));
        await Cycles(t, 3);
        t.Clock.Advance(TimeSpan.FromMinutes(16));
        await Cycles(t, 2);
        Assert.That(NoticesTo(t, child).Count(k => k == "report-missing"), Is.EqualTo(2), "review of PR #14, item 4: a new input gives a new nudge");
    }

    [Test]
    public async Task A_done_session_is_not_forgotten()
    {
        using var t = new Rig();
        var id = t.Create("work");
        t.Runner.OnRun = _ => t.Store.SaveReport(id, ReportStates.Done, "done");
        await Cycles(t, 3);
        t.Runner.OnRun = null;
        t.Clock.Advance(TimeSpan.FromMinutes(30));
        await Cycles(t, 2);
        Assert.That(State(t, id), Is.EqualTo(States.Done));
        Assert.That(NoticesTo(t, id), Is.Empty);
    }

    [Test]
    public async Task A_stale_lease_whose_process_is_alive_is_kept_and_becomes_not_responding()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("hello", "child", parent);
        using var self = Process.GetCurrentProcess();
        t.Store.SaveLease(child, new Lease(Holders.Live, Times.Format(t.Clock.Now - TimeSpan.FromSeconds(20))) { Pid = self.Id, Started = Times.Format(self.StartTime.ToUniversalTime()) });
        Files.Write(t.Store.PathOf(child, StoreFiles.Heartbeat), Times.Format(t.Clock.Now - TimeSpan.FromSeconds(20)));
        await Cycles(t, 2);
        Assert.That(State(t, child), Is.EqualTo(States.Stale));
        Assert.That(t.Store.Lease(child).Holder, Is.EqualTo(Holders.Live), "a live process keeps its lease: no second writer");
        Assert.That(t.Turns.Where(r => r.Id == child), Is.Empty);
        t.Clock.Advance(TimeSpan.FromMinutes(2));
        await t.Cycle();
        Assert.That(State(t, child), Is.EqualTo(States.NotResponding));
        Assert.That(NoticesTo(t, parent), Does.Contain("child-not-responding"));
        Assert.That(t.Turns.Where(r => r.Id == child), Is.Empty);
    }

    [Test]
    public async Task A_stale_lease_whose_process_is_gone_is_released_and_the_supervisor_delivers()
    {
        using var t = new Rig();
        var id = t.Create("hello");
        var ended = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        ended.WaitForExit();
        t.Store.SaveLease(id, new Lease(Holders.Live, Times.Format(t.Clock.Now - TimeSpan.FromMinutes(1))) { Pid = ended.Id, Started = Times.Format(DateTime.UtcNow) });
        Files.Write(t.Store.PathOf(id, StoreFiles.Heartbeat), Times.Format(t.Clock.Now - TimeSpan.FromMinutes(1)));
        await Cycles(t, 3);
        Assert.That(Changes(t, id).Take(2), Is.EqualTo(new[] { States.Stale, States.Idle }));
        Assert.That(t.Store.Lease(id).Holder, Is.EqualTo(Holders.None));
        Assert.That(t.Store.Events.All().Count(e => e.Kind == EventKinds.LeaseRelease && e.Session == id), Is.EqualTo(1));
        Assert.That(t.Turns.Select(r => r.Id), Does.Contain(id));
    }

    [Test]
    public async Task Failed_turns_give_not_responding_and_a_root_notice_goes_to_the_user_queue()
    {
        using var t = new Rig(ok: false);
        var id = t.Create("work", "root");
        await Cycles(t, 4);
        Assert.That(State(t, id), Is.EqualTo(States.NotResponding));
        Assert.That(new StateReader(t.Store).State(id).File?.Fault, Is.EqualTo(Faults.FailedTurns));
        var texts = MessagesToUser(t, id);
        Assert.That(texts, Has.Count.EqualTo(1));
        Assert.That(texts[0], Does.Contain("does not respond").And.Contain("failed-turns"));
        Assert.That(new Inbox(t.Store).Cards().Any(c => c.Kind == EventKinds.Message && c.Session == id), Is.True, "a card that needs an ack");
    }

    [Test]
    public async Task A_stop_is_requested_commits_after_the_turn_ends_and_the_parent_hears_stop_and_resume()
    {
        using var t = new Rig();
        var parent = await t.Started();
        t.Runner.Gate = new TaskCompletionSource();
        var child = t.Create("work", "child", parent);
        t.Supervisor.Tick();
        t.Supervisor.Tick();
        new Lifecycle(t.Store).Stop(child, Parties.User);
        t.Supervisor.Tick();
        var file = new StateReader(t.Store).State(child).File!;
        Assert.That(file.Requested?.To ?? file.State, Is.EqualTo(States.Stopped));
        await t.Supervisor.Idle();
        t.Runner.Gate = null;
        await t.Cycle();
        Assert.That(State(t, child), Is.EqualTo(States.Stopped));
        Assert.That(NoticesTo(t, parent), Does.Contain("child-stopped"));
        new Lifecycle(t.Store).Resume(child, Parties.User);
        await Cycles(t, 2);
        Assert.That(NoticesTo(t, parent), Does.Contain("child-resumed"));
        Assert.That(State(t, child), Is.Not.EqualTo(States.Stopped));
    }

    [Test]
    public async Task An_archive_goes_to_archived_and_the_parent_gets_one_notice()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await Cycles(t, 3);
        new Lifecycle(t.Store).Archive(child, Parties.User, force: false);
        await Cycles(t, 2);
        Assert.That(State(t, child), Is.EqualTo(States.Archived));
        Assert.That(NoticesTo(t, parent).Count(k => k == "child-archived"), Is.EqualTo(1));
    }

    [Test]
    public async Task Auto_archive_archives_a_done_child_with_no_open_work_after_the_wait()
    {
        var policy = Rig.TestPolicy with { AutoArchive = new AutoArchive(true, 30) };
        using var t = new Rig(policy: policy);
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        t.Runner.OnRun = r => { if (r.Id == child) t.Store.SaveReport(child, ReportStates.Done, "done"); };
        await Cycles(t, 3);
        t.Runner.OnRun = null;
        Assert.That(State(t, child), Is.EqualTo(States.Done));
        t.Clock.Advance(TimeSpan.FromMinutes(31));
        await Cycles(t, 2);
        Assert.That(State(t, child), Is.EqualTo(States.Archived));
        Assert.That(File.Exists(t.Store.PathOf(child, StoreFiles.Enabled)), Is.False);
        Assert.That(new Lifecycle(t.Store).Control(child).By, Is.EqualTo(Parties.Supervisor));
    }

    [Test]
    public async Task A_done_child_that_is_not_archived_after_one_hour_gets_one_reminder_to_the_parent()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        t.Runner.OnRun = r => { if (r.Id == child) t.Store.SaveReport(child, ReportStates.Done, "done"); };
        await Cycles(t, 3);
        t.Runner.OnRun = null;
        t.Clock.Advance(TimeSpan.FromMinutes(61));
        await Cycles(t, 3);
        Assert.That(NoticesTo(t, parent).Count(k => k == "archive-reminder"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_state_file_that_is_not_valid_goes_to_the_fallback_agent_which_only_names_the_state()
    {
        var checker = new FakeStateChecker(States.Idle);
        using var t = new Rig(checker: checker);
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await Cycles(t, 3);
        Files.Write(t.Store.PathOf(child, StoreFiles.State), "{ \"state\": \"sleeping\" }");
        var lease = t.Store.Lease(child);
        await t.Cycle();
        Assert.That(checker.Requests, Has.Count.EqualTo(1));
        var file = new StateReader(t.Store).State(child).File!;
        Assert.That((file.State, file.Inferred), Is.EqualTo((States.Idle, true)));
        Assert.That(NoticesTo(t, parent), Does.Contain("state-inferred"));
        Assert.That(t.Store.Lease(child), Is.EqualTo(lease), "an inferred state starts no action");
        Assert.That(File.Exists(t.Store.PathOf(child, StoreFiles.StateCheck)), Is.True);
    }

    [Test]
    public async Task An_imported_session_gets_the_remote_control_notice_unless_its_brief_asks_for_a_background_session()
    {
        using var t = new Rig(import: true);
        var id = t.Create("work", "visible");
        var quiet = t.Store.Create(new Meta("") { Title = "quiet", Cwd = "C:/x", CreatedBy = "supervisor", Background = true }, "work");
        await t.Cycle();
        Assert.That(NoticesTo(t, id), Is.EqualTo(new[] { "open-in-app", "remote-control-self" }));
        Assert.That(NoticesTo(t, quiet), Is.EqualTo(new[] { "open-in-app" }), "a background session is in the app too, with Remote Control off");
    }

    [Test]
    public async Task Only_the_supervisor_writes_meta_and_it_applies_the_allowed_meta_updates()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        var stranger = t.Create("work", "stranger");
        Event Update(string from, string session, string field, object value) =>
            t.Store.Events.Append(EventKinds.MetaUpdate, Parties.Session(from), Parties.Supervisor,
                new Dictionary<string, object?> { ["text"] = $"{field} = {value}", ["session"] = session, ["field"] = field, ["value"] = value }, null, from);
        Update(parent, child, "remoteControl", true);
        Update(child, child, "appId", "local_host-1");
        Update(stranger, child, "remoteControl", false);
        Update(parent, child, "appId", "local_wrong");
        Update(child, child, "title", "renamed");
        await t.Cycle();
        var meta = t.Store.Meta(child);
        Assert.That((meta.RemoteControl, meta.AppId, meta.Title), Is.EqualTo(((bool?)true, "local_host-1", "child")));
        Update(child, child, "remoteControl", false);
        await t.Cycle();
        Assert.That(t.Store.Meta(child).RemoteControl, Is.False, "a session may change its own remoteControl");
    }

    [Test]
    public void The_brief_takes_background_and_orch_new_writes_it_in_meta()
    {
        using var t = new Rig();
        var id = new Messages(t.Store).NewSession(new BriefFields { Title = "bg", Goal = "g", Cwd = "C:/x", Background = true }, "test");
        Assert.That(t.Store.Meta(id).Background, Is.True);
        Assert.That(t.Store.Events.Fields(t.Store.Events.All().Single(e => e.Kind == EventKinds.Brief)).GetValueOrDefault("background"), Is.EqualTo(true));
    }

    [Test]
    public void The_policy_reads_the_lifecycle_timers_and_the_auto_archive_flag()
    {
        var policy = Policy.Parse("""
            { "levels": [ { "name": "low", "description": "d", "timeoutSeconds": 1, "checkDocs": false } ],
              "lifecycle": { "forgottenMinutes": 2, "inputSeconds": 30, "hungMinutes": 5 },
              "autoArchive": { "enabled": true, "afterMinutes": 10 } }
            """, "test");
        Assert.That((policy.Lifecycle.ForgottenMinutes, policy.Lifecycle.InputSeconds, policy.Lifecycle.HungMinutes, policy.Lifecycle.StaleSeconds), Is.EqualTo((2.0, 30.0, 5.0, 15.0)));
        Assert.That(policy.AutoArchive, Is.EqualTo(new AutoArchive(true, 10)));
        var none = Policy.Parse("""{ "levels": [ { "name": "low", "description": "d", "timeoutSeconds": 1, "checkDocs": false } ] }""", "test");
        Assert.That((none.Lifecycle, none.AutoArchive), Is.EqualTo((LifecycleTimers.Default, AutoArchive.Default)));
        foreach (var file in new[] { "policy.default.json", "policy.test.json" })
        {
            var parsed = Policy.Parse(File.ReadAllText(Path.Combine(Home.Root(), "config", file)), file);
            Assert.That(parsed.AutoArchive.Enabled, Is.False, file);
        }
    }

    [Test]
    public void Processes_find_the_parent_and_a_reused_process_id_does_not_count_as_alive()
    {
        using var self = Process.GetCurrentProcess();
        var started = Times.Format(self.StartTime.ToUniversalTime());
        Assert.That(Processes.IsAlive(self.Id, started), Is.True);
        Assert.That(Processes.IsAlive(self.Id, Times.Format(self.StartTime.ToUniversalTime() - TimeSpan.FromMinutes(5))), Is.False);
        Assert.That(Processes.ParentOf(self.Id), Is.Not.Null);
    }
}
