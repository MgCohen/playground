using System.Collections.Concurrent;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

public sealed class Supervisor(FileStore store, ITurnRunner runner, IDocsChecker docs, SupervisorOptions options)
{
    readonly Lock gate = new();
    readonly ConcurrentDictionary<string, Task> running = new();
    readonly ConcurrentDictionary<string, Task> checks = new();
    readonly ConcurrentDictionary<string, int> failures = new();

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
            var sessions = store.Sessions();
            foreach (var id in sessions)
            {
                ReleaseStaleLease(id, now);
                ForwardReport(id);
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

    bool CanRun(string id, DateTimeOffset now) =>
        !running.ContainsKey(id) && failures.GetValueOrDefault(id) < options.MaxFailures && !store.IsLive(id, now);

    void StartTurn(string id, DateTimeOffset now)
    {
        if (!CanRun(id, now)) return;
        var pending = store.Pending(id);
        if (pending.IsEmpty) return;
        if (store.Cwd(id) is not { } cwd)
        {
            store.Log($"{id} skipped: no cwd");
            failures[id] = options.MaxFailures;
            return;
        }
        var meta = store.Meta(id);
        var request = new TurnRequest(id, cwd, pending.Text, meta.CreatedBy is not null && meta.Started != true) { Agent = meta.Agent };
        Track(running, id, () => RunTurn(request, pending));
    }

    void Track(ConcurrentDictionary<string, Task> tasks, string key, Func<Task> work)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracked = start.Task.ContinueWith(_ => work(), TaskScheduler.Default).Unwrap()
            .ContinueWith(_ => { lock (gate) tasks.TryRemove(key, out Task? _); }, TaskScheduler.Default);
        tasks[key] = tracked;
        start.SetResult();
    }

    async Task RunTurn(TurnRequest request, Pending pending)
    {
        var id = request.Id;
        store.Log($"{id} turn start new={request.IsNew} inbox={string.Join(',', pending.Inbox)} answers={string.Join(',', pending.Answers)}");
        if (request.IsNew) store.SaveMeta(store.Meta(id) with { Started = true });
        var from = string.Join(',', pending.Senders.DefaultIfEmpty(Parties.Supervisor));
        var start = store.Events.Append(EventKinds.TurnStart, Parties.Supervisor, Parties.Session(id), Data.Of(("isNew", request.IsNew), ("senders", pending.Senders), ("prompt", request.Prompt)), Data.Of(("inbox", pending.Inbox), ("answers", pending.Answers)), id);
        store.WriteJson(id, new TurnMarker(start.Id, from, start.At), StoreFiles.Turn);
        TurnResult result;
        try
        {
            result = await runner.Run(request, CancellationToken.None);
        }
        catch (Exception e)
        {
            result = new TurnResult(false, null, e.Message, 0);
        }
        if (result.Ok)
        {
            store.MarkSent(id, pending);
            failures.TryRemove(id, out _);
            var output = store.ReadJson<LastOutput>(id, StoreFiles.LastOutput);
            store.Events.Append(EventKinds.TurnEnd, Parties.Session(id), Parties.Supervisor, Data.Of(("ok", true), ("seconds", result.Seconds), ("text", result.Text ?? "")),
                Data.Of(("turnStart", start.Id), ("output", output is { } o && string.CompareOrdinal(o.Event, start.Id) > 0 ? o.Event : null)), id);
            store.Log($"{id} turn done {result.Seconds}s: {Cut(result.Text ?? "", 200)}");
            return;
        }
        if (request.IsNew) store.SaveMeta(store.Meta(id) with { Started = false });
        var count = failures.AddOrUpdate(id, 1, (_, n) => n + 1);
        store.SaveBlocked(id, $"supervisor turn failed {count} times: {result.Error}");
        store.Events.Append(EventKinds.TurnFail, Parties.Session(id), Parties.Supervisor, Data.Of(("count", count), ("error", result.Error)), Data.Of(("turnStart", start.Id)), id);
        store.Log($"{id} turn failed ({count}): {result.Error}");
    }

    static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
