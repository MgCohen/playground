using System.Collections.Concurrent;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

public sealed class Supervisor(FileStore store, ITurnRunner runner, IDocsChecker docs, SupervisorOptions options)
{
    sealed record Failure(int Count, int Rounds, DateTimeOffset? Until, string? Error);

    readonly Lock gate = new();
    readonly ConcurrentDictionary<string, Task> running = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> stops = new();
    readonly ConcurrentDictionary<string, Task> checks = new();
    readonly ConcurrentDictionary<string, Failure> failures = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> imports = new();
    readonly Lifecycle lifecycle = new(store);
    readonly Recovery recovery = new(store);

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
            var sessions = store.Sessions().Where(id => !lifecycle.IsStopped(id)).ToList();
            foreach (var id in sessions)
            {
                ReleaseStaleLease(id, now);
                ForwardReport(id);
                ForwardQuestions(id);
                ApplyPolicy(id, now);
            }
            foreach (var id in sessions)
            {
                if (running.Count >= options.MaxRuns) return;
                StartTurn(id, now);
            }
        }
    }

    public async Task Idle()
    {
        while (!running.IsEmpty || !checks.IsEmpty) await Task.WhenAll([.. running.Values, .. checks.Values]);
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

    void ReleaseStaleLease(string id, DateTimeOffset now)
    {
        if (store.Lease(id).Holder != Holders.Live || store.IsLive(id, now)) return;
        store.SaveLease(id, new Lease(Holders.None, Times.Format(now), System.Text.Json.JsonSerializer.SerializeToElement(new { @event = "stale heartbeat" })));
        store.Log($"{id} lease released: stale heartbeat");
    }

    void ForwardReport(string id)
    {
        var meta = store.Meta(id);
        if (meta.Parent is not { } parent || store.Report(id) is not { } report || report.State == ReportStates.Working) return;
        if (store.ReadJson<Forwarded>(id, StoreFiles.Forwarded)?.At == report.At) return;
        store.Send(parent, store.Texts.ChildReport(meta.Title ?? id, id, report.State, report.Summary, report.Details), Parties.Session(id));
        store.WriteJson(id, new Forwarded(report.At), StoreFiles.Forwarded);
        store.Events.Append(EventKinds.ReportForward, Parties.Session(id), Parties.Session(parent), Data.Of(("state", report.State), ("summary", report.Summary)), Data.Of(("child", id), ("report", report.Event)), parent);
        store.Log($"{id} report {report.State} forwarded to {parent}");
    }

    void ForwardQuestions(string id)
    {
        var meta = store.Meta(id);
        if (meta.Parent is not { } parent) return;
        foreach (var decision in store.OpenDecisions(id))
        {
            var marker = store.PathOf(id, StoreFiles.QuestionsForwarded, StoreFiles.Json(decision.Id));
            if (File.Exists(marker)) continue;
            var level = options.Policy.LevelOf(decision.Risk);
            var defaultText = decision.DefaultOption is not { } option ? "There is no default: the child waits for an answer."
                : level.TimeoutSeconds is { } seconds ? $"If nobody answers in {seconds:0} s, the policy answers \"{option}\"."
                : $"The default is \"{option}\", but this level never applies it.";
            store.Send(parent, store.Texts.ChildQuestion(meta.Title ?? id, id, decision, defaultText), Parties.Session(id));
            Files.WriteJson(marker, new Forwarded(Times.Format(Now)));
            store.Events.Append(EventKinds.QuestionForward, Parties.Session(id), Parties.Session(parent), Data.Of(("decision", decision.Id), ("question", decision.Question)), Data.Of(("child", id), ("decision", decision.Id)), parent);
            store.Log($"{id} question {decision.Id} forwarded to {parent}");
        }
    }

    void ApplyPolicy(string id, DateTimeOffset now)
    {
        foreach (var decision in store.OpenDecisions(id))
        {
            var level = options.Policy.LevelOf(decision.Risk);
            if (level.CheckDocs) StartDocsCheck(id, decision);
            if (!DefaultRule.IsDue(decision, level, now) || store.IsAnswered(id, decision.Id)) continue;
            var reason = DefaultRule.Reason(level);
            store.Events.Append(EventKinds.Answer, Parties.Policy, Parties.Session(id), Data.Of(("decision", decision.Id), ("text", decision.DefaultOption), ("question", decision.Question), ("reason", reason)), Data.Of(("decision", decision.Id)), id);
            store.SaveAnswer(id, decision.Id, new Answer { Value = decision.DefaultOption, By = Parties.Policy, Reason = reason, Note = store.Texts.DefaultNote(options.Policy, decision.Id, reason) });
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
        if (!result.Answered || result.Answer is not { } answer || store.IsAnswered(id, decision.Id)) return;
        store.Events.Append(EventKinds.Answer, Parties.Docs, Parties.Session(id), Data.Of(("decision", decision.Id), ("text", answer), ("question", decision.Question), ("source", result.Source)), Data.Of(("decision", decision.Id)), id);
        store.SaveAnswer(id, decision.Id, new Answer { Value = answer, By = Parties.Docs, Source = result.Source, Reason = $"answered by {result.Source}", Note = store.Texts.DocsNote(result.Source) });
        store.Log($"{id} decision {decision.Id} answered from the docs: {answer} ({result.Source})");
    }

    public string Review(string id, string decision, string verdict, string text) => new Messages(store).Review(id, decision, verdict, text);

    bool CanRun(string id, DateTimeOffset now)
    {
        if (running.ContainsKey(id) || store.IsLive(id, now)) return false;
        if (imports.TryGetValue(id, out var imported) && now - imported < options.ImportWait) return false;
        if (!failures.TryGetValue(id, out var failure) || failure.Count < options.MaxFailures) return true;
        if (failure.Until is not { } until || now < until) return false;
        store.Send(id, store.Texts.Blocked(failure.Count, failure.Error ?? "unknown"), Parties.Supervisor);
        store.Events.Append(EventKinds.Unblocked, Parties.Supervisor, Parties.Session(id), Data.Of(("text", $"restart after {failure.Count} failed turns"), ("count", failure.Count), ("error", failure.Error)), null, id);
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
        store.Log($"{id} turn start new={request.IsNew} cwd={request.Cwd} inbox={string.Join(',', pending.Inbox)} answers={string.Join(',', pending.Answers)}");
        if (request.IsNew) store.SaveMeta(store.Meta(id) with { Started = true });
        var from = string.Join(',', pending.Senders.DefaultIfEmpty(Parties.Supervisor));
        var start = store.Events.Append(EventKinds.TurnStart, Parties.Supervisor, Parties.Session(id), Data.Of(("isNew", request.IsNew), ("senders", pending.Senders), ("prompt", request.Prompt)), Data.Of(("inbox", pending.Inbox), ("answers", pending.Answers)), id);
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
            store.Log($"{id} turn stopped after {result.Seconds}s; the inbox stays for a later turn");
            EndMarker(id, start);
            return;
        }
        if (result.Ok)
        {
            store.MarkSent(id, pending);
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
