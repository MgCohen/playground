using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Stack;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

/// <summary>
/// The store: the stream (<see cref="Events"/>) and the run state of each session, through the repositories of
/// <see cref="Repositories"/>. Decisions, answers, reviews, reports and the pending deliveries are queries of the stream.
/// </summary>
public sealed class FileStore
{
    Policy? policy;

    public FileStore(string root, TimeProvider? clock = null, TemplateSet? templates = null, string writer = Parties.Supervisor)
    {
        Repositories = StoreBackends.Create(root);
        Root = Repositories.Location;
        Clock = clock ?? TimeProvider.System;
        Texts = new Texts(templates ?? Home.LoadTemplates(Root));
        Events = new EventStream(Repositories, Clock, writer, Texts.Templates);
        Renderer = new Renderer(this);
    }

    public IRepositories Repositories { get; }

    /// <summary>Where the store is: <see cref="IRepositories.Location"/>.</summary>
    public string Root { get; }
    public TimeProvider Clock { get; }
    public EventStream Events { get; }
    public Texts Texts { get; }
    public Renderer Renderer { get; }
    public TemplateSet Templates => Texts.Templates;

    /// <summary>The policy in use: store/policy.json, or the one the supervisor sets.</summary>
    public Policy Policy
    {
        get => policy ??= Home.LoadPolicy(Repositories);
        set => policy = value;
    }

    public string Now => Times.Format(Clock.GetUtcNow());

    /// <summary>The local path of a session file. Only for tests that write a file as a hook does: the code uses the repositories.</summary>
    public string PathOf(string id, params string[] parts) => ((Local.LocalRepositories)Repositories).PathOf(id, parts);

    public IReadOnlyList<string> AllSessions() => Repositories.Sessions.List(_ => true).Select(d => d.Id).ToList();

    public IReadOnlyList<string> Sessions() => AllSessions().Where(IsEnabled).ToList();

    /// <summary>True when the session has the enabled marker.</summary>
    public bool IsEnabled(string id) => Has(id, StoreFiles.Enabled);

    /// <summary>Writes or deletes the enabled marker.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        if (enabled) WriteText(id, StoreFiles.Enabled, "");
        else Delete(id, StoreFiles.Enabled);
    }

    public Meta Meta(string id) => ReadJson<Meta>(id, StoreFiles.Meta) ?? new Meta(id);

    public void SaveMeta(Meta meta) => WriteJson(meta.Id, meta, StoreFiles.Meta);

    public Lease Lease(string id) => ReadJson<Lease>(id, StoreFiles.Lease) ?? new Lease(Holders.None, "");

    public void SaveLease(string id, Lease lease) => WriteJson(id, lease, StoreFiles.Lease);

    public DateTimeOffset? Heartbeat(string id) => Times.Parse(ReadText(id, StoreFiles.Heartbeat));

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
        SetEnabled(id, true);
        return id;
    }

    // Questions: envelopes of kind question from a session. The thread is the decision id.

    public Event? DecisionEvent(string thread) =>
        Events.Thread(thread).FirstOrDefault(e => EventKinds.Questions.Contains(e.Kind) && !e.IsForward);

    public Decision? Decision(string thread) => DecisionEvent(thread) is { } e ? ToDecision(e) : null;

    public Decision? Decision(string id, string decision) => Decision(decision) is { } d && d.Session == id ? d : null;

    public IReadOnlyList<Decision> Decisions(string id) =>
        EventKinds.Questions.SelectMany(kind => Events.PostsFrom(Parties.Session(id), kind)).Where(e => e.Thread is not null)
            .OrderBy(e => e.Id, Core.Ordering.Names.Order).Select(ToDecision).ToList();

    public bool IsAnswered(string id, string decision) => Events.FirstAnswer(decision) is not null;

    public bool IsOpen(string id, string decision) => !IsAnswered(id, decision);

    public IReadOnlyList<Decision> OpenDecisions(string id) => Decisions(id).Where(d => !IsAnswered(id, d.Id)).ToList();

    /// <summary>The open questions of all sessions, oldest first: the named query <see cref="StreamQueries.OpenDecisions"/>.</summary>
    public IReadOnlyList<Decision> OpenDecisions() => this.Query(StreamQueries.OpenDecisions()).Where(e => e.Thread is not null).Select(ToDecision).ToList();

    public Decision ToDecision(Event e)
    {
        var f = Events.Fields(e);
        var risk = f.GetValueOrDefault("risk") as string;
        return new Decision(e.Thread ?? e.Id, Parties.SessionId(e.From) ?? e.Session ?? "")
        {
            // A question with no risk and no default takes no policy: the old kind query.
            Kind = DecisionKinds.Of(f),
            Question = f.GetValueOrDefault("question") as string,
            Context = f.GetValueOrDefault("context") as string,
            Snippet = f.GetValueOrDefault("snippet") as string,
            Recommendation = f.GetValueOrDefault("recommendation") as string,
            Options = f.GetValueOrDefault("options") is List<object?> list ? list.Select(o => o?.ToString() ?? "").ToArray() : null,
            Risk = risk ?? "high",
            DefaultOption = f.GetValueOrDefault("defaultOption") as string,
            Mode = f.GetValueOrDefault("waitSeconds") is double wait && wait > 0 ? DecisionModes.Wait : DecisionModes.Pending,
            WaitSeconds = f.GetValueOrDefault("waitSeconds") as double? ?? 0,
            Asked = e.At,
            Event = e.Id,
        };
    }

    /// <summary>
    /// The time when the policy answers this open question with its default, by the rule of the supervisor: only a
    /// question with a defaultOption, at a level with a timeout. None for any other envelope.
    /// </summary>
    public DateTimeOffset? DefaultAt(Event e)
    {
        if (!EventKinds.Questions.Contains(e.Kind) || e.IsForward || e.Thread is null || Events.FirstAnswer(e.Thread) is not null) return null;
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
        return AllSessions().FirstOrDefault(id => Repositories.SessionItems<StackEntry>(id, StoreFiles.Stack).Exists(decision));
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

    /// <summary>A core message kind of <see cref="EventKinds.Deliverable"/>, or an added kind whose meta.json says it is a turn.</summary>
    public bool IsDeliverable(Event e) =>
        EventKinds.Deliverable.Contains(e.Kind) ? e.Kind != EventKinds.Report || e.Needs is not null || e.IsForward
        : !Kinds.All.Contains(e.Kind) && Templates.Has(e.Kind) && Templates[e.Kind].Meta?.IsTurn == true;

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

    public Status? Status(string id) => ReadJson<Status>(id, StoreFiles.Status);

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

    public StackEntry? Stack(string id, string decision) => Repositories.SessionItems<StackEntry>(id, StoreFiles.Stack).Read(decision);

    public IReadOnlyList<StackEntry> Stacks(string id) =>
        Repositories.SessionItems<StackEntry>(id, StoreFiles.Stack).List(_ => true).Select(d => d.Value).OfType<StackEntry>().ToList();

    public void SaveStack(StackEntry entry) => Repositories.SessionItems<StackEntry>(entry.Session, StoreFiles.Stack).Put(entry.Decision, entry);

    public IReadOnlyList<Event> Ledger() => this.Query(StreamQueries.Ledger);

    // Run state files of a session: one file (parts: the file name), or one item of a session folder (parts: the
    // folder and the item name, with or without .json).

    public T? ReadJson<T>(string id, params string[] parts) => Repository<T>(id, parts, out var key).Read(key);

    public void WriteJson<T>(string id, T value, params string[] parts) => Repository<T>(id, parts, out var key).Put(key, value);

    /// <summary>True when the run state file exists, valid or not.</summary>
    public bool Has(string id, params string[] parts) => Repository<string>(id, parts, out var key).Exists(key);

    public string? ReadText(string id, params string[] parts) => Repository<string>(id, parts, out var key).Read(key);

    public void WriteText(string id, string file, string text) => Repositories.SessionFile<string>(file).Put(id, text);

    public void Delete(string id, params string[] parts) => Repository<string>(id, parts, out var key).Delete(key);

    IRepository<Doc<T>, string> Repository<T>(string id, string[] parts, out string key)
    {
        switch (parts.Length)
        {
            case 1:
                key = id;
                return Repositories.SessionFile<T>(parts[0]);
            case 2:
                key = parts[1].EndsWith(".json") ? parts[1][..^5] : parts[1];
                return Repositories.SessionItems<T>(id, parts[0]);
            default:
                throw new ArgumentException($"a run state file has one or two parts, not {parts.Length}: {string.Join("/", parts)}");
        }
    }

    public void Log(string line) => Repositories.Log.Write($"{Now} {line}");
}
