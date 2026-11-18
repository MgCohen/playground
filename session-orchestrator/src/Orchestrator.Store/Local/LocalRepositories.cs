using System.Collections.Concurrent;
using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store.Local;

/// <summary>
/// The local folder store: the only C# code that reads and writes the store folder. The layout is in
/// <c>docs/store.md</c>. The plugin hooks write the same files with the same layout.
/// </summary>
public sealed class LocalRepositories : IRepositories
{
    readonly string root;
    readonly ConcurrentDictionary<(string File, Type Type), object> sessionFiles = new();

    public LocalRepositories(string root)
    {
        this.root = Path.GetFullPath(root);
        Events = new LocalEventRepository(this.root);
        Bodies = Repository(id => Path.Combine(this.root, id), () => Nested(StoreFiles.Bodies).Select(name => $"{StoreFiles.Bodies}/{name}"), JsonCodec<JsonElement>.Instance);
        Policies = Repository(id => Path.Combine(this.root, StoreFiles.Json(id)), () => Single(StoreIds.Policy), PolicyCodec.Instance);
        StoreVersions = Repository(id => Path.Combine(this.root, StoreFiles.Json(id)), () => Single(StoreIds.Version), JsonCodec<StoreVersion>.Instance);
        Claims = Repository(id => Path.Combine(this.root, StoreFiles.Claims, id), () => Nested(StoreFiles.Claims, json: false), TextCodec.Instance);
        Cursors = Repository(id => Path.Combine(this.root, StoreFiles.Subscriptions, StoreFiles.Json(id)), () => Nested(StoreFiles.Subscriptions), JsonCodec<CursorState>.Instance, lockFiles: true);
        Nudges = Repository(id => Path.Combine(this.root, StoreFiles.Nudges, id), () => Files.Names(Path.Combine(this.root, StoreFiles.Nudges)), TextCodec.Instance);
        Sessions = new LocalSessionFolders(Path.Combine(this.root, StoreFiles.Sessions));
        Log = new LocalLogSink(Path.Combine(this.root, StoreFiles.SupervisorLog));
    }

    public string Location => root;
    public IRepository<Doc<Event>, string> Events { get; }
    public IRepository<Doc<JsonElement>, string> Bodies { get; }
    public IRepository<Doc<Core.Policy.Policy>, string> Policies { get; }
    public IRepository<Doc<StoreVersion>, string> StoreVersions { get; }
    public IRepository<Doc<string>, string> Claims { get; }
    public IRepository<Doc<CursorState>, string> Cursors { get; }
    public IRepository<Doc<string>, string> Nudges { get; }
    public IRepository<Doc<string>, string> Sessions { get; }
    public ILogSink Log { get; }

    public IRepository<Doc<T>, string> SessionFile<T>(string file) =>
        (IRepository<Doc<T>, string>)sessionFiles.GetOrAdd((file, typeof(T)), _ => Repository(
            id => Path.Combine(root, StoreFiles.Sessions, id, file),
            () => Files.Names(Path.Combine(root, StoreFiles.Sessions)).Where(id => File.Exists(Path.Combine(root, StoreFiles.Sessions, id, file))),
            Codec<T>()));

    public IRepository<Doc<T>, string> SessionItems<T>(string session, string folder)
    {
        var path = Path.Combine(root, StoreFiles.Sessions, session, folder);
        return Repository(
            id => Path.Combine(path, StoreFiles.Json(id)),
            () => Files.Names(path).Where(name => name.EndsWith(".json")).Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            Codec<T>());
    }

    public bool Exists() => Directory.Exists(root);

    /// <summary>Makes the folders of the stream.</summary>
    public void Prepare()
    {
        foreach (var folder in new[] { StoreFiles.Events, StoreFiles.Bodies, StoreFiles.Subscriptions, StoreFiles.Nudges })
            Directory.CreateDirectory(Path.Combine(root, folder));
    }

    public IStreamSignal Watch() => new StreamWatcher(root);

    /// <summary>The local path of a session file, for tests that write a file as a hook does.</summary>
    public string PathOf(string id, params string[] parts) => Path.Combine([root, StoreFiles.Sessions, id, .. parts]);

    static LocalFileRepository<T> Repository<T>(Func<string, string> pathOf, Func<IEnumerable<string>> ids, IFileCodec<T> codec, bool lockFiles = false) =>
        new(pathOf, ids, codec, lockFiles);

    static IFileCodec<T> Codec<T>() => typeof(T) == typeof(string) ? (IFileCodec<T>)(object)TextCodec.Instance : JsonCodec<T>.Instance;

    IEnumerable<string> Single(string id) => File.Exists(Path.Combine(root, StoreFiles.Json(id))) ? [id] : [];

    /// <summary>The ids <c>&lt;folder&gt;/&lt;file&gt;</c> of the files one level under <paramref name="kind"/>.</summary>
    IEnumerable<string> Nested(string kind, bool json = true)
    {
        var top = Path.Combine(root, kind);
        foreach (var folder in Files.Names(top))
            foreach (var name in Files.Names(Path.Combine(top, folder)))
                if (!json) yield return $"{folder}/{name}";
                else if (name.EndsWith(".json")) yield return kind == StoreFiles.Bodies ? $"{folder}/{name}" : $"{folder}/{name[..^5]}";
    }
}

/// <summary>The session folders. Id and value: the session id. An add makes the folder, a remove deletes it with its files.</summary>
public sealed class LocalSessionFolders(string folder) : IRepository<Doc<string>, string>
{
    readonly Lock gate = new();
    readonly List<Doc<string>> added = [];
    readonly List<Doc<string>> removed = [];

    public Task<Doc<string>?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Directory.Exists(Path.Combine(folder, id)) ? new Doc<string>(id, id) { Version = 1 } : null);

    public Task<List<Doc<string>>> ListAsync(Func<Doc<string>, bool> where, CancellationToken ct) =>
        Task.FromResult(Files.Names(folder).Select(id => new Doc<string>(id, id) { Version = 1 }).Where(where).ToList());

    public void Add(Doc<string> entity)
    {
        lock (gate) added.Add(entity);
    }

    public void Remove(Doc<string> entity)
    {
        lock (gate) removed.Add(entity);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        List<Doc<string>> adds, removes;
        lock (gate)
        {
            adds = [.. added];
            removes = [.. removed];
            added.Clear();
            removed.Clear();
        }
        foreach (var doc in adds)
        {
            var path = Path.Combine(folder, doc.Id);
            if (Directory.Exists(path)) throw new StoreConflictException(StoreConflict.Exists, doc.Id, $"session {doc.Id} exists. Nothing changed.");
            Directory.CreateDirectory(path);
            doc.Saved(1);
        }
        foreach (var doc in removes)
        {
            var path = Path.Combine(folder, doc.Id);
            if (doc.Version == 0 && Directory.Exists(path))
                throw new StoreConflictException(StoreConflict.Stale, doc.Id, $"session {doc.Id} exists, but the remove has no read of it. Nothing changed.");
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            doc.Saved(0);
        }
        return Task.CompletedTask;
    }
}

public sealed class LocalLogSink(string path) : ILogSink
{
    public void Write(string line) => Files.Append(path, line);
}
