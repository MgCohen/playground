using System.Text.Json.Nodes;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;

namespace Orchestrator.Store.Remote.Notion;

/// <summary>
/// The store in Notion (N1 spike). The root is <c>notion:&lt;page id&gt;</c>. Under that page, <see cref="Prepare"/>
/// makes two databases and one log page:
/// <list type="bullet">
/// <item><c>Stream</c>: one row for each envelope, with the envelope fields as properties. The user reads the stream there.</item>
/// <item><c>Docs</c>: one row for each other document (bodies, sessions, cursors, leases, claims...). Title: <c>kind|scope|id</c>.</item>
/// <item><c>Log</c>: a page with one paragraph for each line of the supervisor log.</item>
/// </list>
/// Each row keeps the document text in the <c>Text</c> property. Its version is the hash of the text, as in the local folder.
/// Notion has no conditional write. The rule <c>Exists</c> is a read before the create and a read after it (the
/// oldest row wins). The rule <c>Stale</c> is a read before the write: two writers in the same second can both win.
/// </summary>
public sealed class NotionTransport : IStoreTransport
{
    public const string Scheme = "notion:";
    const string StreamTitle = "Stream";
    const string DocsTitle = "Docs";
    const string LogTitle = "Log";

    /// <summary>The look-back of a feed or a docs sync: rows of other writers with a clock that is late by less than this.</summary>
    public static TimeSpan Window { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The time between two reads of the docs cache and of the feed. A read in this time uses the cache. A write always reads fresh.</summary>
    public static TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(2);

    readonly NotionClient api;
    readonly string page;
    readonly Lock gate = new();
    readonly string epoch = Guid.NewGuid().ToString("N");

    string? streamDb, docsDb, logPage;

    // The docs cache: key (kind|scope|id) to row. A sync reads the rows that changed since the last sync.
    readonly Dictionary<string, Row> docs = [];
    DateTimeOffset? docsSince;
    DateTimeOffset lastSync = DateTimeOffset.MinValue;

    // The feed: the envelopes in arrival order.
    readonly List<WireDoc> feed = [];
    readonly Dictionary<string, WireDoc> feedById = [];
    long feedMs;
    DateTimeOffset lastFeed = DateTimeOffset.MinValue;

    sealed record Row(string PageId, string Kind, string Scope, string Id, long Version, string Text, bool Removed);

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
        lock (gate)
        {
            Need();
        }
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
            Sync();
            if (scope.Kind == StoreWire.Sessions) return SessionIds().Contains(id) ? SessionDoc(id) : null;
            return docs.TryGetValue(Key(scope, id), out var row) && !row.Removed ? new WireDoc(row.Id, row.Version, row.Text) : null;
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
            Sync();
            if (scope.Kind == StoreWire.Sessions) return SessionIds().Order(StringComparer.Ordinal).Select(SessionDoc).ToList();
            return docs.Values.Where(r => !r.Removed && r.Kind == scope.Kind && r.Scope == scope.Query())
                .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new WireDoc(r.Id, r.Version, r.Text)).ToList();
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
                StoreWire.Sessions => SaveSessions(scope, save),
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

    // ---- envelopes ----

    WireSaved SaveEvents(WireSave save)
    {
        if (save.Changes.Count > 0)
            throw new StoreConflictException(StoreConflict.Stale, save.Changes[0].Id, "the envelopes are append only");
        var gone = save.Removes.Select(r => (r.Id, Rows: QueryTitle(streamDb!, r.Id))).ToList();
        foreach (var (id, rows) in gone)
            if (rows.Count == 0 && save.Removes.First(r => r.Id == id).Version != 0)
                throw new StoreConflictException(StoreConflict.Stale, id, $"{id} changed after the read (version 0). Nothing changed.");
        foreach (var add in save.Adds)
            if (feedById.ContainsKey(add.Id) || QueryTitle(streamDb!, add.Id).Count > 0)
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"the envelope {add.Id} exists. Nothing changed.");
        var versions = new Dictionary<string, long>();
        foreach (var (id, rows) in gone)
        {
            foreach (var row in rows) api.Archive(NotionClient.IdOf(row));
            if (feedById.Remove(id, out var doc)) feed.Remove(doc);
            versions[id] = 0;
        }
        var once = new HashSet<string>();
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            api.CreateRow(streamDb!, EventProperties(add));
            var doc = add with { Version = Versions.Of(add.Text) };
            Remember(doc);
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
    void ReadFeed(bool now = false)
    {
        var clock = DateTimeOffset.UtcNow;
        if (!now && clock - lastFeed < SyncInterval) return;
        lastFeed = clock;
        var since = feedMs == 0 ? 0 : feedMs - (long)Window.TotalMilliseconds;
        var filter = new JsonObject { ["property"] = "Ms", ["number"] = new JsonObject { ["greater_than_or_equal_to"] = since } };
        var sorts = new JsonArray(new JsonObject { ["property"] = "Ms", ["direction"] = "ascending" });
        var rows = api.Query(streamDb!, filter, sorts).Select(EventDoc).Where(d => !feedById.ContainsKey(d.Id)).OrderBy(d => d.Id, StringComparer.Ordinal);
        foreach (var doc in rows) Remember(doc);
    }

    void Remember(WireDoc doc)
    {
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

    static JsonObject EventProperties(WireDoc doc)
    {
        var e = JsonNode.Parse(doc.Text) as JsonObject ?? [];
        string S(string name) => e[name]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? e[name]!.GetValue<string>() : "";
        var props = new JsonObject
        {
            ["Id"] = NotionClient.Title(doc.Id),
            ["Ms"] = new JsonObject { ["number"] = MsOf(doc.Id) },
            ["Subject"] = NotionClient.RichText(S("subject")),
            ["From"] = NotionClient.RichText(S("from")),
            ["To"] = NotionClient.RichText(S("to")),
            ["Session"] = NotionClient.RichText(S("session")),
            ["Thread"] = NotionClient.RichText(S("thread")),
            ["Body"] = NotionClient.RichText(S("body")),
            ["Reply"] = NotionClient.RichText(e["refs"]?["reply"]?.ToString() ?? ""),
            ["Text"] = NotionClient.RichText(doc.Text),
        };
        if (S("kind") is { Length: > 0 } kind) props["Kind"] = NotionClient.Select(kind);
        if (S("needs") is { Length: > 0 } needs) props["Needs"] = NotionClient.Select(needs);
        if (S("stream") is { Length: > 0 } stream) props["Stream"] = NotionClient.Select(stream);
        if (DateTimeOffset.TryParse(S("at"), out var at)) props["At"] = new JsonObject { ["date"] = new JsonObject { ["start"] = at.ToString("o") } };
        return props;
    }

    // ---- sessions ----

    // The local folder has a session when its folder sessions/<id>/ exists. Here a session exists when a row of the
    // session exists: its registry entry, a session file, a session item, or a row of kind sessions.
    HashSet<string> SessionIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in docs.Values.Where(r => !r.Removed))
        {
            if (row.Kind is StoreWire.Sessions or StoreWire.SessionEntries or StoreWire.SessionFile) ids.Add(row.Id);
            else if (row.Kind == StoreWire.SessionItems && ScopeValue(row.Scope, "session") is { } session) ids.Add(session);
        }
        return ids;
    }

    static string? ScopeValue(string query, string key) =>
        query.Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == key).Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();

    static WireDoc SessionDoc(string id) => new(id, Versions.Of(id), id);

    WireSaved SaveSessions(WireScope scope, WireSave save)
    {
        lastSync = DateTimeOffset.MinValue;
        Sync();
        var known = SessionIds();
        foreach (var add in save.Adds)
            if (known.Contains(add.Id)) throw new StoreConflictException(StoreConflict.Exists, add.Id, $"session {add.Id} exists. Nothing changed.");
        var versions = new Dictionary<string, long>();
        var once = new HashSet<string>();
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            Write(scope, add.Id, add.Id, Versions.Of(add.Id), removed: false, ReadRow(scope, add.Id));
            versions[add.Id] = Versions.Of(add.Id);
        }
        // A remove deletes the session folder: each row of the session.
        foreach (var remove in save.Removes)
        {
            foreach (var row in docs.Values.Where(r => !r.Removed && (r.Id == remove.Id && r.Kind is StoreWire.Sessions or StoreWire.SessionEntries or StoreWire.SessionFile
                         || r.Kind == StoreWire.SessionItems && ScopeValue(r.Scope, "session") == remove.Id)).ToList())
                Write(new WireScope(row.Kind), row.Id, "", 0, removed: true, row, row.Scope);
            versions[remove.Id] = 0;
        }
        return new(versions);
    }

    // ---- other documents ----

    WireSaved SaveDocs(WireScope scope, WireSave save)
    {
        // Check each rule with a fresh read before the first write, so a refused save writes nothing.
        var fresh = new Dictionary<string, Row?>();
        Row? Fresh(string id) => fresh.TryGetValue(id, out var r) ? r : fresh[id] = ReadRow(scope, id);
        foreach (var add in save.Adds)
            if (Fresh(add.Id) is { Removed: false })
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"{add.Id} exists. Nothing changed.");
        foreach (var change in save.Changes.Concat(save.Removes))
        {
            var row = Fresh(change.Id);
            var stored = row is { Removed: false } ? row.Version : 0;
            if (stored != change.Version)
                throw new StoreConflictException(StoreConflict.Stale, change.Id, $"{change.Id} changed after the read (version {stored}, read {change.Version}). Nothing changed.");
        }

        var versions = new Dictionary<string, long>();
        var once = new HashSet<string>();
        foreach (var add in save.Adds)
        {
            Once(once, add.Id);
            var version = Versions.Of(add.Text);
            var row = Write(scope, add.Id, add.Text, version, removed: false, Fresh(add.Id));
            // The read after the create: when two writers made the same key, the oldest row wins.
            var all = QueryTitle(docsDb!, Key(scope, add.Id)).Where(r => !NotionClient.ReadCheck(r, "Removed")).ToList();
            if (all.Count > 1 && NotionClient.IdOf(all.OrderBy(NotionClient.CreatedOf).ThenBy(NotionClient.IdOf, StringComparer.Ordinal).First()) != row.PageId)
            {
                api.Archive(row.PageId);
                docs.Remove(Key(scope, add.Id));
                throw new StoreConflictException(StoreConflict.Exists, add.Id, $"{add.Id} exists: another writer made it at the same time. Nothing changed.");
            }
            versions[add.Id] = version;
        }
        foreach (var change in save.Changes)
        {
            var version = Versions.Of(change.Text);
            Write(scope, change.Id, change.Text, version, removed: false, Fresh(change.Id));
            versions[change.Id] = version;
        }
        foreach (var remove in save.Removes)
        {
            if (Fresh(remove.Id) is { } row) Write(scope, remove.Id, "", 0, removed: true, row);
            versions[remove.Id] = 0;
        }
        return new(versions);
    }

    Row Write(WireScope scope, string id, string text, long version, bool removed, Row? existing, string? scopeQuery = null)
    {
        var query = scopeQuery ?? scope.Query();
        var props = new JsonObject
        {
            ["Key"] = NotionClient.Title(Key(scope.Kind, query, id)),
            ["Kind"] = NotionClient.Select(scope.Kind),
            ["Scope"] = NotionClient.RichText(query),
            ["Id"] = NotionClient.RichText(id),
            ["Text"] = NotionClient.RichText(text),
            ["Removed"] = new JsonObject { ["checkbox"] = removed },
        };
        var pageId = existing is null ? api.CreateRow(docsDb!, props) : api.UpdateRow(existing.PageId, props);
        var row = new Row(pageId, scope.Kind, query, id, version, text, removed);
        docs[Key(scope.Kind, query, id)] = row;
        return row;
    }

    Row? ReadRow(WireScope scope, string id) =>
        QueryTitle(docsDb!, Key(scope, id)).OrderBy(NotionClient.CreatedOf).Select(ToRow).OrderBy(r => r.Removed).FirstOrDefault();

    /// <summary>Reads the rows that changed since the last sync into the cache. Notion keeps the edit time to the minute.</summary>
    void Sync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastSync < SyncInterval) return;
        JsonObject? filter = docsSince is { } since
            ? new JsonObject { ["timestamp"] = "last_edited_time", ["last_edited_time"] = new JsonObject { ["on_or_after"] = (since - Window).ToString("o") } }
            : null;
        var start = api.ServerTime ?? now;
        foreach (var row in api.Query(docsDb!, filter, null).Select(ToRow))
            docs[Key(row.Kind, row.Scope, row.Id)] = row;
        docsSince = api.ServerTime ?? start;
        lastSync = now;
    }

    /// <summary>A row of the docs database. The version is the hash of the text (a Notion number keeps only 53 bits).</summary>
    static Row ToRow(JsonObject r)
    {
        var text = NotionClient.ReadText(r, "Text");
        var removed = NotionClient.ReadCheck(r, "Removed");
        return new(NotionClient.IdOf(r), NotionClient.ReadSelect(r, "Kind"), NotionClient.ReadText(r, "Scope"), NotionClient.ReadText(r, "Id"), removed ? 0 : Versions.Of(text), text, removed);
    }

    static string Key(WireScope scope, string id) => Key(scope.Kind, scope.Query(), id);

    static string Key(string kind, string scope, string id) => $"{kind}|{scope}|{id}";

    List<JsonObject> QueryTitle(string db, string title)
    {
        var property = db == streamDb ? "Id" : "Key";
        return api.Query(db, new JsonObject { ["property"] = property, ["title"] = new JsonObject { ["equals"] = title } }, null);
    }

    // ---- layout ----

    bool Find()
    {
        if (streamDb is not null && docsDb is not null && logPage is not null) return true;
        foreach (var child in api.Children(page))
        {
            var type = child["type"]?.GetValue<string>();
            var title = child[type ?? ""]?["title"]?.GetValue<string>();
            var id = NotionClient.IdOf(child);
            if (type == "child_database" && title == StreamTitle) streamDb ??= id;
            else if (type == "child_database" && title == DocsTitle) docsDb ??= id;
            else if (type == "child_page" && title == LogTitle) logPage ??= id;
        }
        return streamDb is not null && docsDb is not null && logPage is not null;
    }

    /// <summary>The first write makes the databases, as the local folder makes its folders on the first write.</summary>
    void Need()
    {
        if (Find()) return;
        streamDb ??= api.CreateDatabase(page, StreamTitle, StreamSchema());
        docsDb ??= api.CreateDatabase(page, DocsTitle, DocsSchema());
        logPage ??= api.CreatePage(page, LogTitle);
    }

    static JsonObject StreamSchema() => new()
    {
        ["Id"] = new JsonObject { ["title"] = new JsonObject() },
        ["Kind"] = new JsonObject { ["select"] = new JsonObject() },
        ["Subject"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["From"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["To"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Needs"] = new JsonObject { ["select"] = new JsonObject() },
        ["Session"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Stream"] = new JsonObject { ["select"] = new JsonObject() },
        ["Thread"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Reply"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Body"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["At"] = new JsonObject { ["date"] = new JsonObject() },
        ["Ms"] = new JsonObject { ["number"] = new JsonObject() },
        ["Text"] = new JsonObject { ["rich_text"] = new JsonObject() },
    };

    static JsonObject DocsSchema() => new()
    {
        ["Key"] = new JsonObject { ["title"] = new JsonObject() },
        ["Kind"] = new JsonObject { ["select"] = new JsonObject() },
        ["Scope"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Id"] = new JsonObject { ["rich_text"] = new JsonObject() },
        ["Removed"] = new JsonObject { ["checkbox"] = new JsonObject() },
        ["Text"] = new JsonObject { ["rich_text"] = new JsonObject() },
    };
}
