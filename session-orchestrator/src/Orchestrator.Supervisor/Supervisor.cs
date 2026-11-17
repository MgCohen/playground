using System.Collections.Concurrent;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

/// <summary>
/// The reactors of the supervisor on the stream: the relays, the question follow, the docs check, the default,
/// the watchdog and the turn runner. A push of the stream or the timer of the time rules starts a <see cref="Tick"/>.
/// </summary>
public sealed partial class Supervisor
{
    sealed record Failure(int Count, int Rounds, DateTimeOffset? Until, string? Error);

    public const string RelaySubscription = "relay";

    readonly FileStore store;
    readonly ITurnRunner runner;
    readonly IDocsChecker docs;
    readonly SupervisorOptions options;
    readonly Lock gate = new();
    readonly ConcurrentDictionary<string, Task> running = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> stops = new();
    readonly ConcurrentDictionary<string, Task> checks = new();
    readonly ConcurrentDictionary<string, Failure> failures = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> imports = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> nudged = new();
    readonly Lifecycle lifecycle;
    readonly Recovery recovery;
    readonly Messages messages;
    readonly StateReader states;

    public Supervisor(FileStore store, ITurnRunner runner, IDocsChecker docs, SupervisorOptions options)
    {
        this.store = store;
        this.runner = runner;
        this.docs = docs;
        this.options = options;
        store.Policy = options.Policy;
        store.Events.Open();
        lifecycle = new Lifecycle(store);
        recovery = new Recovery(store);
        messages = new Messages(store);
        states = new StateReader(store);
    }

    public FileStore Store => store;
    public SupervisorOptions Options => options;
    public bool IsRunning(string id) => running.ContainsKey(id);
    public int Running => running.Count;

    DateTimeOffset Now => store.Clock.GetUtcNow();

    public void Tick()
    {
        lock (gate)
        {
            var now = Now;
            StopRunningTurns();
            Relay();
            ApplyMetaUpdates();
            StepStates(now);
            var sessions = store.Sessions().Where(id => !lifecycle.IsStopped(id)).ToList();
            foreach (var id in sessions) ApplyPolicy(id, now);
            FollowQuestions(now);
            foreach (var id in sessions)
            {
                Watchdog(id, now);
                if (running.Count < options.MaxRuns) StartTurn(id, now);
            }
        }
    }

    public async Task Idle()
    {
        while (!running.IsEmpty || !checks.IsEmpty || !fallbacks.IsEmpty) await Task.WhenAll([.. running.Values, .. checks.Values, .. fallbacks.Values]);
    }

    void StopRunningTurns()
    {
        foreach (var id in running.Keys)
            if (lifecycle.IsStopped(id) && stops.TryGetValue(id, out var stop) && !stop.IsCancellationRequested)
            {
                store.Log($"{id} turn stopped: {lifecycle.Control(id).State}");
                stop.Cancel();
            }
    }

    // Question relay: each question and plan of a child goes to its parent at once. A report goes at the end of the turn (RelayReports).

    static bool IsRelayed(Event e) =>
        !e.IsForward && Parties.SessionId(e.From) is not null && e.Kind is EventKinds.Question or EventKinds.Plan;

    void Relay()
    {
        var subscription = store.Events.Subscribe(Parties.Supervisor, RelaySubscription, IsRelayed, "question, plan from a child");
        foreach (var e in subscription.Next())
        {
            var child = Parties.SessionId(e.From)!;
            if (store.Meta(child).Parent is { } parent)
            {
                var to = Parties.Session(parent);
                if (!store.Events.ForwardsOf(e.Id).Any(f => f.To == to))
                {
                    store.Events.Forward(e, to);
                    store.Log($"{child} {e.Kind} {e.Thread ?? e.Id} forwarded to {parent}");
                }
            }
            subscription.Done(e.Id);
        }
    }

    // Question follow: a closed notice when the question of a child closes, and reminders while it waits with no default.

    void FollowQuestions(DateTimeOffset now)
    {
        foreach (var forward in EventKinds.Questions.SelectMany(store.Events.OfKind).Where(e => e.IsForward && e.Thread is not null && Parties.SessionId(e.To) is not null))
        {
            var thread = forward.Thread!;
            var notices = store.Events.Thread(thread).Where(n => n.Kind == EventKinds.Notice && n.To == forward.To).Select(n => (Event: n, Kind: store.Events.Fields(n).GetValueOrDefault("kind") as string)).ToList();
            if (notices.Any(n => n.Kind == "question-closed")) continue;
            var child = Parties.SessionId(forward.From) ?? "";
            var title = store.Meta(child).Title ?? child;
            if (store.Decision(thread) is not { } decision) continue;
            if (store.Answer(child, thread) is { } answer)
            {
                Notify(forward.To, thread, store.Texts.Notice("question-closed", ("title", title), ("child", child), ("decision", thread),
                    ("question", decision.Question ?? "(no question text)"), ("text", answer.Value ?? "(no text)"), ("by", answer.By ?? "unknown")));
                continue;
            }
            if (decision.DefaultOption is not null && options.Policy.LevelOf(decision.Risk).TimeoutSeconds is not null) continue;
            var reminders = notices.Where(n => n.Kind == "question-reminder").Select(n => n.Event).ToList();
            var since = Times.Parse(reminders.LastOrDefault()?.At ?? forward.At) ?? now;
            var wait = reminders.Count == 0 ? options.FirstReminder : options.NextReminder;
            if (now - since < wait) continue;
            var waited = now - (Times.Parse(forward.At) ?? now);
            var text = waited.TotalHours >= 1 ? $"{waited.TotalHours:0.#} h" : $"{waited.TotalMinutes:0} min";
            Notify(forward.To, thread, store.Texts.Notice("question-reminder", ("title", title), ("child", child), ("decision", thread),
                ("question", decision.Question ?? "(no question text)"), ("waited", text)));
        }
    }

    Event Notify(string to, string? thread, Dictionary<string, object?> fields) =>
        store.Events.Post(new PostInput(EventKinds.Notice, Parties.Supervisor, to, fields) { Session = Parties.SessionId(to), Thread = thread });

    // Docs check and default: the docs or the policy answer an open decision.

    void ApplyPolicy(string id, DateTimeOffset now)
    {
        foreach (var decision in store.OpenDecisions(id))
        {
            if (decision.Kind != DecisionKinds.AskUser) continue;
            var level = options.Policy.LevelOf(decision.Risk);
            if (level.CheckDocs) StartDocsCheck(id, decision);
            if (!DefaultRule.IsDue(decision, level, now) || store.IsAnswered(id, decision.Id)) continue;
            var reason = DefaultRule.Reason(level);
            messages.Answer(id, decision.Id, decision.DefaultOption!, Parties.Policy, source: new AnswerSource(Parties.Policy) { Reason = reason, OnDefault = options.Policy.OnDefault });
            store.Log($"{id} decision {decision.Id} default {decision.DefaultOption} ({reason})");
        }
    }

    void StartDocsCheck(string id, Decision decision)
    {
        var key = $"{id}/{decision.Id}";
        var path = store.PathOf(id, StoreFiles.DocsChecks, StoreFiles.Json(decision.Id));
        if (checks.ContainsKey(key) || File.Exists(path) || checks.Count >= options.MaxChecks) return;
        if (store.Cwd(id) is not { } cwd) return;
        var request = new DocsRequest(cwd, decision.Question ?? "", decision.Options ?? [], options.Policy.Docs);
        Track(checks, key, () => CheckDocs(id, decision, request, path));
    }

    async Task CheckDocs(string id, Decision decision, DocsRequest request, string path)
    {
        DocsResult result;
        string? error = null;
        try
        {
            result = await docs.Check(request, CancellationToken.None);
        }
        catch (Exception e)
        {
            result = new DocsResult(false);
            error = e.Message;
        }
        Files.WriteJson(path, new DocsCheck(result.Answered, result.Answer, result.Source, Times.Format(Now)) { Error = error });
        if (!result.Answered || result.Answer is not { } answer) return;
        lock (gate)
        {
            if (store.IsAnswered(id, decision.Id)) return;
            messages.Answer(id, decision.Id, answer, Parties.Docs, source: new AnswerSource(Parties.Docs) { Source = result.Source, Reason = $"answered by {result.Source}" });
        }
        store.Log($"{id} decision {decision.Id} answered from the docs: {answer} ({result.Source})");
    }

    public Event Review(string id, string decision, string verdict, string text) => messages.Review(id, decision, verdict, text);

    // Watchdog: an envelope to a session with no ack in time gets a nudge, then a delivery.blocked post.

    void Watchdog(string id, DateTimeOffset now)
    {
        var open = store.Undelivered(id);
        if (open.Count == 0) return;
        var delivery = options.Policy.Delivery;
        var live = store.IsLive(id, now);
        var status = store.Status(id);
        var blocked = store.Events.OfKind(EventKinds.DeliveryBlocked).Select(b => b.Ref("envelope")).OfType<string>().ToHashSet();
        foreach (var e in open)
        {
            if (blocked.Contains(e.Id)) continue;
            if (!live)
            {
                if (failures.TryGetValue(id, out var failure) && failure.Count >= options.MaxFailures)
                    PostBlocked(id, e, $"the supervisor turn failed {failure.Count} times: {failure.Error}");
                continue;
            }
            if (status?.State == "running") continue;
            var start = Times.Parse(e.At) ?? now;
            if (Times.Parse(status?.At) is { } idle && idle > start) start = idle;
            var waited = now - start;
            if (waited >= TimeSpan.FromSeconds(delivery.NudgeSeconds + delivery.PeerSeconds))
                PostBlocked(id, e, $"no ack in {waited.TotalSeconds:0} s from the live session, after a nudge");
            else if (waited >= TimeSpan.FromSeconds(delivery.NudgeSeconds) && nudged.TryAdd(e.Id, now))
            {
                store.Events.Nudge(Parties.Session(id), e.Id);
                store.Log($"{id} nudge: {e.Kind} {e.Id} has no ack after {waited.TotalSeconds:0} s");
            }
        }
    }

    void PostBlocked(string id, Event e, string reason)
    {
        var body = Data.Of(("text", $"{e.Kind} to {store.Meta(id).Title ?? id} is not delivered: {reason}"), ("session", id), ("envelope", e.Id), ("reason", reason));
        var refs = Data.Of(("envelope", e.Id));
        store.Events.Post(new PostInput(EventKinds.DeliveryBlocked, Parties.Supervisor, Parties.User, body) { Session = id, Thread = e.Thread, Needs = Needs.Ack, Refs = refs });
        if (e.From != Parties.User && e.From != Parties.Session(id))
            store.Events.Post(new PostInput(EventKinds.DeliveryBlocked, Parties.Supervisor, e.From, body) { Session = id, Thread = e.Thread, Refs = refs });
        store.Log($"{id} delivery blocked: {e.Kind} {e.Id}: {reason}");
    }

    // Turn runner: a session that is not live gets its envelopes with no ack as one claude -p turn.

    bool CanRun(string id, DateTimeOffset now)
    {
        // A live lease with an old heartbeat is stale: the state machine releases it only when its process is gone (two writers, S0).
        if (running.ContainsKey(id) || store.IsLive(id, now) || store.Lease(id).Holder == Holders.Live) return false;
        if (imports.TryGetValue(id, out var imported) && now - imported < options.ImportWait) return false;
        if (!failures.TryGetValue(id, out var failure) || failure.Count < options.MaxFailures) return true;
        if (failure.Until is not { } until || now < until) return false;
        Notify(Parties.Session(id), null, store.Texts.Notice("restart", ("count", (double)failure.Count), ("error", failure.Error ?? "unknown")));
        store.Log($"{id} restart after {failure.Count} failed turns");
        failures[id] = failure with { Count = 0, Until = null };
        return true;
    }

    void StartTurn(string id, DateTimeOffset now)
    {
        if (running.ContainsKey(id)) return;
        if (recovery.Interrupted(id) is { } marker)
        {
            if (recovery.IsOldProcessAlive(id, now)) return;
            recovery.Recover(id, marker);
        }
        if (!CanRun(id, now)) return;
        var pending = store.Pending(id);
        if (pending.IsEmpty) return;
        if (OpeningTurn(id) is { } opening) pending = opening;
        if (store.Cwd(id) is not { } cwd)
        {
            store.Log($"{id} skipped: no cwd");
            failures[id] = new Failure(options.MaxFailures, 0, null, "no cwd");
            return;
        }
        var meta = store.Meta(id);
        var request = new TurnRequest(id, cwd, pending.Text, meta.CreatedBy is not null && meta.Started != true) { Agent = meta.Agent, Title = meta.Title };
        var stop = new CancellationTokenSource();
        stops[id] = stop;
        Track(running, id, () => RunTurn(request, pending, stop.Token));
    }

    /// <summary>
    /// The first turn of a new session that the supervisor imports into the app: only the notice open-in-app. The turn makes
    /// the transcript, so that the import can follow at once. The brief stays undelivered: the live app session gets it
    /// from its plugin, and the user can follow the work from its start (decision of the user, 2026-11-17).
    /// </summary>
    Pending? OpeningTurn(string id)
    {
        var meta = store.Meta(id);
        if (options.Import is null || meta.Imported == true || meta.CreatedBy is null) return null;
        var self = Parties.Session(id);
        var waiting = store.Undelivered(id).FirstOrDefault(e => e.Kind == EventKinds.Notice && store.Events.Fields(e).GetValueOrDefault("kind") as string == "open-in-app");
        // A stopped opening turn keeps Started and leaves its notice with no ack: the next turn is the opening turn again,
        // as a resume, so the import still comes before the brief (review of PR #14, item 7).
        if (meta.Started == true && waiting is null) return null;
        var notice = waiting ?? Notify(self, null, store.Texts.Notice("open-in-app"));
        return new Pending(store.Renderer.Join([notice]), [notice.Id]) { Senders = [Parties.Supervisor] };
    }

    void Track(ConcurrentDictionary<string, Task> tasks, string key, Func<Task> work)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracked = start.Task.ContinueWith(_ => work(), TaskScheduler.Default).Unwrap()
            .ContinueWith(_ => { lock (gate) tasks.TryRemove(key, out Task? _); }, TaskScheduler.Default);
        tasks[key] = tracked;
        start.SetResult();
    }

    async Task RunTurn(TurnRequest request, Pending pending, CancellationToken stop)
    {
        var id = request.Id;
        try
        {
            if (request.IsNew) request = request with { Cwd = lifecycle.MakeWorktree(id).Cwd ?? request.Cwd };
        }
        catch (Exception e) when (e is ShellException or IOException)
        {
            Fail(request, $"worktree: {e.Message}", null);
            return;
        }
        store.Log($"{id} turn start new={request.IsNew} cwd={request.Cwd} envelopes={string.Join(',', pending.Envelopes)}");
        if (request.IsNew) store.SaveMeta(store.Meta(id) with { Started = true });
        var from = string.Join(',', pending.Senders.DefaultIfEmpty(Parties.Supervisor));
        var start = store.Events.Append(EventKinds.TurnStart, Parties.Supervisor, Parties.Session(id), Data.Of(("isNew", request.IsNew), ("senders", pending.Senders), ("count", (double)pending.Envelopes.Count)),
            Data.Of(("envelopes", pending.Envelopes)), id);
        store.WriteJson(id, new TurnMarker(start.Id, from, start.At), StoreFiles.Turn);
        TurnResult result;
        try
        {
            result = await runner.Run(request, stop);
        }
        catch (Exception e)
        {
            result = new TurnResult(false, null, e.Message, 0);
        }
        if (stop.IsCancellationRequested)
        {
            store.Events.Append(EventKinds.TurnFail, Parties.Session(id), Parties.Supervisor, Data.Of(("text", "stopped"), ("error", "stopped")), Data.Of(("turnStart", start.Id)), id);
            store.Log($"{id} turn stopped after {result.Seconds}s; its envelopes have no ack and go in a later turn");
            EndMarker(id, start);
            return;
        }
        if (result.Ok)
        {
            lock (gate) store.MarkSent(id, pending.Envelopes, Holders.Supervisor);
            if (failures.TryRemove(id, out _)) store.SaveBlocked(id, null);
            var output = store.ReadJson<LastOutput>(id, StoreFiles.LastOutput);
            store.Events.Append(EventKinds.TurnEnd, Parties.Session(id), Parties.Supervisor, Data.Of(("ok", true), ("seconds", result.Seconds), ("text", result.Text ?? "")),
                Data.Of(("turnStart", start.Id), ("output", output is { } o && string.CompareOrdinal(o.Event, start.Id) > 0 ? o.Event : null)), id);
            store.Log($"{id} turn done {result.Seconds}s: {Cut(result.Text ?? "", 200)}");
            EndMarker(id, start);
            ImportOnce(id);
            return;
        }
        if (request.IsNew) store.SaveMeta(store.Meta(id) with { Started = false });
        Fail(request, result.Error ?? "unknown", start.Id);
        EndMarker(id, start);
    }

    void ImportOnce(string id)
    {
        if (options.Import is not { } import || store.Meta(id).Imported == true) return;
        try
        {
            import(id);
            imports[id] = Now;
            store.Log($"{id} imported into the desktop app");
            var meta = store.Meta(id);
            if (meta.Imported == true && meta.Background != true)
                Notify(Parties.Session(id), RemoteControlThread(id), store.Texts.Notice("remote-control-self"));
        }
        catch (Exception e) when (e is ShellException or IOException or InvalidOperationException)
        {
            store.Log($"{id} import failed: {e.Message}");
        }
    }

    void EndMarker(string id, Event start) =>
        store.WriteJson(id, new TurnMarker(start.Id, store.ReadJson<TurnMarker>(id, StoreFiles.Turn)?.From ?? Parties.Supervisor, start.At) { Ended = store.Now }, StoreFiles.Turn);

    void Fail(TurnRequest request, string error, string? turnStart)
    {
        var id = request.Id;
        var failure = failures.AddOrUpdate(id, new Failure(1, 0, null, error), (_, f) => f with { Count = f.Count + 1, Error = error });
        if (failure.Count >= options.MaxFailures)
        {
            failure = failure with { Until = Now + options.RetryAfter * Math.Pow(2, failure.Rounds), Rounds = failure.Rounds + 1 };
            failures[id] = failure;
        }
        store.SaveBlocked(id, $"supervisor turn failed {failure.Count} times: {error}{(failure.Until is { } until ? $". Next try at {Times.Format(until)}" : "")}");
        store.Events.Append(EventKinds.TurnFail, Parties.Session(id), Parties.Supervisor, Data.Of(("count", failure.Count), ("error", error)), Data.Of(("turnStart", turnStart)), id);
        store.Log($"{id} turn failed ({failure.Count}): {error}");
    }

    static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
