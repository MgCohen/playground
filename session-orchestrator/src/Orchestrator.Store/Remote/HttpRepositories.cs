using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Sessions;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Workflows;
using Orchestrator.Store.Local;

namespace Orchestrator.Store.Remote;

/// <summary>
/// The HTTP implementation of the store (H5): each repository call goes to the store API of a supervisor
/// (<see cref="StoreWire.Prefix"/>). <c>orch</c> uses it when <c>ORCH_URL</c> is set, for example in a cloud worker.
/// The token goes in the header <see cref="TokenHeader"/>. The same rules apply as for the local folder store.
/// </summary>
public sealed class HttpRepositories : IRepositories
{
    public const string TokenHeader = "X-Orch-Token";

    /// <summary>The wait between two polls of <see cref="Watch"/>. The API has no push.</summary>
    public static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    readonly StoreClient client;
    readonly ConcurrentDictionary<(string File, Type Type), object> sessionFiles = new();

    public HttpRepositories(Uri url, string? token) : this(new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(30) }, token)
    {
    }

    /// <param name="http">The client with the base address of the supervisor. Tests pass the client of a test server.</param>
    public HttpRepositories(HttpClient http, string? token)
    {
        if (token is { Length: > 0 }) http.DefaultRequestHeaders.Add(TokenHeader, token);
        client = new StoreClient(http);
        Location = http.BaseAddress?.ToString().TrimEnd('/') ?? "";
        Events = new HttpEventRepository(client);
        Bodies = Repository(new(StoreWire.Bodies), JsonCodec<JsonElement>.Instance);
        Policies = Repository(new(StoreWire.Policies), PolicyCodec.Instance);
        StoreVersions = Repository(new(StoreWire.StoreVersions), JsonCodec<StoreVersion>.Instance);
        Claims = Repository(new(StoreWire.Claims), TextCodec.Instance);
        Cursors = Repository(new(StoreWire.Cursors), JsonCodec<CursorState>.Instance);
        Nudges = Repository(new(StoreWire.Nudges), TextCodec.Instance);
        Sessions = Repository(new(StoreWire.Sessions), TextCodec.Instance);
        SessionEntries = Repository(new(StoreWire.SessionEntries), JsonCodec<SessionEntry>.Instance);
        WorkflowRuns = Repository(new(StoreWire.WorkflowRuns), JsonCodec<WorkflowRun>.Instance);
        Log = new HttpLogSink(client);
    }

    public string Location { get; }
    public IRepository<Doc<Event>, string> Events { get; }
    public IRepository<Doc<JsonElement>, string> Bodies { get; }
    public IRepository<Doc<Core.Policy.Policy>, string> Policies { get; }
    public IRepository<Doc<StoreVersion>, string> StoreVersions { get; }
    public IRepository<Doc<string>, string> Claims { get; }
    public IRepository<Doc<CursorState>, string> Cursors { get; }
    public IRepository<Doc<string>, string> Nudges { get; }
    public IRepository<Doc<string>, string> Sessions { get; }
    public IRepository<Doc<SessionEntry>, string> SessionEntries { get; }
    public IRepository<Doc<WorkflowRun>, string> WorkflowRuns { get; }
    public ILogSink Log { get; }

    public IRepository<Doc<T>, string> SessionFile<T>(string file) =>
        (IRepository<Doc<T>, string>)sessionFiles.GetOrAdd((file, typeof(T)), _ => Repository(new WireScope(StoreWire.SessionFile, File: file), Codec<T>()));

    public IRepository<Doc<T>, string> SessionItems<T>(string session, string folder) =>
        Repository(new WireScope(StoreWire.SessionItems, Session: session, Folder: folder), Codec<T>());

    public IRepository<Doc<Definition>, string> Definitions(Catalog catalog) =>
        Repository(new WireScope(StoreWire.Definitions, Catalog: catalog.Name), DefinitionCodec.Instance);

    public bool Exists() => client.Send<WireInfo>(HttpMethod.Get, StoreWire.Prefix, null).Value!.Exists;

    public void Prepare() => client.Send<WireInfo>(HttpMethod.Post, $"{StoreWire.Prefix}/prepare", new { });

    public IStreamSignal Watch() => new PollSignal(PollInterval);

    HttpRepository<T> Repository<T>(WireScope scope, IFileCodec<T> codec) => new(client, scope, codec);

    static IFileCodec<T> Codec<T>() => typeof(T) == typeof(string) ? (IFileCodec<T>)(object)TextCodec.Instance : JsonCodec<T>.Instance;
}

/// <summary>The calls of the store API, with the errors as store exceptions.</summary>
sealed class StoreClient(HttpClient http)
{
    public (HttpStatusCode Status, T? Value) Send<T>(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null) request.Content = JsonContent.Create(body, options: StoreJson.Line);
        HttpResponseMessage response;
        try
        {
            response = http.SendAsync(request).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new IOException($"no store API answers at {http.BaseAddress}: {e.Message}. Check ORCH_URL and the supervisor.", e);
        }
        using (response)
        {
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new IOException($"the store API at {http.BaseAddress} refused the call ({(int)response.StatusCode}): {text}. Check ORCH_TOKEN.");
                case HttpStatusCode.Conflict:
                    var conflict = JsonSerializer.Deserialize<WireConflict>(text, StoreJson.Options)!;
                    throw new StoreConflictException(Enum.Parse<StoreConflict>(conflict.Conflict), conflict.Id, conflict.Message);
                case HttpStatusCode.NotFound when typeof(T) == typeof(WireDoc):
                    return (response.StatusCode, default);
                case HttpStatusCode.NoContent:
                    return (response.StatusCode, default);
            }
            if (!response.IsSuccessStatusCode)
                throw new IOException($"the store API at {http.BaseAddress} failed for {method} {path} ({(int)response.StatusCode}): {text}");
            return (response.StatusCode, JsonSerializer.Deserialize<T>(text, StoreJson.Options));
        }
    }
}

/// <summary>
/// One repository through the store API. It keeps the adds, the changes and the removes until the save, as the local
/// implementation does. The supervisor checks the versions and gives the new ones.
/// </summary>
class HttpRepository<T>(StoreClient client, WireScope scope, IFileCodec<T> codec) : IRepository<Doc<T>, string>
{
    readonly Lock gate = new();
    readonly List<Doc<T>> added = [];
    readonly List<Doc<T>> removed = [];
    readonly List<Doc<T>> changed = [];

    protected StoreClient Client { get; } = client;

    string Path(string action, params (string Key, string Value)[] more)
    {
        var query = string.Join('&', new[] { scope.Query() }.Concat(more.Select(m => $"{m.Key}={Uri.EscapeDataString(m.Value)}")).Where(q => q.Length > 0));
        return $"{StoreWire.Prefix}/{scope.Kind}/{action}{(query.Length > 0 ? "?" + query : "")}";
    }

    public Task<Doc<T>?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Client.Send<WireDoc>(HttpMethod.Get, Path("get", ("id", id)), null).Value is { } wire ? Load(wire) : null);

    public Task<List<Doc<T>>> ListAsync(Func<Doc<T>, bool> where, CancellationToken ct) =>
        Task.FromResult((Client.Send<List<WireDoc>>(HttpMethod.Get, Path("list"), null).Value ?? []).Select(Load).Where(where).ToList());

    public void Add(Doc<T> entity)
    {
        lock (gate) added.Add(entity);
    }

    public void Remove(Doc<T> entity)
    {
        lock (gate) removed.Add(entity);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        List<Doc<T>> adds, changes, removes;
        lock (gate)
        {
            adds = [.. added];
            changes = [.. changed.Distinct().Where(d => d.IsChanged && !removed.Contains(d))];
            removes = [.. removed];
            added.Clear();
            changed.Clear();
            removed.Clear();
        }
        if (adds.Count + changes.Count + removes.Count == 0) return Task.CompletedTask;
        var save = new WireSave(
            adds.Select(d => new WireDoc(d.Id, 0, codec.Encode(d.Value))).ToList(),
            changes.Select(d => new WireDoc(d.Id, d.Version, codec.Encode(d.Value))).ToList(),
            removes.Select(d => new WireDoc(d.Id, d.Version, "")).ToList());
        var versions = Client.Send<WireSaved>(HttpMethod.Post, Path("save"), save).Value!.Versions;
        foreach (var doc in adds.Concat(changes))
        {
            doc.Saved(versions.GetValueOrDefault(doc.Id));
            doc.Track(Enlist);
        }
        foreach (var doc in removes) doc.Saved(0);
        return Task.CompletedTask;
    }

    protected Doc<T> Load(WireDoc wire)
    {
        var doc = new Doc<T>(wire.Id, codec.Decode(wire.Text, wire.Id)) { Version = wire.Version };
        doc.Track(Enlist);
        return doc;
    }

    void Enlist(Doc<T> doc)
    {
        lock (gate) changed.Add(doc);
    }
}

/// <summary>
/// The envelopes through the store API, with the feed port: each read gets only the new envelopes of the feed of the
/// supervisor. When the supervisor starts again (a new epoch), the feed reads all envelopes again and keeps only the new ids.
/// </summary>
sealed class HttpEventRepository(StoreClient store) : HttpRepository<Event>(store, new WireScope(StoreWire.Events), JsonCodec<Event>.Instance), IAppendFeed<Doc<Event>>
{
    readonly Lock feedGate = new();
    readonly List<Doc<Event>> feed = [];
    readonly HashSet<string> seen = [];
    string? epoch;
    int position;

    public IReadOnlyList<Doc<Event>> ReadFrom(int from)
    {
        lock (feedGate)
        {
            var wire = Client.Send<WireFeed>(HttpMethod.Get, $"{StoreWire.Prefix}/{StoreWire.Events}/feed?from={(epoch is null ? 0 : position)}", null).Value!;
            if (epoch is not null && wire.Epoch != epoch)
                wire = Client.Send<WireFeed>(HttpMethod.Get, $"{StoreWire.Prefix}/{StoreWire.Events}/feed?from=0", null).Value!;
            epoch = wire.Epoch;
            position = wire.Position;
            foreach (var doc in wire.Docs)
                if (seen.Add(doc.Id)) feed.Add(Load(doc));
            return from >= feed.Count ? [] : feed.GetRange(from, feed.Count - from);
        }
    }
}

sealed class HttpLogSink(StoreClient client) : ILogSink
{
    public void Write(string line) => client.Send<object>(HttpMethod.Post, $"{StoreWire.Prefix}/log", new WireLine(line));
}

/// <summary>The push of the HTTP store: a poll. Each wait ends after the poll interval, and the subscriber queries again.</summary>
sealed class PollSignal(TimeSpan interval) : IStreamSignal
{
    public event Action? Changed;

    public async Task<bool> Wait(TimeSpan sweep, CancellationToken cancel = default)
    {
        try
        {
            await Task.Delay(sweep < interval ? sweep : interval, cancel);
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    public void Dispose()
    {
    }
}
