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
            if (r.From == States.Forgotten && to is States.Done or States.Stopped or States.Archived) store.Delete(id, StoreFiles.Forgotten);
        }
        if (r.Requested && r.Taken is { To: States.Archived } taken)
        {
            if (taken.Name == "archive" && s.AppArchived && s.Enabled) NotifyParent(id, "child-archived-in-app");
            if (taken.Name == "auto-archive" && s.InApp && !s.AppArchived && store.IsLive(id, s.Now))
                Notify(Parties.Session(id), null, store.Texts.Notice("archive-self", [.. options.Provider.AppTools]));
        }
        if (r.Escalated > 0 && r.File.Requested is { } request) Escalate(id, request, r.Escalated, s);
    }

    void Escalate(string id, Request request, int level, Snapshot s)
    {
        var meta = store.Session(id);
        if (request.To == States.Stopped)
        {
            NotifyParent(id, "child-not-responding", ("fault", "stop"), ("text", $"The stop did not end its turn in {s.Timers.StopSeconds:0} s."));
            return;
        }
        // C3-F6: the app archive texts are only for a session in the app. The snapshot also gives InApp false with no import.
        if (meta.Engine.Imported != true) return;
        if (level == 1 && HasLiveParent(meta) && meta.Engine.AppId is { } appId && s.InApp && !s.AppArchived)
        {
            NotifyParent(id, "archive-child", [("appId", appId), .. options.Provider.AppTools]);
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
        file = ParentArchivePath(id, file, s);
        if (file.State == States.Forgotten) Forgotten(id, now);
        if (file.State == States.Done && file.Reminded is null && s.Enabled && s.OpenPrs == 0 && s.ReportAge is { } age && age.TotalMinutes >= s.Timers.ReminderMinutes
            && store.Session(id).Parent is not null)
        {
            NotifyParent(id, "archive-reminder", ("waited", Minutes(age)));
            file = file with { Reminded = Times.Format(now) };
        }
        return RemoteControlCheck(id, file, s, now);
    }

    /// <summary>
    /// F8, path 2: a session that the app still shows but that is not live cannot archive itself. Its parent gets
    /// archive-child at once (Remote Control off, then the app archive: probe 2a to 2c), not at the deadline.
    /// </summary>
    StateFile ParentArchivePath(string id, StateFile file, Snapshot s)
    {
        if (file.Requested is not { To: States.Archived, Escalations: 0 } request || s.Enabled || !s.InApp || s.AppArchived || store.IsLive(id, s.Now)) return file;
        var meta = store.Session(id);
        if (!HasLiveParent(meta) || meta.Engine.AppId is not { } appId) return file;
        NotifyParent(id, "archive-child", [("appId", appId), .. options.Provider.AppTools]);
        return file with { Requested = request with { Escalations = 1 } };
    }

    /// <summary>The nudge of a forgotten session, then one notice to the parent when it is still forgotten after the wait.</summary>
    /// <remarks>
    /// One forgotten episode lasts while the last report and the last input from another party than the supervisor stay
    /// the same. A new report or a new input starts a new episode with a new nudge (review of PR #14, item 4).
    /// </remarks>
    void Forgotten(string id, DateTimeOffset now)
    {
        var self = Parties.Session(id);
        var input = store.Events.OfSession(id, now - StateReader.Window).LastOrDefault(e => e.To == self && e.From != Parties.Supervisor && store.IsDeliverable(e))?.Id ?? "none";
        var report = $"{store.Report(id)?.Event ?? "none"}|{input}";
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
        var meta = store.Session(id);
        if (file.RemoteControlAsked is not null || !s.InApp || s.AppArchived || meta.IsPrivate() || meta.Engine.RemoteControl is not null || meta.Engine.AppId is not { } appId) return file;
        var self = Parties.Session(id);
        var asked = store.Events.Thread(RemoteControlThread(id)).LastOrDefault(e => e.Kind == EventKinds.Notice && e.To == self);
        if (asked is null || Times.Parse(asked.At) is not { } at || (now - at).TotalSeconds < s.Timers.RemoteControlSeconds) return file;
        NotifyParent(id, "remote-control-child", [("appId", appId), .. options.Provider.AppTools]);
        return file with { RemoteControlAsked = Times.Format(now) };
    }

    /// <summary>The thread of the remote-control-self notice: a named query, not a scan of the stream.</summary>
    public static string RemoteControlThread(string id) => $"remote-control:{id}";

    void ReleaseLease(string id, string reason)
    {
        var lease = store.Lease(id);
        if (lease.Holder != Holders.Live) return;
        store.SaveLease(id, new Lease(Holders.None, store.Now, System.Text.Json.JsonSerializer.SerializeToElement(new { @event = reason, was = lease.Pid })));
        store.Events.Append(EventKinds.LeaseRelease, Parties.Supervisor, Parties.Session(id), Data.Of(("text", reason), ("pid", lease.Pid)), null, id);
        store.Log($"{id} lease released: {reason}");
    }

    // meta.json has one writer: the supervisor. A plugin posts meta.update; a session may change its own appId and
    // remoteControl, and the remoteControl of its child (review of PR #14, item 2).

    public const string MetaSubscription = "meta";

    /// <summary>An entry that the plugin made with /orch-enable has no code: the registry gives it the next free code.</summary>
    void AssignCodes()
    {
        try
        {
            if (store.Registry.AssignCodes(Parties.Supervisor) is var count and > 0) store.Log($"codes given to {count} session(s)");
        }
        catch (Exception e) when (e is RegistryException or Core.Storage.StoreConflictException or IOException)
        {
            store.Log($"codes not given: {e.Message}");
        }
    }

    void ApplyMetaUpdates()
    {
        var subscription = store.Events.Subscribe(Parties.Supervisor, MetaSubscription, e => e.Kind == EventKinds.MetaUpdate, "meta.update from a plugin");
        foreach (var e in subscription.Next())
        {
            var f = store.Events.Fields(e);
            var from = Parties.SessionId(e.From);
            var target = f.GetValueOrDefault("session") as string ?? from;
            var field = f.GetValueOrDefault("field") as string;
            var value = f.GetValueOrDefault("value");
            var allowed = from is not null && target is not null && store.Registry.Exists(target) && field switch
            {
                "appId" => target == from && value is string,
                "remoteControl" => value is bool && (target == from || store.Session(target).Parent == from),
                _ => false,
            };
            if (allowed)
            {
                store.Registry.Update(target!, x => x with { Engine = field == "appId" ? x.Engine with { AppId = (string)value! } : x.Engine with { RemoteControl = (bool)value! } }, e.From);
                store.Log($"{target} meta {field} = {value} (from {e.From})");
            }
            else store.Log($"meta.update {e.Id} refused: {field} of {target} from {e.From}");
            subscription.Done(e.Id);
        }
    }

    // F5: the report goes to the parent at the end of the turn. Two reports in one turn give one forward: the last one.

    /// <summary>
    /// A turn is open: a supervisor turn, or a live process whose status.json says running. An old heartbeat does not end
    /// the turn; a released lease does (review of PR #14, item 8).
    /// </summary>
    static bool TurnOpen(Snapshot s) => s.SupervisorTurn || s.StatusRunning && s.Holder == Holders.Live;

    /// <summary>
    /// One subscription for each session: all reports of one session become done together at the end of its turn, so the
    /// cursor never passes a report that waits. The user ack of a report does not hide it from the parent (item 5).
    /// </summary>
    void RelayReports(string id, Snapshot s)
    {
        var self = Parties.Session(id);
        var subscription = store.Events.Subscribe(Parties.Supervisor, $"reports-{id}",
            e => e.Kind == EventKinds.Report && !e.IsForward && e.From == self && e.Needs is not null, $"done or blocked reports of {id}");
        var open = subscription.Next();
        if (open.Count == 0 || TurnOpen(s)) return;
        if (store.Session(id).Parent is { } parent)
        {
            var to = Parties.Session(parent);
            var waiting = open.Where(e => !store.Events.ForwardsOf(e.Id).Any(f => f.To == to)).ToList();
            foreach (var old in waiting.SkipLast(1))
            {
                if (!store.Events.AcksOf(old.Id).Any(a => a.From == Parties.Supervisor)) store.Events.Ack(old, Parties.Supervisor, "superseded", "read");
                store.Log($"{id} report {old.Id} superseded by a later report of the same turn");
            }
            if (waiting.Count > 0)
            {
                store.Events.Forward(waiting[^1], to);
                store.Log($"{id} report {waiting[^1].Id} forwarded to {parent} at the end of the turn");
            }
        }
        subscription.Done(open.Select(e => e.Id));
    }

    // Notices to the parent. A root has no parent: its notices go to the user queue as a message that needs an ack.

    bool HasLiveParent(SessionEntry meta) => meta.Parent is { } parent && store.IsEnabled(parent);

    void NotifyParent(string id, string kind, params (string Key, object? Value)[] fields)
    {
        var meta = store.Session(id);
        var data = store.Texts.Notice(kind, [("title", meta.Title ?? Short(id)), ("child", id), .. fields]);
        if (HasLiveParent(meta)) Notify(Parties.Session(meta.Parent!), null, data);
        else ToUser(id, store.Templates.Render(Kinds.Notice, data, UserRender(kind)));
        store.Log($"{id} notice {kind} to {(HasLiveParent(meta) ? meta.Parent : Parties.User)}");
    }

    /// <summary>
    /// The render of a parent notice for the user: <c>render/user-&lt;kind&gt;.md</c>. It names a session, not a child, and
    /// the acts of the user, not of a parent session. A root is not a child (D, problem 2).
    /// </summary>
    string UserRender(string kind) => store.Templates[Kinds.Notice].HasRender($"{UserPrefix}{kind}") ? $"{UserPrefix}{kind}" : kind;

    public const string UserPrefix = "user-";

    void ToUser(string id, string text) =>
        store.Events.Post(new PostInput(EventKinds.Message, Parties.Supervisor, Parties.User, Data.Of(("text", text))) { Session = id, Needs = Needs.Ack });

    static string FaultText(string? fault, Snapshot s) => fault switch
    {
        Faults.FailedTurns => "Its supervisor turns failed. The supervisor tries again after the backoff (5, 10, then 20 min).",
        Faults.StopLatch => $"The engine refuses each delivery until a person types in the session (the stop latch: {s.StopLatch}). A person must type in the session.",
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
            ToUser(id, $"The supervisor cannot calculate the state of \"{store.Session(id).Title ?? id}\" ({id}), and no state checker is set. Look at it with orch status {Short(id)}.");
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
                ToUser(id, $"The state of \"{store.Session(id).Title ?? id}\" ({id}) is unknown: the state checker gave no valid state ({error ?? result.Reason}).");
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
