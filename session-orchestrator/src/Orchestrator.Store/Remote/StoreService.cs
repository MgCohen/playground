using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;

namespace Orchestrator.Store.Remote;

/// <summary>The answer of the store API: an HTTP status and a JSON value.</summary>
public sealed record StoreReply(int Status, object? Value);

/// <summary>
/// The server side of the store API (H5): it applies each call of <see cref="HttpRepositories"/> to the repositories of
/// the supervisor. It has no HTTP code, so the tests call it directly. One save runs at a time, because the
/// repositories keep the pending adds and removes until the save.
/// </summary>
public sealed class StoreService(IRepositories repos)
{
    readonly Lock gate = new();
    readonly string epoch = Guid.NewGuid().ToString("N");

    public IRepositories Repositories => repos;

    public StoreReply Info() => new(200, new WireInfo(repos.Location, repos.Exists(), StoreVersion.Current));

    public StoreReply Prepare()
    {
        repos.Prepare();
        return Info();
    }

    public StoreReply Log(WireLine? line)
    {
        if (line?.Line is not { } text) return Bad("line is necessary");
        repos.Log.Write(text);
        return new(204, null);
    }

    public StoreReply Get(WireScope scope, string? id)
    {
        if (!StoreWire.IsId(id)) return Bad($"id {id} is not a valid store id");
        if (Open(scope) is not { } repository) return Bad(Refused(scope));
        lock (gate) return repository.Get(id!) is { } doc ? new(200, doc) : new(404, new { error = $"no {scope.Kind} {id}" });
    }

    public StoreReply List(WireScope scope)
    {
        if (Open(scope) is not { } repository) return Bad(Refused(scope));
        lock (gate) return new(200, repository.List());
    }

    public StoreReply Save(WireScope scope, WireSave? save)
    {
        if (save is null) return Bad("the save is empty");
        var ids = save.Adds.Concat(save.Changes).Concat(save.Removes).Select(d => d.Id).ToList();
        if (ids.FirstOrDefault(id => !StoreWire.IsId(id)) is { } bad) return Bad($"id {bad} is not a valid store id");
        if (Open(scope) is not { } repository) return Bad(Refused(scope));
        try
        {
            lock (gate) return new(200, new WireSaved(repository.Save(save)));
        }
        catch (StoreConflictException e)
        {
            return new(409, new WireConflict(e.Conflict.ToString(), e.Id, e.Message));
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or FormatException)
        {
            return Bad($"a document of the save is not valid: {e.Message}");
        }
    }

    /// <summary>The envelopes after <paramref name="position"/> in the feed of this process. A new process has a new epoch.</summary>
    public StoreReply Feed(int position)
    {
        lock (gate)
        {
            if (repos.Events is IAppendFeed<Doc<Event>> feed)
            {
                var docs = feed.ReadFrom(Math.Max(0, position));
                return new(200, new WireFeed(epoch, Math.Max(0, position) + docs.Count, docs.Select(d => Wire(d, JsonCodec<Event>.Instance)).ToList()));
            }
            var all = repos.Events.List(_ => true);
            return new(200, new WireFeed(Guid.NewGuid().ToString("N"), all.Count, all.Select(d => Wire(d, JsonCodec<Event>.Instance)).ToList()));
        }
    }

    static StoreReply Bad(string error) => new(400, new { error });

    static string Refused(WireScope scope) => $"unknown kind or a bad scope: {scope}";

    static WireDoc Wire<T>(Doc<T> doc, IFileCodec<T> codec) => new(doc.Id, doc.Version, codec.Encode(doc.Value));

    IWireRepository? Open(WireScope scope) => scope.Kind switch
    {
        StoreWire.Events => new WireRepository<Event>(repos.Events, JsonCodec<Event>.Instance),
        StoreWire.Bodies => new WireRepository<JsonElement>(repos.Bodies, JsonCodec<JsonElement>.Instance),
        StoreWire.Policies => new WireRepository<Core.Policy.Policy>(repos.Policies, PolicyCodec.Instance),
        StoreWire.StoreVersions => new WireRepository<StoreVersion>(repos.StoreVersions, JsonCodec<StoreVersion>.Instance),
        StoreWire.Claims => new WireRepository<string>(repos.Claims, TextCodec.Instance),
        StoreWire.Cursors => new WireRepository<CursorState>(repos.Cursors, JsonCodec<CursorState>.Instance),
        StoreWire.Nudges => new WireRepository<string>(repos.Nudges, TextCodec.Instance),
        StoreWire.Sessions => new WireRepository<string>(repos.Sessions, TextCodec.Instance),
        StoreWire.SessionEntries => new WireRepository<Core.Sessions.SessionEntry>(repos.SessionEntries, JsonCodec<Core.Sessions.SessionEntry>.Instance),
        StoreWire.WorkflowRuns => new WireRepository<Core.Workflows.WorkflowRun>(repos.WorkflowRuns, JsonCodec<Core.Workflows.WorkflowRun>.Instance),
        StoreWire.SessionFile when StoreWire.IsPart(scope.File) => new WireRepository<string>(repos.SessionFile<string>(scope.File!), TextCodec.Instance),
        StoreWire.SessionItems when StoreWire.IsPart(scope.Session) && StoreWire.IsPart(scope.Folder) =>
            new WireRepository<string>(repos.SessionItems<string>(scope.Session!, scope.Folder!), TextCodec.Instance),
        StoreWire.Definitions when Catalog.All.FirstOrDefault(c => c.Name == scope.Catalog) is { } catalog =>
            new WireRepository<Definition>(repos.Definitions(catalog), DefinitionCodec.Instance),
        _ => null,
    };

    interface IWireRepository
    {
        WireDoc? Get(string id);
        List<WireDoc> List();
        Dictionary<string, long> Save(WireSave save);
    }

    /// <summary>One repository of the supervisor as text documents. A session file goes as its raw text.</summary>
    sealed class WireRepository<T>(IRepository<Doc<T>, string> repository, IFileCodec<T> codec) : IWireRepository
    {
        public WireDoc? Get(string id) => repository.Get(id) is { } doc ? Wire(doc, codec) : null;

        public List<WireDoc> List() => repository.List(_ => true).Select(d => Wire(d, codec)).ToList();

        /// <summary>
        /// Checks the version of each change and remove before the first write, so a refused save writes nothing that
        /// the caller can see as pending. The repository checks each version again in its save.
        /// </summary>
        public Dictionary<string, long> Save(WireSave save)
        {
            var changes = new List<(Doc<T> Doc, T Value)>();
            foreach (var change in save.Changes)
            {
                var doc = repository.Get(change.Id);
                if (doc is null || doc.Version != change.Version)
                    throw new StoreConflictException(StoreConflict.Stale, change.Id, $"{change.Id} changed after the read (version {doc?.Version ?? 0}, read {change.Version}). Nothing changed.");
                changes.Add((doc, codec.Decode(change.Text, change.Id)));
            }
            var removes = new List<Doc<T>>();
            foreach (var remove in save.Removes)
            {
                var doc = repository.Get(remove.Id);
                if ((doc?.Version ?? 0) != remove.Version)
                    throw new StoreConflictException(StoreConflict.Stale, remove.Id, $"{remove.Id} changed after the read (version {doc?.Version ?? 0}, read {remove.Version}). Nothing changed.");
                if (doc is not null) removes.Add(doc);
            }
            var adds = save.Adds.Select(a => new Doc<T>(a.Id, codec.Decode(a.Text, a.Id))).ToList();
            foreach (var doc in adds) repository.Add(doc);
            foreach (var (doc, value) in changes) doc.Value = value;
            foreach (var doc in removes) repository.Remove(doc);
            repository.Save();
            var versions = new Dictionary<string, long>();
            foreach (var doc in adds) versions[doc.Id] = doc.Version;
            foreach (var (doc, _) in changes) versions[doc.Id] = doc.Version;
            foreach (var remove in save.Removes) versions[remove.Id] = 0;
            return versions;
        }
    }
}
