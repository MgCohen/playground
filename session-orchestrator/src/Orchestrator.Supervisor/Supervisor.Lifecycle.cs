using System.Collections.Concurrent;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Lifecycle;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

/// <summary>
/// The lifecycle reactor: at each tick, one step of the state machine for each session, then the actions of the change.
/// Only this code runs the machine. Agents write the signals that the guards read.
/// </summary>
public sealed partial class Supervisor
{
    readonly ConcurrentDictionary<string, Task> fallbacks = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> fallbackAt = new();

    bool FailedTurns(string id) => failures.TryGetValue(id, out var f) && f.Count >= options.MaxFailures;

    /// <summary>The snapshot of one session now: the facts that the guards read.</summary>
    public Snapshot SnapshotOf(string id, DateTimeOffset now) => states.Read(id, now, running.ContainsKey(id), FailedTurns(id));

    void StepStates(DateTimeOffset now)
    {
        foreach (var id in store.AllSessions())
        {
            var (file, invalid) = states.State(id);
            if (file is { State: States.Archived, Requested: null } || fallbacks.ContainsKey(id)) continue;
            var snapshot = SnapshotOf(id, now);
            var result = invalid
                ? new StepResult(new StateFile(States.Unknown, Times.Format(now))) { From = file?.State, Changed = true, NeedsFallback = true }
                : Machine.Step(file, snapshot);
            var next = Keep(id, result.File, snapshot, now);
            if (file is null || StoreJson.Write(file) != StoreJson.Write(next)) states.Save(id, next);
            if (result.Changed || result.Requested || result.Cancelled || result.Escalated > 0) PostState(id, result with { File = next });
            if (result.NeedsFallback) StartFallback(id, snapshot, result, now);
            Act(id, result, snapshot);
            RelayReports(id, snapshot);
        }
    }

    void PostState(string id, StepResult r)
    {
        var file = r.File;
        var what = r.Requested ? $"{file.State} (requested: {file.Requested?.To})"
            : r.Cancelled ? $"{file.State} (request cancelled)"
            : r.Escalated > 0 ? $"{file.State} (requested: {file.Requested?.To}, escalation {r.Escalated})"
            : $"{r.From ?? "none"} → {file.State}";
        var guard = r.Taken?.Name ?? file.Guard;
        store.Events.Append(EventKinds.SessionState, Parties.Supervisor, Parties.Session(id), Data.Of(
            ("text", $"{what}{(guard is null ? "" : $" ({guard})")}"), ("from", r.From), ("to", file.State), ("guard", guard),
            ("requested", file.Requested?.To), ("escalated", r.Escalated > 0 ? r.Escalated : null), ("inferred", file.Inferred ? true : null),
            ("fault", file.Fault), ("waitReason", file.WaitReason)), null, id);
        store.Log($"{id} state {what} ({guard})");
    }

    // The actions of a change, a new request, a cancel or an escalation.

    void Act(string id, StepResult r, Snapshot s)
    {
        var to = r.File.State;
        if (r.Changed && r.From is not null && r.From != to)
        {
            if (r.Taken?.Name == "process-gone") ReleaseLease(id, "the process of the live lease is gone");
            if (to == States.NotResponding) NotifyParent(id, "child-not-responding", ("fault", r.File.Fault ?? "unknown"), ("text", FaultText(r.File.Fault, s)));
            if (r.From == States.NotResponding) NotifyParent(id, "child-responding");
            if (to == States.Stopped) NotifyParent(id, "child-stopped");
            if (r.From == States.Stopped && to != States.Archived) NotifyParent(id, "child-resumed");
            if (to == States.Archived) NotifyParent(id, "child-archived");
            if (to == States.Forgotten) Forgotten(id, s.Now);
        }
        if (r.Requested && r.Taken is { To: States.Archived } taken)
        {
            if (taken.Name == "archive" && s.AppArchived && s.Enabled) NotifyParent(id, "child-archived-in-app");
            if (taken.Name == "auto-archive" && s.InApp && !s.AppArchived && store.IsLive(id, s.Now))
                Notify(Parties.Session(id), null, store.Texts.Notice("archive-self"));
        }
        if (r.Escalated > 0 && r.File.Requested is { } request) Escalate(id, request, r.Escalated, s);
    }

    void Escalate(string id, Request request, int level, Snapshot s)
    {
        var meta = store.Meta(id);
        if (request.To == States.Stopped)
        {
            NotifyParent(id, "child-not-responding", ("fault", "stop"), ("text", $"The stop did not end its turn in {s.Timers.StopSeconds:0} s."));
            return;
        }
        if (level == 1 && HasLiveParent(meta) && meta.AppId is { } appId && s.InApp && !s.AppArchived)
        {
            NotifyParent(id, "archive-child", ("appId", appId));
            return;
        }
        if (level == 2 || !HasLiveParent(meta))
            ToUser(id, $"Archive \"{meta.Title ?? id}\" ({id}) from the app sidebar: the app still shows it after the archive in the store.");
    }

    /// <summary>The actions of a state while it lasts. It returns the state file with the marks of the actions.</summary>
    StateFile Keep(string id, StateFile file, Snapshot s, DateTimeOffset now)
    {
        if (file.Requested is { Transition: "auto-archive" } && s.Enabled && (!s.InApp || s.AppArchived))
        {
            try
            {
                foreach (var line in lifecycle.Archive(id, Parties.Supervisor, force: false)) store.Log($"{id} auto-archive: {line}");
            }
            catch (MessageException e)
            {
                store.Log($"{id} auto-archive refused: {e.Message}");
            }
        }
        if (file.State == States.Forgotten) Forgotten(id, now);
        if (file.State == States.Done && file.Reminded is null && s.Enabled && s.OpenPrs == 0 && s.ReportAge is { } age && age.TotalMinutes >= s.Timers.ReminderMinutes
            && store.Meta(id).Parent is not null)
        {
            NotifyParent(id, "archive-reminder", ("waited", Minutes(age)));
            file = file with { Reminded = Times.Format(now) };
        }
        return RemoteControlCheck(id, file, s, now);
    }

    /// <summary>The nudge of a forgotten session, then one notice to the parent when it is still forgotten after the wait.</summary>
    void Forgotten(string id, DateTimeOffset now)
    {
        var report = store.Report(id)?.Event ?? "none";
        var marker = store.ReadJson<ForgottenMarker>(id, StoreFiles.Forgotten);
        if (marker is null || marker.Report != report)
        {
            Notify(Parties.Session(id), null, store.Texts.Notice("report-missing"));
            store.WriteJson(id, new ForgottenMarker(report, Times.Format(now)), StoreFiles.Forgotten);
            store.Log($"{id} forgotten: nudge");
            return;
        }
        if (marker.Notified is not null || Times.Parse(marker.Nudged) is not { } nudged || now - nudged < TimeSpan.FromMinutes(options.Policy.Lifecycle.ForgottenMinutes)) return;
        NotifyParent(id, "child-forgotten", ("waited", Minutes(now - nudged)));
        store.WriteJson(id, marker with { Notified = Times.Format(now) }, StoreFiles.Forgotten);
    }

    /// <summary>F4: an imported session that did not turn on its Remote Control after the self notice gets the parent path.</summary>
    StateFile RemoteControlCheck(string id, StateFile file, Snapshot s, DateTimeOffset now)
    {
        var meta = store.Meta(id);
        if (file.RemoteControlAsked is not null || !s.InApp || s.AppArchived || meta.Background == true || meta.RemoteControl is not null || meta.AppId is not { } appId) return file;
        var self = Parties.Session(id);
        var asked = store.Events.All().LastOrDefault(e => e.Kind == EventKinds.Notice && e.To == self && store.Events.Fields(e).GetValueOrDefault("kind") as string == "remote-control-self");
        if (asked is null || Times.Parse(asked.At) is not { } at || (now - at).TotalSeconds < s.Timers.RemoteControlSeconds) return file;
        NotifyParent(id, "remote-control-child", ("appId", appId));
        return file with { RemoteControlAsked = Times.Format(now) };
    }

    void ReleaseLease(string id, string reason)
    {
        var lease = store.Lease(id);
        if (lease.Holder != Holders.Live) return;
        store.SaveLease(id, new Lease(Holders.None, store.Now, System.Text.Json.JsonSerializer.SerializeToElement(new { @event = reason, was = lease.Pid })));
        store.Events.Append(EventKinds.LeaseRelease, Parties.Supervisor, Parties.Session(id), Data.Of(("text", reason), ("pid", lease.Pid)), null, id);
        store.Log($"{id} lease released: {reason}");
    }

    // F5: the report goes to the parent at the end of the turn. Two reports in one turn give one forward: the last one.

    void RelayReports(string id, Snapshot s)
    {
        if (s.Running || store.Meta(id).Parent is not { } parent) return;
        var to = Parties.Session(parent);
        var self = Parties.Session(id);
        var open = store.Events.All()
            .Where(e => e.Kind == EventKinds.Report && !e.IsForward && e.From == self && e.Needs is not null
                && !store.Events.ForwardsOf(e.Id).Any(f => f.To == to) && !store.Events.IsAcked(e.Id))
            .ToList();
        if (open.Count == 0) return;
        foreach (var old in open.SkipLast(1))
        {
            store.Events.Ack(old, Parties.Supervisor, "superseded", "read");
            store.Log($"{id} report {old.Id} superseded by a later report of the same turn");
        }
        store.Events.Forward(open[^1], to);
        store.Log($"{id} report {open[^1].Id} forwarded to {parent} at the end of the turn");
    }

    // Notices to the parent. A root has no parent: its notices go to the user queue as a message that needs an ack.

    bool HasLiveParent(Meta meta) => meta.Parent is { } parent && File.Exists(store.PathOf(parent, StoreFiles.Enabled));

    void NotifyParent(string id, string kind, params (string Key, object? Value)[] fields)
    {
        var meta = store.Meta(id);
        var data = store.Texts.Notice(kind, [("title", meta.Title ?? Short(id)), ("child", id), .. fields]);
        if (HasLiveParent(meta)) Notify(Parties.Session(meta.Parent!), null, data);
        else ToUser(id, store.Templates.Render(Kinds.Notice, data, kind));
        store.Log($"{id} notice {kind} to {(HasLiveParent(meta) ? meta.Parent : Parties.User)}");
    }

    void ToUser(string id, string text) =>
        store.Events.Post(new PostInput(EventKinds.Message, Parties.Supervisor, Parties.User, Data.Of(("text", text))) { Session = id, Needs = Needs.Ack });

    static string FaultText(string? fault, Snapshot s) => fault switch
    {
        Faults.FailedTurns => "Its supervisor turns failed. The supervisor tries again after the backoff (5, 10, then 20 min).",
        Faults.StopLatch => $"The engine refuses each delivery until a person types in the session (the stop latch: {s.StopLatch}). Tell the user to type in the session.",
        Faults.InputNotTaken => $"The live session did not take its input for {s.Timers.InputSeconds:0} s. The input stays in its inbox.",
        Faults.HungTurn => $"Its turn wrote no event for {s.Timers.HungMinutes:0} min.",
        Faults.HungProcess => $"Its process is alive, but it wrote no heartbeat for {s.Timers.StaleProcessSeconds:0} s. The supervisor does not resume it. The user must stop it.",
        Faults.Loop => $"One transition ran {s.Timers.LoopCount} times in {s.Timers.LoopMinutes:0} min.",
        _ => "The fault is not known.",
    };

    static string Minutes(TimeSpan span) => span.TotalHours >= 1 ? $"{span.TotalHours:0.#} h" : $"{span.TotalMinutes:0} min";

    static string Short(string id) => id.Length > 8 ? id[..8] : id;

    // The fallback: the agent infers the state when the file is not valid or two guards conflict. It starts no action.

    void StartFallback(string id, Snapshot s, StepResult r, DateTimeOffset now)
    {
        if (fallbackAt.TryGetValue(id, out var last) && now - last < options.FallbackWait) return;
        fallbackAt[id] = now;
        if (r.Conflict.Count > 0)
            store.Events.Append(EventKinds.StateConflict, Parties.Supervisor, Parties.Session(id), Data.Of(
                ("text", $"{r.File.State}: {string.Join(" and ", r.Conflict)} say yes with the same priority"), ("state", r.File.State), ("transitions", r.Conflict)), null, id);
        store.Log($"{id} FINDING: fallback for state {r.File.State}{(r.Conflict.Count > 0 ? $", conflict {string.Join(", ", r.Conflict)}" : ", no valid state.json")}");
        if (options.StateChecker is not { } checker)
        {
            ToUser(id, $"The supervisor cannot calculate the state of \"{store.Meta(id).Title ?? id}\" ({id}), and no state checker is set. Look at it with orch status {Short(id)}.");
            return;
        }
        var request = new StateCheckRequest(id, store.Cwd(id) ?? store.Root, StoreJson.Write(s), r.File.State, r.Conflict);
        Track(fallbacks, id, () => RunFallback(checker, request));
    }

    async Task RunFallback(IStateChecker checker, StateCheckRequest request)
    {
        var id = request.Id;
        StateCheckResult result;
        string? error = null;
        try
        {
            result = await checker.Check(request, CancellationToken.None);
        }
        catch (Exception e)
        {
            result = new StateCheckResult(null, "the state checker failed");
            error = e.Message;
        }
        lock (gate)
        {
            store.WriteJson(id, new { state = result.State, reason = result.Reason, at = store.Now, error }, StoreFiles.StateCheck);
            if (!States.IsKnown(result.State))
            {
                ToUser(id, $"The state of \"{store.Meta(id).Title ?? id}\" ({id}) is unknown: the state checker gave no valid state ({error ?? result.Reason}).");
                return;
            }
            var (file, invalid) = states.State(id);
            var from = file?.State;
            var inferred = Machine.Inferred(invalid || file?.State == States.Unknown ? null : file, result.State!, result.Reason, Now);
            states.Save(id, inferred);
            PostState(id, new StepResult(inferred) { From = from, Changed = true });
            NotifyParent(id, "state-inferred", ("state", result.State), ("text", result.Reason));
        }
    }
}
