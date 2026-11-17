using Orchestrator.Core.Policy;

namespace Orchestrator.Core.Lifecycle;

/// <summary>The nine lifecycle states, and unknown while the fallback agent infers the state.</summary>
public static class States
{
    public const string Start = "start";
    public const string Running = "running";
    public const string Idle = "idle";
    public const string Stale = "stale";
    public const string NotResponding = "not-responding";
    public const string Forgotten = "forgotten";
    public const string Done = "done";
    public const string Stopped = "stopped";
    public const string Archived = "archived";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All = [Start, Running, Idle, Stale, NotResponding, Forgotten, Done, Stopped, Archived];

    public static bool IsKnown(string? state) => state is not null && All.Contains(state);
}

/// <summary>Why an idle session waits. None: it waits for nothing.</summary>
public static class WaitReasons
{
    public const string Decision = "decision";
    public const string Child = "child";
    public const string Blocked = "blocked";
    public const string None = "none";
}

/// <summary>The four faults of the state not responding, and a loop of one transition.</summary>
public static class Faults
{
    public const string FailedTurns = "failed-turns";
    public const string StopLatch = "stop-latch";
    public const string InputNotTaken = "input-not-taken";
    public const string HungTurn = "hung-turn";
    public const string HungProcess = "hung-process";
    public const string Loop = "loop";
}

/// <summary>
/// The facts that the guards read: one read of the store files and the supervisor memory for one session at one tick.
/// The guards are pure functions of a snapshot, so each guard has a unit test.
/// </summary>
public sealed record Snapshot(DateTimeOffset Now)
{
    public LifecycleTimers Timers { get; init; } = LifecycleTimers.Default;
    public AutoArchive AutoArchive { get; init; } = AutoArchive.Default;

    /// <summary>The enabled file exists.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The state in control.json: active, stopped or archived.</summary>
    public string Control { get; init; } = Contract.ControlStates.Active;

    /// <summary>meta.json has started: true.</summary>
    public bool Started { get; init; }

    /// <summary>A turn is in flight: a supervisor turn, or status.json running with a fresh heartbeat.</summary>
    public bool Running { get; init; }

    /// <summary>status.json says running, with a fresh or an old heartbeat.</summary>
    public bool StatusRunning { get; init; }

    /// <summary>The supervisor has a turn of this session in flight.</summary>
    public bool SupervisorTurn { get; init; }

    public string Holder { get; init; } = Contract.Holders.None;

    /// <summary>The age of heartbeat.txt, or of the lease when it is newer. Null: no heartbeat.</summary>
    public TimeSpan? BeatAge { get; init; }

    /// <summary>The process in lease.json is alive. Null: the lease has no process id.</summary>
    public bool? ProcessAlive { get; init; }

    /// <summary>The supervisor turns failed MaxFailures times in a row.</summary>
    public bool FailedTurns { get; init; }

    /// <summary>The stop latch reason that the plugin wrote in blocked.json. Null: no latch.</summary>
    public string? StopLatch { get; init; }

    /// <summary>The age of the oldest envelope to the session with no ack. Null: nothing waits.</summary>
    public TimeSpan? InputAge { get; init; }

    /// <summary>The time since the last envelope of the session. Null: no envelope.</summary>
    public TimeSpan? SinceEvent { get; init; }

    /// <summary>The time since the last turn ended. Null: no turn ended yet.</summary>
    public TimeSpan? IdleFor { get; init; }

    /// <summary>The state of the newest report of the session: working, blocked or done. Null: no report.</summary>
    public string? Report { get; init; }

    /// <summary>The age of the newest report.</summary>
    public TimeSpan? ReportAge { get; init; }

    /// <summary>An envelope to the session arrived after its newest report.</summary>
    public bool InputAfterReport { get; init; }

    /// <summary>An envelope to the session arrived after the end of the last turn.</summary>
    public bool InputAfterTurn { get; init; }

    public string WaitReason { get; init; } = WaitReasons.None;

    public int OpenDecisions { get; init; }

    public int OpenPrs { get; init; }

    /// <summary>The app has a record of the session.</summary>
    public bool InApp { get; init; }

    /// <summary>The app record shows isArchived.</summary>
    public bool AppArchived { get; init; }

    public bool BeatFresh => BeatAge is { } age && age.TotalSeconds < Timers.StaleSeconds;

    public bool BeatOld => !BeatFresh;
}

/// <summary>How a transition commits.</summary>
public enum TransitionKind
{
    /// <summary>The state changes when the guard says yes.</summary>
    Immediate,

    /// <summary>The machine records a request and starts the actions. The state changes when the expected signals arrive.</summary>
    Requested,
}

/// <summary>
/// A guarded transition. <see cref="From"/> is <see cref="Machine.Any"/> for a global transition.
/// For two local transitions of one state, the lower <see cref="Priority"/> wins.
/// </summary>
public sealed record Transition(string Name, string From, string To, int Priority, TransitionKind Kind, Func<Snapshot, bool> Guard)
{
    /// <summary>False for the main path (start, running, idle, done): a normal session takes these many times in one hour.</summary>
    public bool CountsForLoop { get; init; } = true;

    public override string ToString() => Name;
}

/// <summary>An open requested transition. The state stays until the signals in <see cref="WaitsFor"/> arrive.</summary>
public sealed record Request(string To, string Transition, string Since, string Deadline, IReadOnlyList<string> WaitsFor)
{
    /// <summary>0: no escalation yet. 1: the first escalation (the parent). 2: the second (the user).</summary>
    public int Escalations { get; init; }
}

/// <summary>One state change in the history of state.json, for the loop rule.</summary>
public sealed record StateStep(string Transition, string At);

/// <summary>store/sessions/&lt;id&gt;/state.json. Only the supervisor writes it.</summary>
public sealed record StateFile(string State, string Since)
{
    public Request? Requested { get; init; }

    /// <summary>True: the fallback agent inferred the state.</summary>
    public bool Inferred { get; init; }

    /// <summary>The transition or the check that set the state.</summary>
    public string? Guard { get; init; }

    /// <summary>The fault of the state not responding.</summary>
    public string? Fault { get; init; }

    public string? WaitReason { get; init; }

    /// <summary>The time of the archive reminder of a done session.</summary>
    public string? Reminded { get; init; }

    /// <summary>The time of the notice that asked the parent to turn on the Remote Control of the session (F4).</summary>
    public string? RemoteControlAsked { get; init; }

    public IReadOnlyList<StateStep> History { get; init; } = [];
}
