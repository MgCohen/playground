using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;

namespace Orchestrator.Core.Lifecycle;

/// <summary>A state with the check that detects it, for a session with no state.json.</summary>
public sealed record StateCheck(string State, Func<Snapshot, bool> Check);

/// <summary>The result of one step of the machine for one session.</summary>
public sealed record StepResult(StateFile File)
{
    /// <summary>The state before the step. Null: the session had no state.json.</summary>
    public string? From { get; init; }

    public bool Changed { get; init; }

    /// <summary>The transition that changed the state, opened the request or committed it.</summary>
    public Transition? Taken { get; init; }

    /// <summary>True: the step opened a new request.</summary>
    public bool Requested { get; init; }

    /// <summary>True: the step cancelled the open request.</summary>
    public bool Cancelled { get; init; }

    /// <summary>The escalation level that the step reached: 1 or 2. 0: none.</summary>
    public int Escalated { get; init; }

    /// <summary>The names of two local transitions with the same priority that both said yes.</summary>
    public IReadOnlyList<string> Conflict { get; init; } = [];

    /// <summary>True: no check matched or two guards conflict. The fallback agent infers the state.</summary>
    public bool NeedsFallback { get; init; }
}

/// <summary>
/// The lifecycle state machine. It has no IO: the supervisor reads a <see cref="Snapshot"/>, calls <see cref="Step"/>,
/// writes state.json and does the actions of the change.
/// </summary>
public static class Machine
{
    public const string Any = "*";

    /// <summary>The fault of the state not responding, or null.</summary>
    public static string? Fault(Snapshot s)
    {
        if (s.FailedTurns) return Faults.FailedTurns;
        if (s.StopLatch is not null) return Faults.StopLatch;
        if (s.Holder == Holders.Live && s.BeatOld && s.ProcessAlive == true && s.BeatAge is { } beat && beat.TotalSeconds >= s.Timers.StaleProcessSeconds)
            return Faults.HungProcess;
        if (s.Holder == Holders.Live && s.BeatFresh && !s.Running && s.InputAge is { } input && input.TotalSeconds >= s.Timers.InputSeconds)
            return Faults.InputNotTaken;
        if (s.Running && s.SinceEvent is { } quiet && quiet.TotalMinutes >= s.Timers.HungMinutes) return Faults.HungTurn;
        return null;
    }

    /// <summary>A live lease with an old heartbeat, but not a hung process: a hung process is not responding, not stale.</summary>
    static bool IsStale(Snapshot s) => s.Holder == Holders.Live && s.BeatOld && Fault(s) != Faults.HungProcess;

    static bool IsDone(Snapshot s) => s.Report == ReportStates.Done && !s.InputAfterReport;

    static bool IsForgotten(Snapshot s) =>
        !s.Running && s.IdleFor is { } idle && idle.TotalMinutes >= s.Timers.ForgottenMinutes
        && s.Report is not (ReportStates.Done or ReportStates.Blocked) && s.WaitReason == WaitReasons.None
        && !s.InputAfterTurn && s.InputAge is null;

    static bool IsAutoArchive(Snapshot s) =>
        s.AutoArchive.Enabled && !s.Running && s.InputAge is null && s.OpenPrs == 0 && s.OpenDecisions == 0
        && s.ReportAge is { } age && age.TotalMinutes >= s.AutoArchive.AfterMinutes;

    /// <summary>The global transitions, in their fixed order. A global applies from each state with a higher rank.</summary>
    public static readonly IReadOnlyList<Transition> Globals =
    [
        new("archive", Any, States.Archived, 1, TransitionKind.Requested, s => s.Control == ControlStates.Archived || !s.Enabled || s.AppArchived) { CountsForLoop = false },
        new("stop", Any, States.Stopped, 2, TransitionKind.Requested, s => s.Control == ControlStates.Stopped) { CountsForLoop = false },
        new("stale", Any, States.Stale, 3, TransitionKind.Immediate, IsStale),
        new("fault", Any, States.NotResponding, 4, TransitionKind.Immediate, s => Fault(s) is not null),
    ];

    /// <summary>The local transitions. Only the transitions that leave the current state are checked.</summary>
    public static readonly IReadOnlyList<Transition> Locals =
    [
        new("first-turn", States.Start, States.Running, 1, TransitionKind.Immediate, s => s.Running || s.Started) { CountsForLoop = false },

        new("report-done", States.Running, States.Done, 1, TransitionKind.Immediate, s => !s.Running && IsDone(s)) { CountsForLoop = false },
        new("turn-end", States.Running, States.Idle, 2, TransitionKind.Immediate, s => !s.Running) { CountsForLoop = false },

        new("input", States.Idle, States.Running, 1, TransitionKind.Immediate, s => s.Running) { CountsForLoop = false },
        new("report-done", States.Idle, States.Done, 2, TransitionKind.Immediate, s => !s.Running && IsDone(s)) { CountsForLoop = false },
        new("forgotten", States.Idle, States.Forgotten, 3, TransitionKind.Immediate, IsForgotten),

        new("input", States.Done, States.Running, 1, TransitionKind.Immediate, s => s.Running) { CountsForLoop = false },
        new("handled", States.Done, States.Idle, 2, TransitionKind.Immediate, s => !s.Running && !IsDone(s) && s.InputAge is null) { CountsForLoop = false },
        new("auto-archive", States.Done, States.Archived, 3, TransitionKind.Requested, IsAutoArchive) { CountsForLoop = false },

        new("nudge-turn", States.Forgotten, States.Running, 1, TransitionKind.Immediate, s => s.Running),
        new("report-done", States.Forgotten, States.Done, 2, TransitionKind.Immediate, s => !s.Running && IsDone(s)),
        new("remembered", States.Forgotten, States.Idle, 3, TransitionKind.Immediate, s => !s.Running && !IsDone(s) && !IsForgotten(s)),

        new("beat-running", States.Stale, States.Running, 1, TransitionKind.Immediate, s => s.BeatFresh && s.Running),
        new("beat", States.Stale, States.Idle, 2, TransitionKind.Immediate, s => s.BeatFresh && !s.Running),
        new("process-gone", States.Stale, States.Idle, 3, TransitionKind.Immediate, s => s.BeatOld && (s.Holder != Holders.Live || s.ProcessAlive != true)),
        new("process-hangs", States.Stale, States.NotResponding, 4, TransitionKind.Immediate, s => s.BeatOld && s.Holder == Holders.Live && Fault(s) == Faults.HungProcess),

        new("recovered-running", States.NotResponding, States.Running, 1, TransitionKind.Immediate, s => s.Running && Fault(s) is null),
        new("recovered", States.NotResponding, States.Idle, 2, TransitionKind.Immediate, s => !s.Running && Fault(s) is null),

        new("resume", States.Stopped, States.Idle, 1, TransitionKind.Immediate, s => s.Control == ControlStates.Active) { CountsForLoop = false },
    ];

    /// <summary>The checks that detect the state of a session with no state.json, in their fixed order. The first yes wins.</summary>
    public static readonly IReadOnlyList<StateCheck> Checks =
    [
        new(States.Archived, s => s.Control == ControlStates.Archived || !s.Enabled),
        new(States.Stopped, s => s.Control == ControlStates.Stopped),
        new(States.Stale, IsStale),
        new(States.NotResponding, s => Fault(s) is not null),
        new(States.Start, s => !s.Started && !s.Running),
        new(States.Running, s => s.Running),
        new(States.Done, s => !s.Running && IsDone(s)),
        new(States.Forgotten, IsForgotten),
        new(States.Idle, s => !s.Running),
    ];

    /// <summary>The rank of a state: a global transition applies only from a state with a higher rank.</summary>
    public static int Rank(string state) => state switch
    {
        States.Archived => 1,
        States.Stopped => 2,
        States.Stale => 3,
        States.NotResponding => 4,
        _ => 5,
    };

    public static IEnumerable<Transition> From(string state) => Locals.Where(t => t.From == state);

    /// <summary>The state that the checks detect, or null when no check matches.</summary>
    public static string? Detect(Snapshot s) => Checks.FirstOrDefault(c => c.Check(s))?.State;

    /// <summary>One step: from the state in <paramref name="file"/>, only the guarded transitions lead out.</summary>
    public static StepResult Step(StateFile? file, Snapshot s)
    {
        var at = Times.Format(s.Now);
        if (file is null)
        {
            if (Detect(s) is not { } detected) return new StepResult(new StateFile(States.Unknown, at)) { NeedsFallback = true, Changed = true };
            var first = new StateFile(detected, at) { Guard = "detect", Fault = detected == States.NotResponding ? Fault(s) : null, WaitReason = s.WaitReason };
            return new StepResult(first) { Changed = true };
        }
        if (!States.IsKnown(file.State)) return new StepResult(file) { NeedsFallback = file.State != States.Unknown };
        file = file with { WaitReason = s.WaitReason };

        if (file.Requested is { } request)
        {
            var stronger = Globals.FirstOrDefault(g => g.Priority < Rank(request.To) && g.To != request.To && g.Guard(s));
            if (stronger is not null) return Take(file, stronger, s);
            if (IsCancelled(request, s))
                return new StepResult(file with { Requested = null }) { From = file.State, Cancelled = true };
            if (IsCommitted(request, s))
            {
                var transition = Globals.Concat(Locals).First(t => t.Name == request.Transition && t.To == request.To);
                return Change(file, transition, s);
            }
            var level = Escalation(request, s);
            if (level > request.Escalations)
                return new StepResult(file with { Requested = request with { Escalations = level } }) { From = file.State, Escalated = level };
            return new StepResult(file) { From = file.State };
        }

        foreach (var global in Globals)
            if (global.Priority < Rank(file.State) && global.To != file.State && global.Guard(s)) return Take(file, global, s);

        var yes = From(file.State).Where(t => t.Guard(s)).ToList();
        if (yes.Count == 0)
        {
            if (file.State == States.NotResponding && Fault(s) is { } fault && fault != file.Fault)
                return new StepResult(file with { Fault = fault }) { From = file.State };
            return new StepResult(file) { From = file.State };
        }
        var best = yes.Min(t => t.Priority);
        var top = yes.Where(t => t.Priority == best).ToList();
        if (top.Count > 1) return new StepResult(file) { From = file.State, Conflict = [.. top.Select(t => t.Name)], NeedsFallback = true };
        if (file.State == States.NotResponding && file.Fault == Faults.Loop && IsLooping(file, s, null)) return new StepResult(file) { From = file.State };
        return Take(file, top[0], s);
    }

    static StepResult Take(StateFile file, Transition transition, Snapshot s)
    {
        if (transition.Kind == TransitionKind.Immediate) return Change(file, transition, s);
        var waits = transition.To == States.Stopped ? new[] { "no supervisor turn" }
            : s.InApp ? ["no supervisor turn", "enabled gone", "app archived"] : new[] { "no supervisor turn", "enabled gone" };
        var deadline = s.Now + TimeSpan.FromSeconds(transition.To == States.Stopped ? s.Timers.StopSeconds : s.Timers.ArchiveSeconds);
        var request = new Request(transition.To, transition.Name, Times.Format(s.Now), Times.Format(deadline), waits);
        if (IsCommitted(request, s)) return Change(file, transition, s);
        return new StepResult(file with { Requested = request }) { From = file.State, Taken = transition, Requested = true };
    }

    static StepResult Change(StateFile file, Transition transition, Snapshot s)
    {
        var at = Times.Format(s.Now);
        if (transition.CountsForLoop && transition.To != States.NotResponding && IsLooping(file, s, transition.Name))
        {
            if (file.State == States.NotResponding) return new StepResult(file with { Fault = Faults.Loop }) { From = file.State };
            var loop = Globals.First(g => g.To == States.NotResponding);
            return new StepResult(file with { State = States.NotResponding, Since = at, Requested = null, Guard = Faults.Loop, Fault = Faults.Loop, Inferred = false, History = Add(file, s, loop.Name) })
            { From = file.State, Changed = true, Taken = loop };
        }
        var next = file with
        {
            State = transition.To,
            Since = at,
            Requested = null,
            Guard = transition.Name,
            Fault = transition.To == States.NotResponding ? Fault(s) : null,
            Inferred = false,
            Reminded = transition.To == States.Done ? null : file.Reminded,
            History = transition.CountsForLoop ? Add(file, s, transition.Name) : file.History,
        };
        return new StepResult(next) { From = file.State, Changed = true, Taken = transition };
    }

    static IReadOnlyList<StateStep> Add(StateFile file, Snapshot s, string name)
    {
        var window = s.Now - TimeSpan.FromMinutes(s.Timers.LoopMinutes);
        return [.. file.History.Where(h => Times.Parse(h.At) is { } at && at >= window), new StateStep(name, Times.Format(s.Now))];
    }

    /// <summary>True when one transition (or, with no name, any one transition) ran LoopCount times in the loop window.</summary>
    static bool IsLooping(StateFile file, Snapshot s, string? name)
    {
        var window = s.Now - TimeSpan.FromMinutes(s.Timers.LoopMinutes);
        var recent = file.History.Where(h => Times.Parse(h.At) is { } at && at >= window && h.Transition != "fault");
        return recent.GroupBy(h => h.Transition).Any(g => (name is null || g.Key == name) && g.Count() >= s.Timers.LoopCount);
    }

    static bool IsCommitted(Request request, Snapshot s) => request.To switch
    {
        States.Stopped => s.Control == ControlStates.Stopped && !s.SupervisorTurn,
        States.Archived => !s.Enabled && !s.SupervisorTurn && (!s.InApp || s.AppArchived),
        _ => false,
    };

    static bool IsCancelled(Request request, Snapshot s) => request.To switch
    {
        States.Stopped => s.Control != ControlStates.Stopped,
        States.Archived when request.Transition == "auto-archive" => s.Running || s.InputAge is not null || s.Control == ControlStates.Stopped,
        States.Archived => s.Control != ControlStates.Archived && s.Enabled && !s.AppArchived,
        _ => true,
    };

    static int Escalation(Request request, Snapshot s)
    {
        if (Times.Parse(request.Deadline) is not { } deadline || s.Now < deadline) return 0;
        if (request.To != States.Archived) return 1;
        var since = Times.Parse(request.Since) ?? s.Now;
        return s.Now - since >= TimeSpan.FromMinutes(s.Timers.ArchiveUserMinutes) ? 2 : 1;
    }

    /// <summary>The state that the fallback agent inferred: it names the state and starts no action.</summary>
    public static StateFile Inferred(StateFile? file, string state, string reason, DateTimeOffset now) =>
        (file ?? new StateFile(state, Times.Format(now))) with { State = state, Since = Times.Format(now), Inferred = true, Guard = $"inferred: {reason}", Requested = null };
}
