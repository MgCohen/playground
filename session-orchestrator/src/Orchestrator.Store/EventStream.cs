using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Ordering;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

/// <summary>The input of the primitive Post.</summary>
public sealed record PostInput(string Kind, string From, string To, IReadOnlyDictionary<string, object?> Body)
{
    public string? Session { get; init; }
    public string? Thread { get; init; }
    public string? Needs { get; init; }
    public IReadOnlyDictionary<string, object?>? Refs { get; init; }

    /// <summary>The work stream. Null: the stream of the session of the post in the registry (<see cref="EventStream.StreamOf"/>).</summary>
    public string? Stream { get; init; }
}

/// <summary>
/// The Stream API: post, subscribe, query, read and nudge on the store, through the repositories of
/// <see cref="IRepositories"/>. The class keeps all envelopes in memory, with indexes, and adds only the new
/// envelopes of the repository at each call. The index by session (<see cref="OfSession"/>) and the index by sender
/// and kind (<see cref="LastFrom"/>, <see cref="PostsFrom"/>) let the snapshot of each tick read only the envelopes
/// of one session (finding B2-9).
/// </summary>
public sealed class EventStream
{
    readonly IRepositories repos;
    readonly TimeProvider clock;
    readonly string writer;
    readonly TemplateSet? templates;
    readonly Lock gate = new();
    readonly List<Event> events = [];
    readonly Dictionary<string, Event> byId = [];
    readonly Dictionary<string, List<Event>> byOf = [];
    readonly Dictionary<string, List<Event>> byReply = [];
    readonly Dictionary<string, List<Event>> byThread = [];
    readonly Dictionary<string, List<Event>> byKind = [];
    readonly Dictionary<string, List<Event>> bySession = [];
    readonly Dictionary<(string From, string Kind), List<Event>> byPost = [];
    readonly Dictionary<string, JsonElement> bodies = [];
    readonly Dictionary<string, string?> streams = [];
    long lastMs;
    int feedPosition;

    public EventStream(string root, TimeProvider clock, string writer = Parties.Supervisor, TemplateSet? templates = null)
        : this(StoreBackends.Create(root), clock, writer, templates)
    {
    }

    public EventStream(IRepositories repos, TimeProvider clock, string writer = Parties.Supervisor, TemplateSet? templates = null)
    {
        this.repos = repos;
        this.clock = clock;
        this.writer = writer;
        this.templates = templates;
    }

    public string Root => repos.Location;

    // 0. Setup

    /// <summary>Makes the store ready for the stream.</summary>
    public void Open() => repos.Prepare();

    // 1. Post

    /// <summary>
    /// Writes the body, then the envelope: the envelope is the commit point. A message kind must match its template
    /// and the from, to and reply rules of its meta.json. A second answer in one thread is refused.
    /// </summary>
    public Event Post(PostInput post)
    {
        post = post with { Body = post.Body.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value) };
        lock (gate)
        {
            Check(post);
            Claim(post);
            var (id, ms) = NewId();
            var body = $"{StoreFiles.Bodies}/{Day(ms)}/{id}.json";
            var element = JsonSerializer.SerializeToElement(post.Body, StoreJson.Options);
            repos.Bodies.Add(new Doc<JsonElement>(body, element));
            repos.Bodies.Save();
            var e = new Event(id, Times.Format(clock.GetUtcNow()), post.Session, post.Kind, post.From, post.To, Subjects.Of(post.Kind, post.Body), body, ToElements(post.Refs))
            {
                Thread = post.Thread,
                Needs = post.Needs,
                Stream = post.Stream ?? StreamOf(post.Session ?? Parties.SessionId(post.From) ?? Parties.SessionId(post.To)),
            };
            Write(e);
            bodies[e.Body] = element;
            Add(e);
            return e;
        }
    }

    /// <summary>
    /// The stream of a session in the registry. The registry sets the stream when it creates the entry and never
    /// changes it, so the result of an entry is kept. A session with no entry yet is read again at the next post.
    /// </summary>
    public string? StreamOf(string? session)
    {
        if (session is null) return null;
        lock (gate)
        {
            if (streams.TryGetValue(session, out var known)) return known;
            if (repos.SessionEntries.Read(session) is not { } entry) return null;
            streams[session] = entry.Stream;
            return entry.Stream;
        }
    }

    /// <summary>A post of a trace, lifecycle or git kind, with no thread.</summary>
    public Event Append(string kind, string from, string to, IReadOnlyDictionary<string, object?> data, IReadOnlyDictionary<string, object?>? refs = null, string? session = null) =>
        Post(new PostInput(kind, from, to, data) { Refs = refs, Session = session });

    /// <summary>A new envelope with a new receiver, the same body, and refs.of set to the original.</summary>
    public Event Forward(Event original, string to, string? needs = null, IReadOnlyDictionary<string, object?>? refs = null)
    {
        lock (gate)
        {
            if (templates?.Has(original.Kind) == true && templates[original.Kind].Meta is { } meta && !meta.AllowsTo(Parties.ClassOf(to)))
                throw new MessageException($"{original.Kind}: a forward to {to} is not allowed. Allowed: {string.Join(", ", meta.To)}");
            var (id, _) = NewId();
            var allRefs = new Dictionary<string, object?>(refs ?? new Dictionary<string, object?>()) { [RefKeys.Of] = original.Id };
            var e = original with
            {
                Id = id,
                At = Times.Format(clock.GetUtcNow()),
                To = to,
                Refs = ToElements(allRefs),
                Needs = needs,
            };
            Write(e);
            Add(e);
            return e;
        }
    }

    /// <summary>The envelope is the commit point of a post.</summary>
    void Write(Event e)
    {
        repos.Events.Add(new Doc<Event>(e.Id, e));
        repos.Events.Save();
    }

    /// <summary>The pong: an ack with refs.of. <paramref name="from"/> is the receiver, <paramref name="by"/> the way it arrived.</summary>
    public Event Ack(Event of, string from, string by, string state = "delivered") =>
        Post(new PostInput(EventKinds.Ack, from, of.From, new Dictionary<string, object?> { ["state"] = state, ["by"] = by })
        {
            Session = of.Session,
            Thread = of.Thread,
            Refs = new Dictionary<string, object?> { [RefKeys.Of] = of.Id },
        });

    void Check(PostInput post)
    {
        if (templates is null || !templates.Has(post.Kind)) return;
        var check = templates.Validate(post.Kind, post.Body);
        if (!check.IsValid) throw new MessageException($"{post.Kind}: the body does not match templates/{post.Kind}/schema.json: {check.Message}");
        var template = templates[post.Kind];
        if (template.Meta is { } meta)
        {
            if (!meta.AllowsFrom(Parties.ClassOf(post.From))) throw new MessageException($"{post.Kind}: from {post.From} is not allowed. Allowed: {string.Join(", ", meta.From)}");
            if (!meta.AllowsTo(Parties.ClassOf(post.To))) throw new MessageException($"{post.Kind}: to {post.To} is not allowed. Allowed: {string.Join(", ", meta.To)}");
        }
        if (post.Refs?.TryGetValue(RefKeys.Reply, out var reply) == true && reply is string replyId)
        {
            Refresh();
            if (!byId.TryGetValue(replyId, out var replied)) throw new MessageException($"{post.Kind}: refs.reply {replyId} names no envelope");
            if (templates.Has(replied.Kind) && templates[replied.Kind].Meta is { } repliedMeta && !repliedMeta.Replies.Contains(post.Kind))
                throw new MessageException($"{post.Kind}: a {replied.Kind} takes only these replies: {string.Join(", ", repliedMeta.Replies)}");
        }
        if (post.Kind == EventKinds.Answer && post.Thread is { } thread && FirstAnswer(thread) is { } first)
        {
            var text = Fields(first).TryGetValue("text", out var t) ? t : "";
            throw new MessageException($"decision {thread} already has an answer from {first.From}: {text}. The session has this answer. Nothing changed.");
        }
    }

    /// <summary>
    /// First answer wins between processes too: before an answer is posted, its writer adds the claim
    /// answer/&lt;thread&gt;. A second add of the same claim fails, and its writer gets the first answer in the error text.
    /// </summary>
    void Claim(PostInput post)
    {
        if (post.Kind != EventKinds.Answer || post.Thread is not { } thread) return;
        var claim = StoreIds.AnswerClaim(thread);
        if (repos.Claims.TryCreate(claim, post.From)) return;
        for (var attempt = 0; attempt < 20 && FirstAnswer(thread) is null; attempt++) System.Threading.Thread.Sleep(50);
        var first = FirstAnswer(thread);
        var text = first is null ? "(the answer is not written yet)" : Fields(first).GetValueOrDefault("text");
        throw new MessageException($"decision {thread} already has an answer from {first?.From ?? repos.Claims.Read(claim) ?? "another writer"}: {text}. The session has this answer. Nothing changed.");
    }

    (string Id, long Ms) NewId()
    {
        var ms = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (ms <= lastMs) ms = lastMs + 1;
        lastMs = ms;
        return (Names.NewEventId(DateTimeOffset.FromUnixTimeMilliseconds(ms), writer), ms);
    }

    // 3. Query

    public IReadOnlyList<Event> All()
    {
        lock (gate)
        {
            Refresh();
            return events.ToList();
        }
    }

    public IEnumerable<Event> Since(DateTimeOffset since) => After(since.ToUnixTimeMilliseconds().ToString());

    /// <summary>The envelopes with an id after <paramref name="start"/>, in id order.</summary>
    public IReadOnlyList<Event> After(string? start)
    {
        lock (gate)
        {
            Refresh();
            if (start is null) return events.ToList();
            var first = LowerBound(start);
            return events.GetRange(first, events.Count - first);
        }
    }

    public Event? Find(string id)
    {
        lock (gate)
        {
            if (byId.TryGetValue(id, out var e)) return e;
            Refresh();
            return byId.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<Event> Thread(string thread) => Index(byThread, thread);

    public IReadOnlyList<Event> RepliesTo(string id) => Index(byReply, id);

    /// <summary>The envelopes of one kind, in id order.</summary>
    public IReadOnlyList<Event> OfKind(string kind) => Index(byKind, kind);

    /// <summary>The envelopes of one sender (the full party) and one kind that are not forwards, in id order.</summary>
    public IReadOnlyList<Event> PostsFrom(string party, string kind) => Index(byPost, (party, kind));

    /// <summary>The newest envelope of one sender and one kind that is not a forward.</summary>
    public Event? LastFrom(string party, string kind)
    {
        lock (gate)
        {
            Refresh();
            return byPost.TryGetValue((party, kind), out var list) && list.Count > 0 ? list[^1] : null;
        }
    }

    /// <summary>
    /// The envelopes of one session, in id order: from the session, to the session, or with the session in their
    /// <c>session</c> field. With <paramref name="since"/>: only the envelopes with an id time at or after it.
    /// </summary>
    public IReadOnlyList<Event> OfSession(string id, DateTimeOffset? since = null)
    {
        lock (gate)
        {
            Refresh();
            if (!bySession.TryGetValue(id, out var list)) return [];
            if (since is not { } time) return list.ToList();
            var first = LowerBound(list, time.ToUnixTimeMilliseconds().ToString());
            return list.GetRange(first, list.Count - first);
        }
    }

    /// <summary>The acks and the forwards of one envelope.</summary>
    public IReadOnlyList<Event> OfEnvelope(string id) => Index(byOf, id);

    public IReadOnlyList<Event> AcksOf(string id) => OfEnvelope(id).Where(e => e.Kind == EventKinds.Ack).ToList();

    public IReadOnlyList<Event> ForwardsOf(string id) => OfEnvelope(id).Where(e => e.Kind != EventKinds.Ack).ToList();

    public bool IsAcked(string id) => AcksOf(id).Count > 0;

    /// <summary>The first answer in a thread that is not a forward.</summary>
    public Event? FirstAnswer(string thread) => Thread(thread).FirstOrDefault(e => e.Kind == EventKinds.Answer && !e.IsForward);

    IReadOnlyList<Event> Index<TKey>(Dictionary<TKey, List<Event>> index, TKey key) where TKey : notnull
    {
        lock (gate)
        {
            Refresh();
            return index.TryGetValue(key, out var list) ? list.ToList() : [];
        }
    }

    // 4. Read

    public JsonElement? Body(Event e)
    {
        lock (gate)
        {
            if (bodies.TryGetValue(e.Body, out var cached)) return cached;
            var body = repos.Bodies.Read(e.Body);
            if (body.ValueKind == JsonValueKind.Undefined) return null;
            bodies[e.Body] = body;
            return body;
        }
    }

    public Dictionary<string, object?> Fields(Event e) => Body(e) is { } body ? Render.Fields(body) : [];

    // 5. Nudge

    /// <summary>A wake-up signal with no content: the receiver looks at the stream.</summary>
    public void Nudge(string to, string? id = null) => repos.Nudges.Put(Parties.Folder(to), id ?? "");

    /// <summary>True when the party has a nudge.</summary>
    public bool IsNudged(string party) => repos.Nudges.Exists(Parties.Folder(party));

    // 2. Subscribe

    public StreamSubscription Subscribe(string party, string name, Func<Event, bool> filter, string? description = null) =>
        new(this, repos.Cursors, $"{Parties.Folder(party)}/{name}", party, name, filter, description, clock);

    // The cache

    void Refresh()
    {
        if (repos.Events is IAppendFeed<Doc<Event>> feed)
        {
            var docs = feed.ReadFrom(feedPosition);
            feedPosition += docs.Count;
            foreach (var doc in docs) Add(EventKinds.Current(doc.Value));
            return;
        }
        foreach (var doc in repos.Events.List(d => !byId.ContainsKey(d.Id))) Add(EventKinds.Current(doc.Value));
    }

    void Add(Event e)
    {
        if (!byId.TryAdd(e.Id, e)) return;
        Insert(events, e);
        if (e.Of is { } of) Insert(Bucket(byOf, of), e);
        if (e.Reply is { } reply) Insert(Bucket(byReply, reply), e);
        if (e.Thread is { } thread) Insert(Bucket(byThread, thread), e);
        Insert(Bucket(byKind, e.Kind), e);
        if (!e.IsForward) Insert(Bucket(byPost, (e.From, e.Kind)), e);
        foreach (var session in SessionsOf(e)) Insert(Bucket(bySession, session), e);
        if (long.TryParse(e.Id.Split('-')[0], out var ms) && e.Id.EndsWith($"-{writer}") && ms > lastMs) lastMs = ms;
    }

    /// <summary>The sessions of an envelope: the sender, the receiver and the session field, each one time.</summary>
    static IEnumerable<string> SessionsOf(Event e) =>
        new[] { Parties.SessionId(e.From), Parties.SessionId(e.To), e.Session }.OfType<string>().Where(s => s.Length > 0).Distinct();

    static List<Event> Bucket<TKey>(Dictionary<TKey, List<Event>> index, TKey key) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = [];
        return list;
    }

    static void Insert(List<Event> list, Event e)
    {
        var at = list.Count;
        while (at > 0 && Names.Order.Compare(list[at - 1].Id, e.Id) > 0) at--;
        list.Insert(at, e);
    }

    int LowerBound(string start) => LowerBound(events, start);

    /// <summary>The index of the first envelope with an id after <paramref name="start"/>.</summary>
    static int LowerBound(List<Event> list, string start)
    {
        int low = 0, high = list.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Names.Order.Compare(list[mid].Id, start) <= 0) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>The day of an id time: a part of the body path of the envelope contract.</summary>
    static string Day(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd");

    static IReadOnlyDictionary<string, JsonElement> ToElements(IReadOnlyDictionary<string, object?>? refs) =>
        (refs ?? new Dictionary<string, object?>()).Where(r => r.Value is not null).ToDictionary(r => r.Key, r => JsonSerializer.SerializeToElement(r.Value, StoreJson.Options));
}

/// <summary>
/// One subscriber: a filter and a private cursor in subscriptions/&lt;party&gt;/&lt;name&gt;.json. <see cref="Next"/> gives the
/// matching envelopes that the subscriber did not mark done. Envelopes of other writers can arrive a little late,
/// so the cursor keeps the done ids of the last <see cref="Grace"/>.
/// </summary>
public sealed class StreamSubscription
{
    /// <summary>
    /// A writer takes the id time before its two writes, so its envelope can appear a little after newer ids.
    /// An envelope that appears more than this time after its id time can be missed by a subscription.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(120);

    readonly EventStream stream;
    readonly IRepository<Doc<CursorState>, string> cursors;
    readonly string id;
    readonly Func<Event, bool> filter;
    readonly TimeProvider clock;

    internal StreamSubscription(EventStream stream, IRepository<Doc<CursorState>, string> cursors, string id, string party, string name, Func<Event, bool> filter, string? description, TimeProvider clock)
    {
        this.stream = stream;
        this.cursors = cursors;
        this.id = id;
        this.filter = filter;
        this.clock = clock;
        Party = party;
        Name = name;
        Description = description;
    }

    public string Party { get; }
    public string Name { get; }
    public string? Description { get; }
    /// <summary>The id of the cursor in <see cref="IRepositories.Cursors"/>.</summary>
    public string Id => id;

    public string? Cursor => Load().Cursor;

    public IReadOnlyList<Event> Next()
    {
        var state = Load();
        var recent = state.Recent.ToHashSet();
        var start = state.Cursor is { } cursor ? Minus(cursor, Grace) : null;
        return stream.After(start).Where(e => !recent.Contains(e.Id) && filter(e)).ToList();
    }

    public void Done(string id) => Done([id]);

    /// <summary>
    /// Merges the done ids into the cursor: read, merge, save. The supervisor and orch watch write the same cursor.
    /// A save of an old version fails, so the merge reads the newer cursor and merges again. No writer loses the
    /// done ids of the other writer.
    /// </summary>
    public void Done(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return;
        // The repository has one unit of work for all threads of this process. Without the lock, a save of one thread
        // can take the change of another thread and the other thread then saves nothing. Other processes use the version.
        lock (cursors) Merge(list);
    }

    void Merge(List<string> list)
    {
        for (var attempt = 1; ; attempt++)
        {
            var doc = cursors.Get(id);
            var state = doc?.Value ?? Empty;
            var cursor = state.Cursor;
            var recent = state.Recent.ToHashSet();
            foreach (var done in list)
            {
                recent.Add(done);
                if (cursor is null || Names.Order.Compare(done, cursor) > 0) cursor = done;
            }
            var floor = Minus(cursor!, Grace);
            var kept = recent.Where(r => Names.Order.Compare(r, floor) > 0).Order(Names.Order).ToList();
            var merged = new CursorState(Party, Name, cursor, kept, Times.Format(clock.GetUtcNow())) { Filter = Description };
            if (doc is null) cursors.Add(new Doc<CursorState>(id, merged));
            else doc.Value = merged;
            try
            {
                cursors.Save();
                return;
            }
            catch (StoreConflictException) when (attempt < MergeAttempts)
            {
            }
        }
    }

    const int MergeAttempts = 50;

    CursorState Empty => new(Party, Name, null, [], "");

    CursorState Load() => cursors.Read(id) ?? Empty;

    static string Minus(string id, TimeSpan by) =>
        long.TryParse(id.Split('-')[0], out var ms) ? Math.Max(0, ms - (long)by.TotalMilliseconds).ToString() : id;
}
