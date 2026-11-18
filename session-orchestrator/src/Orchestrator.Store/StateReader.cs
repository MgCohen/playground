using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Lifecycle;
using Orchestrator.Core.Stack;

namespace Orchestrator.Store;

/// <summary>Reads the lifecycle facts of one session into a <see cref="Snapshot"/>, and reads and writes state.json.</summary>
public sealed class StateReader(FileStore store)
{
    /// <summary>The state file. <c>Invalid</c> is true when the file exists but is not valid JSON or names no known state.</summary>
    public (StateFile? File, bool Invalid) State(string id)
    {
        if (!store.Has(id, StoreFiles.State)) return (null, false);
        var file = store.ReadJson<StateFile>(id, StoreFiles.State);
        if (file is null || file.State is null) return (null, true);
        return (file, file.State != States.Unknown && !States.IsKnown(file.State));
    }

    /// <summary>The longest look back of a lifecycle query. Older envelopes changed the state long ago.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public void Save(string id, StateFile file) => store.WriteJson(id, file, StoreFiles.State);

    /// <summary>One read of the store for one session. The supervisor gives the facts that only it knows.</summary>
    public Snapshot Read(string id, DateTimeOffset now, bool supervisorTurn, bool failedTurns)
    {
        var policy = store.Policy;
        var meta = store.Meta(id);
        var lease = store.Lease(id);
        var beat = store.Heartbeat(id);
        var leaseSince = Times.Parse(lease.Since);
        var newest = beat is { } b && leaseSince is { } l ? (b > l ? b : l) : beat ?? leaseSince;
        var beatAge = newest is { } n ? now - n : (TimeSpan?)null;
        var fresh = beatAge is { } age && age.TotalSeconds < policy.Lifecycle.StaleSeconds;
        var status = store.Status(id);
        var running = supervisorTurn || status?.State == "running" && fresh;
        var turn = store.ReadJson<TurnMarker>(id, StoreFiles.Turn);
        var idleSince = Latest(status?.State == "idle" ? Times.Parse(status.At) : null, Times.Parse(turn?.Ended));
        var report = store.Report(id);
        var reportAt = Times.Parse(report?.At);
        var self = Parties.Session(id);
        // Bounded queries (review of PR #14, item 9): the own events of the hung window, the inputs after the report or the turn.
        var hung = TimeSpan.FromMinutes(policy.Lifecycle.HungMinutes + 1);
        var floor = now - Window;
        bool Own(Event e) => e.From == self || e.Kind == EventKinds.TurnStart && e.Session == id;
        // The hung window first; only when it is empty, the 24 h window. No event at all: null, not hung (a new turn).
        var lastOwn = store.Events.Since(now - hung).Where(Own).Select(e => Times.Parse(e.At)).Max()
            ?? store.Events.Since(floor).Where(Own).Select(e => Times.Parse(e.At)).Max();
        bool InputAfter(DateTimeOffset? at) => at is { } t && store.Events.Since(t > floor ? t : floor).Any(e => e.To == self && FileStore.IsDeliverable(e) && Times.Parse(e.At) > t);
        var undelivered = store.Undelivered(id);
        var openDecisions = store.OpenDecisions(id).Count;
        var blocked = store.Blocked(id);
        var appId = meta.AppId;
        var inApp = appId is not null && AppRecords.Exists(appId);
        return new Snapshot(now)
        {
            Timers = policy.Lifecycle,
            AutoArchive = policy.AutoArchive,
            Enabled = store.IsEnabled(id),
            Control = new Lifecycle(store).Control(id).State,
            Started = meta.Started == true,
            Running = running,
            StatusRunning = status?.State == "running",
            SupervisorTurn = supervisorTurn,
            Holder = lease.Holder,
            BeatAge = beatAge,
            ProcessAlive = lease.Holder == Holders.Live && lease.Pid is { } pid ? Processes.IsAlive(pid, lease.Started) : null,
            FailedTurns = failedTurns,
            StopLatch = blocked is { Reason: { } reason, By: null } ? reason : null,
            InputAge = undelivered.Count > 0 && Times.Parse(undelivered[0].At) is { } first ? now - first : null,
            SinceEvent = lastOwn is { } own ? now - own : null,
            IdleFor = !running && idleSince is { } idle ? now - idle : null,
            Report = report?.State,
            ReportAge = reportAt is { } r ? now - r : null,
            InputAfterReport = InputAfter(reportAt),
            InputAfterTurn = InputAfter(idleSince),
            WaitReason = WaitReason(id, openDecisions, report?.State),
            OpenDecisions = openDecisions,
            OpenPrs = store.Stacks(id).SelectMany(e => e.Versions).Count(v => v.State is VersionStates.Open or VersionStates.Ready && v.Pr is not null),
            InApp = inApp,
            AppArchived = inApp && AppRecords.IsArchived(appId!),
        };
    }

    string WaitReason(string id, int openDecisions, string? report)
    {
        if (openDecisions > 0) return WaitReasons.Decision;
        var children = store.Sessions().Where(s => store.Meta(s).Parent == id);
        if (children.Any(c => State(c).File?.State is not (States.Done or States.Archived))) return WaitReasons.Child;
        if (report == ReportStates.Blocked) return WaitReasons.Blocked;
        return WaitReasons.None;
    }

    static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
