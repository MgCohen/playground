using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Lifecycle;
using Orchestrator.Core.Policy;

namespace Orchestrator.Tests;

/// <summary>The guards of the state machine: pure functions of a snapshot, with no IO.</summary>
public sealed class LifecycleMachineTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    static Snapshot S() => new(Now) { Started = true, Holder = Holders.None };

    static StateFile In(string state) => new(state, Times.Format(Now - TimeSpan.FromMinutes(1)));

    static StepResult Step(string state, Snapshot s) => Machine.Step(In(state), s);

    /// <summary>
    /// Sample snapshots for the overlap test: a fixed random set over the facts that the guards read.
    /// </summary>
    static IEnumerable<Snapshot> Samples()
    {
        var random = new Random(17);
        T Pick<T>(params T[] values) => values[random.Next(values.Length)];
        TimeSpan? Span(params double[] seconds) => Pick(seconds.Select(v => v < 0 ? (TimeSpan?)null : TimeSpan.FromSeconds(v)).ToArray());
        for (var i = 0; i < 20000; i++)
            yield return new Snapshot(Now)
            {
                Enabled = Pick(true, true, true, false),
                Control = Pick(ControlStates.Active, ControlStates.Active, ControlStates.Stopped, ControlStates.Archived),
                Started = Pick(true, false),
                Running = Pick(true, false),
                SupervisorTurn = Pick(true, false),
                Holder = Pick(Holders.Live, Holders.None, Holders.Supervisor),
                BeatAge = Span(-1, 2, 30, 200),
                ProcessAlive = Pick<bool?>(null, true, false),
                FailedTurns = Pick(false, false, true),
                StopLatch = Pick<string?>(null, null, "latch"),
                InputAge = Span(-1, -1, 10, 300),
                SinceEvent = Span(-1, 60, 3600),
                IdleFor = Span(-1, 60, 1200),
                Report = Pick<string?>(null, ReportStates.Working, ReportStates.Blocked, ReportStates.Done),
                ReportAge = Span(-1, 60, 7200),
                InputAfterReport = Pick(true, false),
                InputAfterTurn = Pick(true, false),
                WaitReason = Pick(WaitReasons.None, WaitReasons.None, WaitReasons.Decision, WaitReasons.Child),
                OpenDecisions = Pick(0, 0, 1),
                OpenPrs = Pick(0, 0, 1),
                InApp = Pick(true, false),
                AppArchived = Pick(false, false, true),
                AutoArchive = Pick(AutoArchive.Default, new AutoArchive(true, 30)),
            };
    }

    [Test]
    public void No_two_transitions_that_leave_one_state_with_the_same_priority_say_yes_on_one_snapshot()
    {
        var samples = Samples().ToList();
        foreach (var state in States.All)
        {
            var transitions = Machine.From(state).ToList();
            foreach (var a in transitions)
                foreach (var b in transitions.Where(b => !ReferenceEquals(a, b) && b.Priority == a.Priority))
                {
                    var both = samples.FirstOrDefault(s => a.Guard(s) && b.Guard(s));
                    Assert.That(both, Is.Null, $"{state}: {a} and {b} have priority {a.Priority} and both say yes");
                }
        }
    }

    [Test]
    public void Each_state_has_unique_priorities_and_each_state_except_archived_has_a_way_out()
    {
        foreach (var state in States.All)
        {
            var priorities = Machine.From(state).Select(t => t.Priority).ToList();
            Assert.That(priorities, Is.Unique, state);
            if (state != States.Archived) Assert.That(priorities, Is.Not.Empty, $"{state} has no local transition");
        }
        Assert.That(Machine.From(States.Archived), Is.Empty);
        Assert.That(Machine.Globals.Select(g => g.Priority), Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void The_step_never_takes_a_transition_that_is_not_in_the_tables()
    {
        var all = Machine.Globals.Concat(Machine.Locals).ToHashSet();
        foreach (var s in Samples().Take(3000))
            foreach (var state in States.All)
            {
                var r = Step(state, s);
                if (r.Taken is { } taken) Assert.That(all, Does.Contain(taken));
                if (r.Changed && r.File.State != state)
                    Assert.That(r.Taken!.From is var from && (from == state || from == Machine.Any), $"{state} → {r.File.State} by {r.Taken}");
            }
    }

    [Test]
    public void A_new_session_with_no_state_file_is_start_and_goes_to_running_after_its_first_turn()
    {
        var first = Machine.Step(null, S() with { Started = false });
        Assert.That(first.File.State, Is.EqualTo(States.Start));
        Assert.That(first.File.Guard, Is.EqualTo("detect"));
        Assert.That(Machine.Step(first.File, S() with { Started = true }).File.State, Is.EqualTo(States.Running));
    }

    [Test]
    public void The_checks_detect_the_state_in_the_fixed_order()
    {
        Assert.That(Machine.Detect(S() with { Enabled = false, Control = ControlStates.Stopped }), Is.EqualTo(States.Archived));
        Assert.That(Machine.Detect(S() with { Control = ControlStates.Stopped, Holder = Holders.Live }), Is.EqualTo(States.Stopped));
        Assert.That(Machine.Detect(S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromMinutes(1) }), Is.EqualTo(States.Stale));
        Assert.That(Machine.Detect(S() with { FailedTurns = true }), Is.EqualTo(States.NotResponding));
        Assert.That(Machine.Detect(S() with { Running = true }), Is.EqualTo(States.Running));
        Assert.That(Machine.Detect(S() with { Report = ReportStates.Done }), Is.EqualTo(States.Done));
        Assert.That(Machine.Detect(S() with { IdleFor = TimeSpan.FromMinutes(20) }), Is.EqualTo(States.Forgotten));
        Assert.That(Machine.Detect(S()), Is.EqualTo(States.Idle));
    }

    [Test]
    public void A_turn_end_with_a_done_report_goes_to_done_and_without_one_to_idle()
    {
        Assert.That(Step(States.Running, S() with { Report = ReportStates.Done }).File.State, Is.EqualTo(States.Done));
        Assert.That(Step(States.Running, S() with { Report = ReportStates.Working }).File.State, Is.EqualTo(States.Idle));
        Assert.That(Step(States.Running, S() with { Report = ReportStates.Done, InputAfterReport = true }).File.State, Is.EqualTo(States.Idle));
        Assert.That(Step(States.Running, S() with { Running = true }).Changed, Is.False);
    }

    [Test]
    public void An_idle_session_with_no_final_report_is_forgotten_after_the_wait_unless_it_waits_for_something()
    {
        var idle = S() with { IdleFor = TimeSpan.FromMinutes(16), Report = ReportStates.Working };
        Assert.That(Step(States.Idle, idle).File.State, Is.EqualTo(States.Forgotten));
        Assert.That(Step(States.Idle, idle with { IdleFor = TimeSpan.FromMinutes(14) }).Changed, Is.False);
        Assert.That(Step(States.Idle, idle with { WaitReason = WaitReasons.Decision }).Changed, Is.False);
        Assert.That(Step(States.Idle, idle with { WaitReason = WaitReasons.Child }).Changed, Is.False);
        Assert.That(Step(States.Idle, idle with { Report = ReportStates.Blocked }).Changed, Is.False);
        Assert.That(Step(States.Forgotten, idle with { Running = true, IdleFor = null }).File.State, Is.EqualTo(States.Running));
    }

    [Test]
    public void The_forgotten_timer_comes_from_the_policy()
    {
        var idle = S() with { IdleFor = TimeSpan.FromMinutes(2), Timers = new LifecycleTimers(ForgottenMinutes: 1) };
        Assert.That(Step(States.Idle, idle).File.State, Is.EqualTo(States.Forgotten));
    }

    [Test]
    public void A_live_lease_with_an_old_heartbeat_is_stale_from_running_idle_and_done()
    {
        var stale = S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromSeconds(20) };
        foreach (var state in new[] { States.Running, States.Idle, States.Done, States.Forgotten, States.Start, States.NotResponding })
            Assert.That(Step(state, stale).File.State, Is.EqualTo(States.Stale), state);
        Assert.That(Step(States.Idle, stale with { BeatAge = TimeSpan.FromSeconds(5) }).Changed, Is.False);
    }

    [Test]
    public void A_stale_session_goes_to_idle_when_its_process_is_gone_and_to_not_responding_when_it_hangs()
    {
        var stale = S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromSeconds(30) };
        var gone = Step(States.Stale, stale with { ProcessAlive = false });
        Assert.That((gone.File.State, gone.Taken?.Name), Is.EqualTo((States.Idle, "process-gone")));
        Assert.That(Step(States.Stale, stale with { ProcessAlive = null }).Taken?.Name, Is.EqualTo("process-gone"), "a lease with no process id keeps the rule of before B2");
        Assert.That(Step(States.Stale, stale with { ProcessAlive = true }).Changed, Is.False, "alive, no beat for 30 s: wait");
        var hangs = Step(States.Stale, stale with { ProcessAlive = true, BeatAge = TimeSpan.FromMinutes(3) });
        Assert.That((hangs.File.State, hangs.File.Fault), Is.EqualTo((States.NotResponding, Faults.HungProcess)));
        var back = Step(States.Stale, stale with { ProcessAlive = true, BeatAge = TimeSpan.FromSeconds(1) });
        Assert.That((back.File.State, back.Taken?.Name), Is.EqualTo((States.Idle, "beat")));
    }

    [Test]
    public void Each_fault_gives_not_responding_and_the_session_leaves_it_when_the_fault_is_gone()
    {
        var live = S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromSeconds(1) };
        Assert.That(Step(States.Idle, S() with { FailedTurns = true }).File.Fault, Is.EqualTo(Faults.FailedTurns));
        Assert.That(Step(States.Idle, live with { StopLatch = "latch" }).File.Fault, Is.EqualTo(Faults.StopLatch));
        Assert.That(Step(States.Idle, live with { InputAge = TimeSpan.FromMinutes(3) }).File.Fault, Is.EqualTo(Faults.InputNotTaken));
        Assert.That(Step(States.Idle, live with { InputAge = TimeSpan.FromSeconds(30) }).Changed, Is.False);
        Assert.That(Step(States.Running, S() with { Running = true, SinceEvent = TimeSpan.FromMinutes(31) }).File.Fault, Is.EqualTo(Faults.HungTurn));
        Assert.That(Step(States.NotResponding, S()).File.State, Is.EqualTo(States.Idle));
        Assert.That(Step(States.NotResponding, S() with { Running = true }).File.State, Is.EqualTo(States.Running));
        Assert.That(Step(States.NotResponding, S() with { FailedTurns = true }).Changed, Is.False);
    }

    [Test]
    public void A_stop_is_requested_and_commits_when_the_supervisor_turn_ended()
    {
        var stop = S() with { Control = ControlStates.Stopped, Running = true, SupervisorTurn = true };
        var requested = Step(States.Running, stop);
        Assert.That((requested.File.State, requested.Requested, requested.File.Requested?.To), Is.EqualTo((States.Running, true, States.Stopped)));
        Assert.That(Machine.Step(requested.File, stop).Changed, Is.False, "the state stays while the request waits");
        var committed = Machine.Step(requested.File, stop with { Running = false, SupervisorTurn = false });
        Assert.That((committed.File.State, committed.File.Requested), Is.EqualTo((States.Stopped, (Request?)null)));
        Assert.That(Step(States.Idle, stop with { Running = false, SupervisorTurn = false }).File.State, Is.EqualTo(States.Stopped), "no turn: the request commits at once");
    }

    [Test]
    public void A_stop_request_escalates_at_its_deadline_and_the_state_stays()
    {
        var stop = S() with { Control = ControlStates.Stopped, Running = true, SupervisorTurn = true };
        var requested = Step(States.Running, stop).File;
        Assert.That(Machine.Step(requested, stop with { Now = Now + TimeSpan.FromSeconds(29) }).Escalated, Is.EqualTo(0));
        var later = Machine.Step(requested, stop with { Now = Now + TimeSpan.FromSeconds(31) });
        Assert.That((later.File.State, later.Escalated), Is.EqualTo((States.Running, 1)));
        Assert.That(Machine.Step(later.File, stop with { Now = Now + TimeSpan.FromSeconds(40) }).Escalated, Is.EqualTo(0), "one escalation only");
        Assert.That(Machine.Step(requested, S() with { Running = true }).Cancelled, Is.True, "a resume before the commit cancels the stop");
    }

    [Test]
    public void Stopped_and_archived_ignore_the_weaker_global_transitions()
    {
        var all = S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromMinutes(5), FailedTurns = true, Control = ControlStates.Stopped };
        Assert.That(Step(States.Stopped, all).Changed, Is.False);
        Assert.That(Step(States.Stopped, all with { Control = ControlStates.Active }).File.State, Is.EqualTo(States.Idle));
        Assert.That(Step(States.Archived, all with { Control = ControlStates.Active, Enabled = true }).Changed, Is.False);
        Assert.That(Step(States.Stale, S() with { Holder = Holders.Live, BeatAge = TimeSpan.FromSeconds(30), ProcessAlive = true, FailedTurns = true }).Changed, Is.False, "stale is stronger than a fault");
    }

    [Test]
    public void An_archive_of_a_session_in_the_app_waits_for_the_app_and_commits_when_the_app_archived_it()
    {
        var archive = S() with { Control = ControlStates.Archived, Enabled = false, InApp = true };
        var requested = Step(States.Done, archive);
        Assert.That(requested.File.Requested?.WaitsFor, Is.EqualTo(new[] { "enabled gone", "app archived" }));
        Assert.That(Machine.Step(requested.File, archive with { AppArchived = true }).File.State, Is.EqualTo(States.Archived));
        Assert.That(Step(States.Done, archive with { InApp = false }).File.State, Is.EqualTo(States.Archived), "not in the app: the store side commits");
        var level1 = Machine.Step(requested.File, new Snapshot(Now + TimeSpan.FromSeconds(121)) { Control = ControlStates.Archived, Enabled = false, InApp = true });
        Assert.That(level1.Escalated, Is.EqualTo(1));
        var level2 = Machine.Step(level1.File, new Snapshot(Now + TimeSpan.FromMinutes(11)) { Control = ControlStates.Archived, Enabled = false, InApp = true });
        Assert.That(level2.Escalated, Is.EqualTo(2));
    }

    [Test]
    public void A_sidebar_archive_requests_the_archive_of_the_store_side()
    {
        var r = Step(States.Idle, S() with { InApp = true, AppArchived = true });
        Assert.That((r.Requested, r.Taken?.Name, r.File.Requested?.WaitsFor), Is.EqualTo((true, "archive", new[] { "enabled gone", "app archived" })));
    }

    [Test]
    public void Auto_archive_is_off_by_default_and_a_new_input_cancels_its_request()
    {
        var done = S() with { Report = ReportStates.Done, ReportAge = TimeSpan.FromHours(2) };
        Assert.That(Step(States.Done, done).Changed || Step(States.Done, done).Requested, Is.False);
        var on = done with { AutoArchive = new AutoArchive(true, 60) };
        var requested = Step(States.Done, on);
        Assert.That((requested.Requested, requested.File.Requested?.Transition), Is.EqualTo((true, "auto-archive")));
        Assert.That(Step(States.Done, on with { OpenPrs = 1 }).Requested, Is.False);
        var cancelled = Machine.Step(requested.File, on with { InputAge = TimeSpan.FromSeconds(1) });
        Assert.That((cancelled.Cancelled, cancelled.File.Requested), Is.EqualTo((true, (Request?)null)));
    }

    [Test]
    public void A_transition_that_runs_more_than_three_times_in_one_hour_is_a_loop()
    {
        var history = Enumerable.Range(1, 3).Select(i => new StateStep("forgotten", Times.Format(Now - TimeSpan.FromMinutes(10 * i)))).ToList();
        var file = In(States.Idle) with { History = history };
        var r = Machine.Step(file, S() with { IdleFor = TimeSpan.FromMinutes(16) });
        Assert.That((r.File.State, r.File.Fault), Is.EqualTo((States.NotResponding, Faults.Loop)));
        Assert.That(Machine.Step(r.File, S()).Changed, Is.False, "the loop holds while the window has the steps");
        var old = file with { History = [.. history.Select(h => h with { At = Times.Format(Now - TimeSpan.FromHours(2)) })] };
        Assert.That(Machine.Step(old, S() with { IdleFor = TimeSpan.FromMinutes(16) }).File.State, Is.EqualTo(States.Forgotten));
    }

    [Test]
    public void The_main_path_does_not_count_for_the_loop_rule()
    {
        var file = In(States.Idle);
        for (var i = 0; i < 10; i++)
        {
            file = Machine.Step(file, S() with { Running = true }).File;
            file = Machine.Step(file, S()).File;
        }
        Assert.That(file.State, Is.EqualTo(States.Idle));
        Assert.That(file.History, Is.Empty);
    }

    [Test]
    public void An_unknown_state_name_needs_the_fallback()
    {
        Assert.That(Machine.Step(In("sleeping"), S()).NeedsFallback, Is.True);
        var inferred = Machine.Inferred(null, States.Idle, "no turn", Now);
        Assert.That((inferred.State, inferred.Inferred), Is.EqualTo((States.Idle, true)));
        Assert.That(Machine.Step(inferred, S() with { Running = true }).File.Inferred, Is.False, "a guard replaces the inferred state");
    }
}
