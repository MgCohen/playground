using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;

namespace Orchestrator.Store.Remote.Notion;

/// <summary>
/// The store in Notion (N1 spike). The root is <c>notion:&lt;page id&gt;</c>. The first write makes four linked
/// databases and one log page under that page:
/// <list type="bullet">
/// <item><c>Events</c>: one row for each envelope (the stream). Relations: <c>From</c>, <c>To</c> and <c>Session</c> to
/// <c>Sessions</c>, <c>Body</c> to <c>Bodies</c>, <c>Reply</c> to <c>Events</c>.</item>
/// <item><c>Bodies</c>: one row for each body. The page content shows the fields. Relation <c>Event</c>, rollup <c>Session</c>.</item>
/// <item><c>Sessions</c>: one row for each party (<c>user</c>, <c>supervisor</c>, <c>session:&lt;id&gt;</c>). A session row
/// holds the registry entry. Relations <c>Inbox</c>, <c>Sent</c>, <c>Events</c> (the duals of the event relations) and <c>Parent</c>.</item>
/// <item><c>Docs</c>: the run state (cursors, leases, claims, nudges, policy, templates, runs). Title: <c>kind|scope|id</c>.</item>
/// </list>
/// Each row keeps the document text in <c>Text</c>. The version is the hash of that text, as in the local folder.
/// Notion has no conditional write. The rule <c>Exists</c> is a read before the create and a read after it (the
/// oldest row wins). The rule <c>Stale</c> is a read before the write: two writers in the same second can both win.
/// </summary>
public sealed class NotionTransport : IStoreTransport
{
    public const string Scheme = "notion:";
    const string EventsTitle = "Events";
    const string BodiesTitle = "Bodies";
    const string SessionsTitle = "Sessions";
    const string DocsTitle = "Docs";
    const string LogTitle = "Log";
    const string SessionParty = "session:";

    /// <summary>The look-back of a feed or a table sync: rows of other writers with a clock that is late by less than this.</summary>
    public static TimeSpan Window { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The time between two reads of a table cache and of the feed. A read in this time uses the cache. A write always reads fresh.</summary>
    public static TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(2);

    readonly NotionClient api;
    readonly string page;
    readonly Lock gate = new();
    readonly string epoch = Guid.NewGuid().ToString("N");

    readonly Table bodies = new(BodiesTitle, "Path");
    readonly Table sessions = new(SessionsTitle, "Party");
    readonly Table docs = new(DocsTitle, "Key");
    string? eventsDb, logPage;

    // The feed: the envelopes in arrival order, and the page of each envelope for the Reply relation.
    readonly List<WireDoc> feed = [];
    readonly Dictionary<string, WireDoc> feedById = [];
    readonly Dictionary<string, string> eventPages = [];
    long feedMs;
    DateTimeOffset lastFeed = DateTimeOffset.MinValue;

    sealed record Row(string PageId, string Key, string Kind, string Scope, string Id, string Text, bool Removed, DateTimeOffset Created);

    /// <summary>One database with a cache of its rows by title. A sync reads the rows that changed since the last sync.</summary>
    sealed class Table(string title, string titleProperty)
    {
        public string Title { get; } = title;
        public string TitleProperty { get; } = titleProperty;
        public string? Db { get; set; }
        public Dictionary<string, Row> Rows { get; } = [];
        public DateTimeOffset? Since { get; set; }
        public DateTimeOffset Last { get; set; } = DateTimeOffset.MinValue;
        public IEnumerable<Row> Live => Rows.Values.Where(r => !r.Removed);
    }

    public NotionTransport(string root, string token, HttpClient? http = null)
    {
        page = PageOf(root);
        api = new NotionClient(token, http);
        Location = root;
    }

    public static bool IsRoot(string root) => root.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page id of a root: <c>notion:&lt;id&gt;</c>, with or without dashes, or a Notion page URL.</summary>
    public static string PageOf(string root)
    {
        var text = root[Scheme.Length..].Trim().TrimEnd('/');
        var last = text.Split('/', '-', '?', '#').Where(p => p.Length > 0).Select(p => p.Replace("-", "")).LastOrDefault(p => p.Length == 32 && p.All(Uri.IsHexDigit));
        var compact = text.Replace("-", "");
        return last ?? (compact.Length == 32 && compact.All(Uri.IsHexDigit) ? compact : throw new ArgumentException($"{root} names no Notion page. Use notion:<page id>."));
    }

    public string Location { get; }

    public NotionClient Api => api;

    public WireInfo Info()
    {
        lock (gate) return new(Location, Find(), StoreVersion.Current);
    }

    public void Prepare()
    {
        lock (gate) Need();
    }

    public void Log(string line)
    {
        lock (gate)
        {
            if (!Find() || logPage is null) return;
            api.AppendParagraph(logPage, line);
        }
    }

    public WireDoc? Get(WireScope scope, string id)
    {
        lock (gate)
        {
            if (!Find()) return null;
            if (scope.Kind == StoreWire.Events)
            {
                ReadFeed();
                return feedById.GetValueOrDefault(id);
            }
            if (scope.Kind == StoreWire.Sessions) return SessionIds().Contains(id) ? SessionDoc(id) : null;
            var (table, key) = Place(scope, id);
            Sync(table);
            return table.Rows.TryGetValue(key, out var row) && Present(table, row) ? new WireDoc(id, Versions.Of(row.Text), row.Text) : null;
        }
    }

    public List<WireDoc> List(WireScope scope)
    {
        lock (gate)
        {
            if (!Find()) return [];
            if (scope.Kind == StoreWire.Events)
            {
                ReadFeed();
                return [.. feed];
            }
            if (scope.Kind == StoreWire.Sessions) return SessionIds().Order(StringComparer.Ordinal).Select(SessionDoc).ToList();
            var (table, _) = Place(scope, "");
            Sync(table);
            return table.Rows.Values.Where(r => Present(table, r) && Member(table, scope, r))
                .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new WireDoc(r.Id, Versions.Of(r.Text), r.Text)).ToList();
        }
    }

    public WireSaved Save(WireScope scope, WireSave save)
    {
        lock (gate)
        {
            Need();
            return scope.Kind switch
            {
                StoreWire.Events => SaveEvents(save),
                StoreWire.Sessions => SaveSessions(save),
                _ => SaveDocs(scope, save),
            };
        }
    }

    public WireFeed Feed(int from)
    {
        lock (gate)
        {
            if (!Find()) return new(epoch, 0, []);
            ReadFeed();
            var start = Math.Clamp(from, 0, feed.Count);
            return new(epoch, feed.Count, feed.GetRange(start, feed.Count - start));
        }
    }

    // ---- places ----

    /// <summary>The table and the row title of a document: a body, a session entry, or a document of the run state.</summary>
    (Table Table, string Key) Place(WireScope scope, string id) => scope.Kind switch
    {
        StoreWire.Bodies => (bodies, id),
        StoreWire.SessionEntries => (sessions, SessionParty + id),
        _ => (docs, $"{scope.Kind}|{scope.Query()}|{id}"),
    };

    /// <summary>A session row without an entry is a party that only sends or gets envelopes: no document.</summary>
    bool Present(Table table, Row row) => !row.Removed && (table != sessions || row.Text.Length > 0);

    bool Member(Table table, WireScope scope, Row row) =>
        table == docs ? row.Kind == scope.Kind && row.Scope == scope.Query()
        : table != sessions || row.Key.StartsWith(SessionParty, StringComparison.Ordinal);

    // ---- envelopes ----

    WireSaved SaveEvents(WireSave save)
    {
        if (save.Changes.Count > 0)
            throw new StoreConflictException(StoreConflict.Stale, save.Changes[0].Id, "the envelopes are append only");
        var gone = save.Removes.Select(r => (r.Id, Rows: QueryTitle(eventsDb!, "Id", r.Id))).ToList();
        foreach (var (id, rows) in gone)
            if (rows.Count == 0 && save.Removes.First(r => r.Id == id).Version != 0)
                throw new StoreConflictException(StoreConflict.Stale, id, $"{id} changed after the read (version 0). Nothing changed.");
        foreach (var add in save.Adds)
            if (feedById.ContainsKey(add.Id) || QueryTitle(eventsDb!, "Id", add.Id).Count > 0)
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"the envelope {add.Id} exists. Nothing changed.");
        var versions = new Dictionary<string, long>();
        foreach (var (id, rows) in gone)
        {
            foreach (var row in rows) api.Archive(NotionClient.IdOf(row));
            if (feedById.Remove(id, out var doc)) feed.Remove(doc);
            eventPages.Remove(id);
            versions[id] = 0;
        }
        var once = new HashSet<string>();
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            var pageId = api.CreateRow(eventsDb!, EventProperties(add));
            var doc = add with { Version = Versions.Of(add.Text) };
            Remember(doc, pageId);
            versions[add.Id] = doc.Version;
        }
        return new(versions);
    }

    /// <summary>A second add of one id in one save fails after the first add, as in the local folder.</summary>
    static void Once(HashSet<string> added, string id)
    {
        if (!added.Add(id)) throw new StoreConflictException(StoreConflict.Exists, id, $"the save adds {id} two times. The first add is saved.");
    }

    /// <summary>Reads the new envelopes into the feed: one query each <see cref="SyncInterval"/>, for all readers.</summary>
    void ReadFeed()
    {
        var clock = DateTimeOffset.UtcNow;
        if (clock - lastFeed < SyncInterval) return;
        lastFeed = clock;
        var since = feedMs == 0 ? 0 : feedMs - (long)Window.TotalMilliseconds;
        var filter = new JsonObject { ["property"] = "Ms", ["number"] = new JsonObject { ["greater_than_or_equal_to"] = since } };
        var sorts = new JsonArray(new JsonObject { ["property"] = "Ms", ["direction"] = "ascending" });
        var rows = api.Query(eventsDb!, filter, sorts)
            .Select(r => (Doc: EventDoc(r), Page: NotionClient.IdOf(r)))
            .Where(d => !feedById.ContainsKey(d.Doc.Id)).OrderBy(d => d.Doc.Id, StringComparer.Ordinal).ToList();
        foreach (var (doc, pageId) in rows) Remember(doc, pageId);
    }

    void Remember(WireDoc doc, string pageId)
    {
        eventPages[doc.Id] = pageId;
        if (!feedById.TryAdd(doc.Id, doc)) return;
        feed.Add(doc);
        feedMs = Math.Max(feedMs, MsOf(doc.Id));
    }

    static WireDoc EventDoc(JsonObject row)
    {
        var text = NotionClient.ReadText(row, "Text");
        return new(NotionClient.ReadTitle(row, "Id"), Versions.Of(text), text);
    }

    static long MsOf(string id) => long.TryParse(id.Split('-')[0], out var ms) ? ms : 0;

    JsonObject EventProperties(WireDoc doc)
    {
        var e = Parse(doc.Text);
        var session = S(e, "session");
        var props = new JsonObject
        {
            ["Id"] = NotionClient.Title(doc.Id),
            ["Ms"] = new JsonObject { ["number"] = MsOf(doc.Id) },
            ["Subject"] = NotionClient.RichText(S(e, "subject")),
            ["Thread"] = NotionClient.RichText(S(e, "thread")),
            ["From"] = NotionClient.Relation(PartyPage(S(e, "from"))),
            ["To"] = NotionClient.Relation(PartyPage(S(e, "to"))),
            ["Session"] = NotionClient.Relation(session.Length > 0 ? PartyPage(SessionParty + session) : null),
            ["Body"] = NotionClient.Relation(BodyPage(S(e, "body"))),
            ["Reply"] = NotionClient.Relation(e["refs"]?["reply"] is JsonValue reply && reply.GetValueKind() == JsonValueKind.String ? eventPages.GetValueOrDefault(reply.GetValue<string>()) : null),
            ["Text"] = NotionClient.RichText(doc.Text),
        };
        if (S(e, "kind") is { Length: > 0 } kind) props["Kind"] = NotionClient.Select(kind);
        if (S(e, "needs") is { Length: > 0 } needs) props["Needs"] = NotionClient.Select(needs);
        if (S(e, "stream") is { Length: > 0 } stream) props["Stream"] = NotionClient.Select(stream);
        if (DateTimeOffset.TryParse(S(e, "at"), out var at)) props["At"] = new JsonObject { ["date"] = new JsonObject { ["start"] = at.ToString("o") } };
        return props;
    }

    /// <summary>The page of a body path. The body is written before its envelope, so it is in the cache or in Notion.</summary>
    string? BodyPage(string path)
    {
        if (path.Length == 0) return null;
        if (bodies.Rows.TryGetValue(path, out var row)) return row.PageId;
        return ReadRow(bodies, path)?.PageId;
    }

    /// <summary>The row of a party in <c>Sessions</c>. The first envelope of a party makes its row.</summary>
    string? PartyPage(string party)
    {
        if (party.Length == 0) return null;
        if (sessions.Rows.TryGetValue(party, out var known)) return known.PageId;
        if (ReadRow(sessions, party) is { } row)
        {
            sessions.Rows[party] = row;
            return row.PageId;
        }
        var isSession = party.StartsWith(SessionParty, StringComparison.Ordinal);
        var id = isSession ? party[SessionParty.Length..] : party;
        var props = new JsonObject { ["Party"] = NotionClient.Title(party), ["Kind"] = NotionClient.Select(isSession ? "session" : party), ["Id"] = NotionClient.RichText(id) };
        var pageId = api.CreateRow(sessions.Db!, props);
        sessions.Rows[party] = new Row(pageId, party, isSession ? "session" : party, "", id, "", false, DateTimeOffset.UtcNow);
        return pageId;
    }

    // ---- sessions ----

    // The local folder has a session when its folder sessions/<id>/ exists. Here a session exists when it has a registry
    // entry in Sessions, a session file or a session item in Docs, or a row of kind sessions in Docs.
    HashSet<string> SessionIds()
    {
        Sync(sessions);
        Sync(docs);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in sessions.Rows.Values.Where(r => Present(sessions, r) && r.Key.StartsWith(SessionParty, StringComparison.Ordinal))) ids.Add(row.Id);
        foreach (var row in docs.Live)
        {
            if (row.Kind is StoreWire.Sessions or StoreWire.SessionFile) ids.Add(row.Id);
            else if (row.Kind == StoreWire.SessionItems && ScopeValue(row.Scope, "session") is { } session) ids.Add(session);
        }
        return ids;
    }

    static string? ScopeValue(string query, string key) =>
        query.Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == key).Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();

    static WireDoc SessionDoc(string id) => new(id, Versions.Of(id), id);

    WireSaved SaveSessions(WireSave save)
    {
        sessions.Last = docs.Last = DateTimeOffset.MinValue;
        var known = SessionIds();
        foreach (var add in save.Adds)
            if (known.Contains(add.Id)) throw new StoreConflictException(StoreConflict.Exists, add.Id, $"session {add.Id} exists. Nothing changed.");
        var versions = new Dictionary<string, long>();
        var once = new HashSet<string>();
        var scope = new WireScope(StoreWire.Sessions);
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            var key = Place(scope, add.Id).Key;
            WriteDoc(key, scope.Kind, scope.Query(), add.Id, add.Id, removed: false, ReadRow(docs, key));
            versions[add.Id] = Versions.Of(add.Id);
        }
        // A remove deletes the session folder: the registry entry and each row of the session in Docs.
        foreach (var remove in save.Removes)
        {
            if (sessions.Rows.GetValueOrDefault(SessionParty + remove.Id) is { } entry && Present(sessions, entry))
                WriteSession(remove.Id, "", removed: true, entry);
            foreach (var row in docs.Live.Where(r => r.Id == remove.Id && r.Kind is StoreWire.Sessions or StoreWire.SessionFile
                         || r.Kind == StoreWire.SessionItems && ScopeValue(r.Scope, "session") == remove.Id).ToList())
                WriteDoc(row.Key, row.Kind, row.Scope, row.Id, "", removed: true, row);
            versions[remove.Id] = 0;
        }
        return new(versions);
    }

    // ---- documents: bodies, session entries, run state ----

    WireSaved SaveDocs(WireScope scope, WireSave save)
    {
        var table = Place(scope, "").Table;
        // Check each rule with a fresh read before the first write, so a refused save writes nothing.
        var fresh = new Dictionary<string, Row?>();
        Row? Fresh(string id) => fresh.TryGetValue(id, out var r) ? r : fresh[id] = ReadRow(table, Place(scope, id).Key);
        foreach (var add in save.Adds)
            if (Fresh(add.Id) is { } row && Present(table, row))
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"{add.Id} exists. Nothing changed.");
        foreach (var change in save.Changes.Concat(save.Removes))
        {
            var row = Fresh(change.Id);
            var stored = row is not null && Present(table, row) ? Versions.Of(row.Text) : 0;
            if (stored != change.Version)
                throw new StoreConflictException(StoreConflict.Stale, change.Id, $"{change.Id} changed after the read (version {stored}, read {change.Version}). Nothing changed.");
        }

        var versions = new Dictionary<string, long>();
        var once = new HashSet<string>();
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            var row = Write(table, scope, add.Id, add.Text, removed: false, Fresh(add.Id));
            // The read after the create: when two writers made the same row, the oldest row wins.
            var all = QueryTitle(table.Db!, table.TitleProperty, row.Key).Select(r => ToRow(table, r)).Where(r => Present(table, r)).ToList();
            if (all.Count > 1 && all.OrderBy(r => r.Created).ThenBy(r => r.PageId, StringComparer.Ordinal).First().PageId != row.PageId)
            {
                api.Archive(row.PageId);
                table.Rows.Remove(row.Key);
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"{add.Id} exists: another writer made it at the same time. Nothing changed.");
            }
            versions[add.Id] = Versions.Of(add.Text);
        }
        foreach (var change in save.Changes)
        {
            Write(table, scope, change.Id, change.Text, removed: false, Fresh(change.Id));
            versions[change.Id] = Versions.Of(change.Text);
        }
        foreach (var remove in save.Removes)
        {
            if (Fresh(remove.Id) is { } row) Write(table, scope, remove.Id, "", removed: true, row);
            versions[remove.Id] = 0;
        }
        return new(versions);
    }

    Row Write(Table table, WireScope scope, string id, string text, bool removed, Row? existing)
    {
        if (table == bodies) return WriteBody(id, text, removed, existing);
        if (table == sessions) return WriteSession(id, text, removed, existing);
        return WriteDoc(Place(scope, id).Key, scope.Kind, scope.Query(), id, text, removed, existing);
    }

    Row WriteDoc(string key, string kind, string scope, string id, string text, bool removed, Row? existing)
    {
        var props = new JsonObject
        {
            ["Key"] = NotionClient.Title(key),
            ["Kind"] = NotionClient.Select(kind),
            ["Scope"] = NotionClient.RichText(scope),
            ["Id"] = NotionClient.RichText(id),
            ["Text"] = NotionClient.RichText(text),
            ["Removed"] = new JsonObject { ["checkbox"] = removed },
        };
        return Keep(docs, existing, props, null, new Row("", key, kind, scope, id, text, removed, DateTimeOffset.UtcNow));
    }

    /// <summary>A body row. A new body gets its fields as page content, so a person reads it in Notion.</summary>
    Row WriteBody(string path, string text, bool removed, Row? existing)
    {
        var props = new JsonObject
        {
            ["Path"] = NotionClient.Title(path),
            ["Text"] = NotionClient.RichText(text),
            ["Removed"] = new JsonObject { ["checkbox"] = removed },
        };
        return Keep(bodies, existing, props, existing is null ? BodyBlocks(text) : null, new Row("", path, StoreWire.Bodies, "", path, text, removed, DateTimeOffset.UtcNow));
    }

    /// <summary>A session row with its registry entry: the entry text, and its main fields as properties.</summary>
    Row WriteSession(string id, string text, bool removed, Row? existing)
    {
        var party = SessionParty + id;
        var entry = Parse(text);
        var props = new JsonObject
        {
            ["Party"] = NotionClient.Title(party),
            ["Kind"] = NotionClient.Select("session"),
            ["Id"] = NotionClient.RichText(id),
            ["Title"] = NotionClient.RichText(S(entry, "title")),
            ["Code"] = NotionClient.RichText(S(entry, "code")),
            ["Text"] = NotionClient.RichText(text),
            ["Removed"] = new JsonObject { ["checkbox"] = removed },
        };
        if (S(entry, "role") is { Length: > 0 } role) props["Role"] = NotionClient.Select(role);
        if (S(entry, "stream") is { Length: > 0 } stream) props["Stream"] = NotionClient.Select(stream);
        if (S(entry, "parent") is { Length: > 0 } parent && parent != id) props["Parent"] = NotionClient.Relation(PartyPage(SessionParty + parent));
        return Keep(sessions, existing ?? sessions.Rows.GetValueOrDefault(party), props, null, new Row("", party, "session", "", id, text, removed, DateTimeOffset.UtcNow));
    }

    Row Keep(Table table, Row? existing, JsonObject props, JsonArray? children, Row row)
    {
        var pageId = existing is null ? api.CreateRow(table.Db!, props, children) : api.UpdateRow(existing.PageId, props);
        var kept = row with { PageId = pageId, Created = existing?.Created ?? row.Created };
        table.Rows[row.Key] = kept;
        return kept;
    }

    /// <summary>One paragraph for each field of the body (at most 90).</summary>
    static JsonArray BodyBlocks(string text)
    {
        var blocks = new JsonArray();
        foreach (var (name, value) in Parse(text).Take(90))
            blocks.Add(NotionClient.Paragraph(name, value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>()
                : value is JsonArray list && list.All(i => i is JsonValue) ? string.Join(", ", list.Select(i => i!.ToString())) : value?.ToJsonString() ?? ""));
        return blocks;
    }

    Row? ReadRow(Table table, string key) =>
        QueryTitle(table.Db!, table.TitleProperty, key).Select(r => ToRow(table, r)).OrderBy(r => !Present(table, r)).ThenBy(r => r.Created).FirstOrDefault();

    /// <summary>Reads the rows that changed since the last sync into the cache. Notion keeps the edit time to the minute.</summary>
    void Sync(Table table)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - table.Last < SyncInterval) return;
        JsonObject? filter = table.Since is { } since
            ? new JsonObject { ["timestamp"] = "last_edited_time", ["last_edited_time"] = new JsonObject { ["on_or_after"] = (since - Window).ToString("o") } }
            : null;
        var start = api.ServerTime ?? now;
        var rows = api.Query(table.Db!, filter, null).Select(r => ToRow(table, r)).ToList();
        foreach (var group in rows.GroupBy(r => r.Key))
            table.Rows[group.Key] = group.OrderBy(r => !Present(table, r)).ThenBy(r => r.Created).First();
        table.Since = start;
        table.Last = now;
    }

    static Row ToRow(Table table, JsonObject r)
    {
        var key = NotionClient.ReadTitle(r, table.TitleProperty);
        var body = table.Title == BodiesTitle;
        var id = body ? key : NotionClient.ReadText(r, "Id");
        return new(NotionClient.IdOf(r), key, body ? StoreWire.Bodies : NotionClient.ReadSelect(r, "Kind"),
            NotionClient.ReadText(r, "Scope"), id.Length > 0 ? id : key, NotionClient.ReadText(r, "Text"), NotionClient.ReadCheck(r, "Removed"), NotionClient.CreatedOf(r));
    }

    List<JsonObject> QueryTitle(string db, string property, string title) =>
        api.Query(db, new JsonObject { ["property"] = property, ["title"] = new JsonObject { ["equals"] = title } }, null);

    static JsonObject Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static string S(JsonObject o, string name) => o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";

    // ---- layout ----

    bool Find()
    {
        if (Found) return true;
        foreach (var child in api.Children(page))
        {
            var type = child["type"]?.GetValue<string>();
            var title = child[type ?? ""]?["title"]?.GetValue<string>();
            var id = NotionClient.IdOf(child);
            if (type == "child_page" && title == LogTitle) logPage ??= id;
            if (type != "child_database") continue;
            if (title == EventsTitle) eventsDb ??= id;
            else if (title == BodiesTitle) bodies.Db ??= id;
            else if (title == SessionsTitle) sessions.Db ??= id;
            else if (title == DocsTitle) docs.Db ??= id;
        }
        return Found;
    }

    bool Found => eventsDb is not null && bodies.Db is not null && sessions.Db is not null && docs.Db is not null && logPage is not null;

    /// <summary>The first write makes the databases and their links, as the local folder makes its folders on the first write.</summary>
    void Need()
    {
        if (Find()) return;
        sessions.Db ??= api.CreateDatabase(page, SessionsTitle, SessionsSchema());
        bodies.Db ??= api.CreateDatabase(page, BodiesTitle, BodiesSchema());
        if (eventsDb is null)
        {
            var events = api.CreateDatabaseOf(page, EventsTitle, EventsSchema(sessions.Db, bodies.Db));
            eventsDb = NotionClient.IdOf(events);
            api.UpdateDatabase(eventsDb, new JsonObject { ["Reply"] = SelfRelation(eventsDb) });
            // Give the dual relations the names of the stream: Inbox, Sent, Events, Event.
            string Dual(string property) => events["properties"]![property]!["relation"]!["dual_property"]!["synced_property_name"]!.GetValue<string>();
            api.UpdateDatabase(sessions.Db, new JsonObject
            {
                [Dual("To")] = new JsonObject { ["name"] = "Inbox" },
                [Dual("From")] = new JsonObject { ["name"] = "Sent" },
                [Dual("Session")] = new JsonObject { ["name"] = "Events" },
                ["Parent"] = SelfRelation(sessions.Db),
            });
            api.UpdateDatabase(bodies.Db, new JsonObject { [Dual("Body")] = new JsonObject { ["name"] = "Event" } });
            api.UpdateDatabase(bodies.Db, new JsonObject
            {
                ["Session"] = new JsonObject { ["rollup"] = new JsonObject { ["relation_property_name"] = "Event", ["rollup_property_name"] = "Session", ["function"] = "show_original" } },
            });
        }
        docs.Db ??= api.CreateDatabase(page, DocsTitle, DocsSchema());
        logPage ??= api.CreatePage(page, LogTitle);
    }

    static JsonObject Text() => new() { ["rich_text"] = new JsonObject() };

    static JsonObject Pick() => new() { ["select"] = new JsonObject() };

    static JsonObject Check() => new() { ["checkbox"] = new JsonObject() };

    static JsonObject DualRelation(string db) => new() { ["relation"] = new JsonObject { ["database_id"] = db, ["type"] = "dual_property", ["dual_property"] = new JsonObject() } };

    static JsonObject SelfRelation(string db) => new() { ["relation"] = new JsonObject { ["database_id"] = db, ["type"] = "single_property", ["single_property"] = new JsonObject() } };

    static JsonObject EventsSchema(string sessionsDb, string bodiesDb) => new()
    {
        ["Id"] = new JsonObject { ["title"] = new JsonObject() },
        ["Kind"] = Pick(),
        ["Subject"] = Text(),
        ["From"] = DualRelation(sessionsDb),
        ["To"] = DualRelation(sessionsDb),
        ["Session"] = DualRelation(sessionsDb),
        ["Body"] = DualRelation(bodiesDb),
        ["Needs"] = Pick(),
        ["Thread"] = Text(),
        ["Stream"] = Pick(),
        ["At"] = new JsonObject { ["date"] = new JsonObject() },
        ["Ms"] = new JsonObject { ["number"] = new JsonObject() },
        ["Text"] = Text(),
    };

    static JsonObject BodiesSchema() => new()
    {
        ["Path"] = new JsonObject { ["title"] = new JsonObject() },
        ["Removed"] = Check(),
        ["Text"] = Text(),
    };

    static JsonObject SessionsSchema() => new()
    {
        ["Party"] = new JsonObject { ["title"] = new JsonObject() },
        ["Kind"] = Pick(),
        ["Id"] = Text(),
        ["Title"] = Text(),
        ["Code"] = Text(),
        ["Role"] = Pick(),
        ["Stream"] = Pick(),
        ["Removed"] = Check(),
        ["Text"] = Text(),
    };

    static JsonObject DocsSchema() => new()
    {
        ["Key"] = new JsonObject { ["title"] = new JsonObject() },
        ["Kind"] = Pick(),
        ["Scope"] = Text(),
        ["Id"] = Text(),
        ["Removed"] = Check(),
        ["Text"] = Text(),
    };
}
