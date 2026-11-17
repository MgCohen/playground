using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Ordering;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

/// <summary>The input of the primitive Post.</summary>
public sealed record PostInput(string Kind, string From, string To, IReadOnlyDictionary<string, object?> Body)
{
    public string? Session { get; init; }
    public string? Thread { get; init; }
    public string? Needs { get; init; }
    public IReadOnlyDictionary<string, object?>? Refs { get; init; }
}

/// <summary>
/// The Stream API: post, subscribe, query, read and nudge on the store. Envelopes live in events/, bodies in bodies/.
/// The class keeps all envelopes in memory and reads only new files at each call.
/// </summary>
public sealed class EventStream
{
    readonly string root;
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
    readonly Dictionary<string, List<Event>> byFrom = [];
    readonly Dictionary<string, JsonElement> bodies = [];
    readonly HashSet<string> closedDays = [];
    long lastMs;

    public EventStream(string root, TimeProvider clock, string writer = Parties.Supervisor, TemplateSet? templates = null)
    {
        this.root = root;
        this.clock = clock;
        this.writer = writer;
        this.templates = templates;
    }

    public string Root => root;

    // 0. Setup

    /// <summary>Makes the folders of the stream.</summary>
    public void Open()
    {
        foreach (var folder in new[] { StoreFiles.Events, StoreFiles.Bodies, StoreFiles.Subscriptions, StoreFiles.Nudges })
            Directory.CreateDirectory(Path.Combine(root, folder));
    }

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
            var day = Day(ms);
            var body = $"{StoreFiles.Bodies}/{day}/{id}.json";
            Files.WriteJson(Path.Combine(root, body), post.Body);
            var e = new Event(id, Times.Format(clock.GetUtcNow()), post.Session, post.Kind, post.From, post.To, Subjects.Of(post.Kind, post.Body), body, ToElements(post.Refs))
            {
                Thread = post.Thread,
                Needs = post.Needs,
            };
            Files.WriteJson(Path.Combine(root, StoreFiles.Events, day, $"{id}.json"), e);
            bodies[e.Body] = JsonSerializer.SerializeToElement(post.Body, StoreJson.Options);
            Add(e);
            return e;
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
            var (id, ms) = NewId();
            var allRefs = new Dictionary<string, object?>(refs ?? new Dictionary<string, object?>()) { [RefKeys.Of] = original.Id };
            var e = original with
            {
                Id = id,
                At = Times.Format(clock.GetUtcNow()),
                To = to,
                Refs = ToElements(allRefs),
                Needs = needs,
            };
            Files.WriteJson(Path.Combine(root, StoreFiles.Events, Day(ms), $"{id}.json"), e);
            Add(e);
            return e;
        }
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
    /// First answer wins between processes too: before an answer is posted, its writer creates claims/answer/&lt;thread&gt;
    /// with an exclusive create. A second writer gets the first answer in the error text.
    /// </summary>
    void Claim(PostInput post)
    {
        if (post.Kind != EventKinds.Answer || post.Thread is not { } thread) return;
        var path = Path.Combine(root, StoreFiles.Claims, "answer", thread);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var claim = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            claim.Write(System.Text.Encoding.UTF8.GetBytes(post.From));
        }
        catch (IOException) when (File.Exists(path))
        {
            for (var attempt = 0; attempt < 20 && FirstAnswer(thread) is null; attempt++) System.Threading.Thread.Sleep(50);
            var first = FirstAnswer(thread);
            var text = first is null ? "(the answer is not written yet)" : Fields(first).GetValueOrDefault("text");
            throw new MessageException($"decision {thread} already has an answer from {first?.From ?? Files.ReadText(path) ?? "another writer"}: {text}. The session has this answer. Nothing changed.");
        }
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

    /// <summary>The envelopes of one sender (the full party), in id order.</summary>
    public IReadOnlyList<Event> FromParty(string party) => Index(byFrom, party);

    /// <summary>The newest envelope of one sender and one kind that is not a forward.</summary>
    public Event? LastFrom(string party, string kind)
    {
        lock (gate)
        {
            Refresh();
            if (!byFrom.TryGetValue(party, out var list)) return null;
            for (var i = list.Count - 1; i >= 0; i--)
                if (list[i].Kind == kind && !list[i].IsForward) return list[i];
            return null;
        }
    }

    /// <summary>The acks and the forwards of one envelope.</summary>
    public IReadOnlyList<Event> OfEnvelope(string id) => Index(byOf, id);

    public IReadOnlyList<Event> AcksOf(string id) => OfEnvelope(id).Where(e => e.Kind == EventKinds.Ack).ToList();

    public IReadOnlyList<Event> ForwardsOf(string id) => OfEnvelope(id).Where(e => e.Kind != EventKinds.Ack).ToList();

    public bool IsAcked(string id) => AcksOf(id).Count > 0;

    /// <summary>The first answer in a thread that is not a forward.</summary>
    public Event? FirstAnswer(string thread) => Thread(thread).FirstOrDefault(e => e.Kind == EventKinds.Answer && !e.IsForward);

    IReadOnlyList<Event> Index(Dictionary<string, List<Event>> index, string key)
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
            var body = Files.ReadJson<JsonElement>(Path.Combine(root, e.Body));
            if (body.ValueKind == JsonValueKind.Undefined) return null;
            bodies[e.Body] = body;
            return body;
        }
    }

    public Dictionary<string, object?> Fields(Event e) => Body(e) is { } body ? Render.Fields(body) : [];

    // 5. Nudge

    /// <summary>A wake-up signal with no content: the receiver looks at the stream.</summary>
    public void Nudge(string to, string? id = null) =>
        Files.Write(Path.Combine(root, StoreFiles.Nudges, Parties.Folder(to)), id ?? "");

    public DateTimeOffset? Nudged(string party)
    {
        var path = Path.Combine(root, StoreFiles.Nudges, Parties.Folder(party));
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    // 2. Subscribe

    public StreamSubscription Subscribe(string party, string name, Func<Event, bool> filter, string? description = null) =>
        new(this, Path.Combine(root, StoreFiles.Subscriptions, Parties.Folder(party), StoreFiles.Json(name)), party, name, filter, description, clock);

    // The cache

    void Refresh()
    {
        var folder = Path.Combine(root, StoreFiles.Events);
        if (!Directory.Exists(folder)) return;
        var days = Files.Names(folder);
        var added = new List<Event>();
        foreach (var day in days)
        {
            if (closedDays.Contains(day)) continue;
            foreach (var name in Files.Names(Path.Combine(folder, day)))
            {
                var id = Path.GetFileNameWithoutExtension(name);
                if (byId.ContainsKey(id)) continue;
                if (Files.ReadJson<Event>(Path.Combine(folder, day, name)) is { } e) added.Add(EventKinds.Current(e));
            }
        }
        // A day closes when two newer days exist: a late writer can still add a file to yesterday.
        foreach (var day in days.Take(Math.Max(0, days.Count - 2))) closedDays.Add(day);
        if (added.Count == 0) return;
        foreach (var e in added) Add(e);
    }

    void Add(Event e)
    {
        if (!byId.TryAdd(e.Id, e)) return;
        Insert(events, e);
        if (e.Of is { } of) Insert(Bucket(byOf, of), e);
        if (e.Reply is { } reply) Insert(Bucket(byReply, reply), e);
        if (e.Thread is { } thread) Insert(Bucket(byThread, thread), e);
        Insert(Bucket(byKind, e.Kind), e);
        Insert(Bucket(byFrom, e.From), e);
        if (long.TryParse(e.Id.Split('-')[0], out var ms) && e.Id.EndsWith($"-{writer}") && ms > lastMs) lastMs = ms;
    }

    static List<Event> Bucket(Dictionary<string, List<Event>> index, string key)
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

    int LowerBound(string start)
    {
        int low = 0, high = events.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Names.Order.Compare(events[mid].Id, start) <= 0) low = mid + 1;
            else high = mid;
        }
        return low;
    }

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

    sealed record State(string Party, string Name, string? Cursor, List<string> Recent, string At)
    {
        public string? Filter { get; init; }
    }

    readonly EventStream stream;
    readonly string path;
    readonly Func<Event, bool> filter;
    readonly TimeProvider clock;

    internal StreamSubscription(EventStream stream, string path, string party, string name, Func<Event, bool> filter, string? description, TimeProvider clock)
    {
        this.stream = stream;
        this.path = path;
        this.filter = filter;
        this.clock = clock;
        Party = party;
        Name = name;
        Description = description;
    }

    public string Party { get; }
    public string Name { get; }
    public string? Description { get; }
    public string Path => path;

    public string? Cursor => Load().Cursor;

    public IReadOnlyList<Event> Next()
    {
        var state = Load();
        var recent = state.Recent.ToHashSet();
        var start = state.Cursor is { } cursor ? Minus(cursor, Grace) : null;
        return stream.After(start).Where(e => !recent.Contains(e.Id) && filter(e)).ToList();
    }

    public void Done(string id) => Done([id]);

    public void Done(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return;
        using var held = Hold();
        var state = Load();
        var cursor = state.Cursor;
        var recent = state.Recent.ToHashSet();
        foreach (var id in list)
        {
            recent.Add(id);
            if (cursor is null || Names.Order.Compare(id, cursor) > 0) cursor = id;
        }
        var floor = Minus(cursor!, Grace);
        var kept = recent.Where(id => Names.Order.Compare(id, floor) > 0).Order(Names.Order).ToList();
        Files.WriteJson(path, new State(Party, Name, cursor, kept, Times.Format(clock.GetUtcNow())) { Filter = Description });
    }

    State Load() => Files.ReadJson<State>(path) ?? new State(Party, Name, null, [], "");

    /// <summary>
    /// The lock of the read, merge and write in <see cref="Done(IEnumerable{string})"/>. The supervisor and orch watch
    /// write the same cursor. Without the lock, the last write loses the done ids of the other writer.
    /// </summary>
    FileStream Hold()
    {
        var file = path + ".lock";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var end = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (DateTime.UtcNow < end)
            {
                System.Threading.Thread.Sleep(10);
            }
        }
    }

    /// <summary>The longest wait for the lock of the cursor file. After it, the write fails with an IOException.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    static string Minus(string id, TimeSpan by) =>
        long.TryParse(id.Split('-')[0], out var ms) ? Math.Max(0, ms - (long)by.TotalMilliseconds).ToString() : id;
}
