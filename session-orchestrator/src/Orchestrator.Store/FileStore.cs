using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Stack;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

/// <summary>
/// The store: the stream (<see cref="Events"/>) and the lifecycle files of each session. Decisions, answers,
/// reviews, reports and the pending deliveries are queries of the stream.
/// </summary>
public sealed class FileStore
{
    Policy? policy;

    public FileStore(string root, TimeProvider? clock = null, TemplateSet? templates = null, string writer = Parties.Supervisor)
    {
        Root = Path.GetFullPath(root);
        Clock = clock ?? TimeProvider.System;
        Texts = new Texts(templates ?? new TemplateSet(Home.Templates()));
        Events = new EventStream(Root, Clock, writer, Texts.Templates);
        Renderer = new Renderer(this);
    }

    public string Root { get; }
    public TimeProvider Clock { get; }
    public EventStream Events { get; }
    public Texts Texts { get; }
    public Renderer Renderer { get; }
    public TemplateSet Templates => Texts.Templates;

    /// <summary>The policy in use: store/policy.json, or the one the supervisor sets.</summary>
    public Policy Policy
    {
        get => policy ??= Home.LoadPolicy(Root);
        set => policy = value;
    }

    public string Now => Times.Format(Clock.GetUtcNow());

    public string Dir(string id) => Path.Combine(Root, StoreFiles.Sessions, id);

    public string PathOf(string id, params string[] parts) => Path.Combine([Dir(id), .. parts]);

    public IReadOnlyList<string> AllSessions() => Files.Names(Path.Combine(Root, StoreFiles.Sessions));

    public IReadOnlyList<string> Sessions() => AllSessions().Where(id => File.Exists(PathOf(id, StoreFiles.Enabled))).ToList();

    public Meta Meta(string id) => Files.ReadJson<Meta>(PathOf(id, StoreFiles.Meta)) ?? new Meta(id);

    public void SaveMeta(Meta meta) => Files.WriteJson(PathOf(meta.Id, StoreFiles.Meta), meta);

    public Lease Lease(string id) => Files.ReadJson<Lease>(PathOf(id, StoreFiles.Lease)) ?? new Lease(Holders.None, "");

    public void SaveLease(string id, Lease lease) => Files.WriteJson(PathOf(id, StoreFiles.Lease), lease);

    public DateTimeOffset? Heartbeat(string id) => Times.Parse(Files.ReadText(PathOf(id, StoreFiles.Heartbeat)));

    public bool IsLive(string id, DateTimeOffset now) => LeaseRules.IsLive(Lease(id), Heartbeat(id), now);

    public string? Cwd(string id)
    {
        if (Meta(id).Cwd is { } cwd) return cwd;
        var reason = Lease(id).Reason;
        return reason is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Object && start.TryGetProperty("cwd", out var c)
            ? c.GetString()
            : null;
    }

    /// <summary>Writes meta.json and the enabled file of a new session. The first message is a post to the session.</summary>
    public string Create(Meta meta)
    {
        var id = meta.Id.Length > 0 ? meta.Id : Guid.NewGuid().ToString();
        SaveMeta(meta with { Id = id, CreatedAt = Now });
        Files.Write(PathOf(id, StoreFiles.Enabled), "");
        return id;
    }

    // Decisions and queries: envelopes of kind decision or query from a session. The thread is the decision id.

    public Event? DecisionEvent(string thread) =>
        Events.Thread(thread).FirstOrDefault(e => EventKinds.Questions.Contains(e.Kind) && !e.IsForward);

    public Decision? Decision(string thread) => DecisionEvent(thread) is { } e ? ToDecision(e) : null;

    public Decision? Decision(string id, string decision) => Decision(decision) is { } d && d.Session == id ? d : null;

    public IReadOnlyList<Decision> Decisions(string id) =>
        Events.FromParty(Parties.Session(id)).Where(e => EventKinds.Questions.Contains(e.Kind) && !e.IsForward && e.Thread is not null).Select(ToDecision).ToList();

    public bool IsAnswered(string id, string decision) => Events.FirstAnswer(decision) is not null;

    public bool IsOpen(string id, string decision) => !IsAnswered(id, decision);

    public IReadOnlyList<Decision> OpenDecisions(string id) => Decisions(id).Where(d => !IsAnswered(id, d.Id)).ToList();

    /// <summary>The open decisions and queries of all sessions, oldest first: the named query <see cref="StreamQueries.OpenDecisions"/>.</summary>
    public IReadOnlyList<Decision> OpenDecisions() => this.Query(StreamQueries.OpenDecisions()).Where(e => e.Thread is not null).Select(ToDecision).ToList();

    public Decision ToDecision(Event e)
    {
        var f = Events.Fields(e);
        return new Decision(e.Thread ?? e.Id, Parties.SessionId(e.From) ?? e.Session ?? "")
        {
            Kind = e.Kind == EventKinds.Query ? "query" : "ask_user",
            Question = f.GetValueOrDefault("question") as string,
            Context = f.GetValueOrDefault("context") as string,
            Snippet = f.GetValueOrDefault("snippet") as string,
            Recommendation = f.GetValueOrDefault("recommendation") as string,
            Options = f.GetValueOrDefault("options") is List<object?> list ? list.Select(o => o?.ToString() ?? "").ToArray() : null,
            Risk = f.GetValueOrDefault("risk") as string ?? (e.Kind == EventKinds.Query ? "high" : "medium"),
            DefaultOption = f.GetValueOrDefault("defaultOption") as string,
            Mode = f.GetValueOrDefault("waitSeconds") is double wait && wait > 0 ? DecisionModes.Wait : DecisionModes.Pending,
            WaitSeconds = f.GetValueOrDefault("waitSeconds") as double? ?? 0,
            Asked = e.At,
            Event = e.Id,
        };
    }

    /// <summary>
    /// The time when the policy answers this open decision with its default, by the rule of the supervisor: only a
    /// decision (not a query), with a defaultOption, at a level with a timeout. None for any other envelope.
    /// </summary>
    public DateTimeOffset? DefaultAt(Event e)
    {
        if (e.Kind != EventKinds.Decision || e.IsForward || e.Thread is null || Events.FirstAnswer(e.Thread) is not null) return null;
        var decision = ToDecision(e);
        return DefaultRule.At(decision, Policy.LevelOf(decision.Risk));
    }

    public Answer? Answer(string id, string decision) => Events.FirstAnswer(decision) is { } e ? ToAnswer(e) : null;

    Answer ToAnswer(Event e)
    {
        var f = Events.Fields(e);
        var by = f.GetValueOrDefault("by") as string ?? e.From;
        var decision = f.GetValueOrDefault("decision") as string ?? e.Thread ?? "";
        return new Answer
        {
            Value = f.GetValueOrDefault("text") as string,
            By = by,
            Source = f.GetValueOrDefault("source") as string,
            Reason = f.GetValueOrDefault("reason") as string,
            Note = Texts.AnswerNote(by, f.GetValueOrDefault("onDefault") as string, decision, f.GetValueOrDefault("reason") as string, f.GetValueOrDefault("source") as string),
            Via = e.Via,
            Event = e.Id,
            At = e.At,
        };
    }

    public string? SessionOf(string decision)
    {
        if (DecisionEvent(decision) is { } e) return Parties.SessionId(e.From) ?? e.Session;
        var file = StoreFiles.Json(decision);
        return AllSessions().FirstOrDefault(id => File.Exists(PathOf(id, StoreFiles.Stack, file)));
    }

    public string? ResolveSession(string prefix)
    {
        var all = AllSessions();
        if (all.Contains(prefix)) return prefix;
        var matches = all.Where(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // Reviews: the newest review in the thread of the decision, for one version id.

    public Review? Review(string id, string versionId)
    {
        var decision = StackNames.DecisionOf(versionId);
        var e = Events.Thread(decision).LastOrDefault(r => r.Kind == EventKinds.Review && !r.IsForward
            && (Events.Fields(r).GetValueOrDefault("version") as string ?? decision) == versionId);
        if (e is null) return null;
        var f = Events.Fields(e);
        return new Review(f.GetValueOrDefault("verdict") as string ?? "", f.GetValueOrDefault("text") as string ?? "", e.At) { Event = e.Id };
    }

    // Reports and updates: the newest post of the session.

    public Event? LastPost(string id, string kind) => Events.LastFrom(Parties.Session(id), kind);

    public Report? Report(string id)
    {
        if (LastPost(id, EventKinds.Report) is not { } e) return null;
        var f = Events.Fields(e);
        return new Report(f.GetValueOrDefault("state") as string ?? "", f.GetValueOrDefault("summary") as string ?? "", e.At)
        {
            Details = f.GetValueOrDefault("details") as string,
            Done = Strings(f.GetValueOrDefault("done")),
            Next = Strings(f.GetValueOrDefault("next")),
            Blockers = Strings(f.GetValueOrDefault("blockers")),
            Links = Strings(f.GetValueOrDefault("links")),
            Event = e.Id,
        };
    }

    static IReadOnlyList<string>? Strings(object? value) => value is List<object?> list ? list.Select(o => o?.ToString() ?? "").ToList() : null;

    // Delivery: the envelopes to a session that have no ack.

    public StreamSubscription Deliveries(string id)
    {
        var to = Parties.Session(id);
        return Events.Subscribe(to, "deliver", e => e.To == to && IsDeliverable(e), "to = self, message kinds, no ack");
    }

    public static bool IsDeliverable(Event e) =>
        EventKinds.Deliverable.Contains(e.Kind) && (e.Kind != EventKinds.Report || e.Needs is not null || e.IsForward);

    /// <summary>
    /// The envelopes to the session with no ack, in order. The shared cursor moves only over the acknowledged envelopes
    /// before the first open one, so it never passes an envelope that still waits for its ack.
    /// </summary>
    public IReadOnlyList<Event> Undelivered(string id)
    {
        var subscription = Deliveries(id);
        var done = new List<string>();
        var open = new List<Event>();
        foreach (var e in subscription.Next())
        {
            if (!Events.IsAcked(e.Id)) open.Add(e);
            else if (open.Count == 0) done.Add(e.Id);
        }
        subscription.Done(done);
        return open;
    }

    public Pending Pending(string id)
    {
        var open = Undelivered(id);
        return new Pending(Renderer.Join(open), open.Select(e => e.Id).ToList()) { Senders = open.Select(e => e.From).Distinct().ToList() };
    }

    /// <summary>Posts an ack for each envelope: the receiver got it.</summary>
    public void MarkSent(string id, IEnumerable<string> envelopes, string by)
    {
        foreach (var envelope in envelopes)
            if (Events.Find(envelope) is { } e && !Events.IsAcked(e.Id)) Events.Ack(e, Parties.Session(id), by);
    }

    public Status? Status(string id) => Files.ReadJson<Status>(PathOf(id, StoreFiles.Status));

    /// <summary>The newest <c>blocked</c> envelope about the session. Its reason is None when the session is not blocked.</summary>
    public Blocked? Blocked(string id) =>
        Events.OfKind(EventKinds.Blocked).LastOrDefault(e => e.Session == id) is { } e ? new Blocked(Events.Fields(e).GetValueOrDefault("reason") as string, e.At) { Event = e.Id, By = e.From == Parties.Supervisor ? Parties.Supervisor : null } : null;

    /// <summary>Posts a <c>blocked</c> envelope from the supervisor to the user when the reason changes.</summary>
    public void SaveBlocked(string id, string? reason)
    {
        if (Blocked(id)?.Reason == reason) return;
        Events.Append(EventKinds.Blocked, Parties.Supervisor, Parties.User,
            new Dictionary<string, object?> { ["text"] = reason is null ? "delivery unblocked" : $"delivery blocked: {reason}", ["reason"] = reason }, session: id);
    }

    public StackEntry? Stack(string id, string decision) => Files.ReadJson<StackEntry>(PathOf(id, StoreFiles.Stack, StoreFiles.Json(decision)));

    public IReadOnlyList<StackEntry> Stacks(string id) =>
        Files.Names(PathOf(id, StoreFiles.Stack)).Select(name => Files.ReadJson<StackEntry>(PathOf(id, StoreFiles.Stack, name))).OfType<StackEntry>().ToList();

    public void SaveStack(StackEntry entry) => Files.WriteJson(PathOf(entry.Session, StoreFiles.Stack, StoreFiles.Json(entry.Decision)), entry);

    public IReadOnlyList<Event> Ledger() => this.Query(StreamQueries.Ledger);

    public T? ReadJson<T>(string id, params string[] parts) => Files.ReadJson<T>(PathOf(id, parts));

    public void WriteJson<T>(string id, T value, params string[] parts) => Files.WriteJson(PathOf(id, parts), value);

    public void Log(string line) => Files.Append(Path.Combine(Root, StoreFiles.SupervisorLog), $"{Now} {line}");
}
