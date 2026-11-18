using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store.Local;

/// <summary>
/// The local folder implementation of the envelopes: <c>events/&lt;day&gt;/&lt;id&gt;.json</c>, with the day of the id time.
/// The repository keeps the envelopes that it read in memory and reads only new files at each list. An envelope does
/// not change after its write, so an envelope has no change save.
/// </summary>
public sealed class LocalEventRepository(string root) : IRepository<Doc<Event>, string>
{
    readonly Lock gate = new();
    readonly List<Doc<Event>> read = [];
    readonly Dictionary<string, Doc<Event>> byId = [];
    readonly HashSet<string> closedDays = [];
    readonly List<Doc<Event>> added = [];
    readonly List<Doc<Event>> removed = [];

    string Folder => Path.Combine(root, StoreFiles.Events);

    public Task<Doc<Event>?> GetAsync(string id, CancellationToken ct)
    {
        lock (gate)
        {
            if (byId.TryGetValue(id, out var known)) return Task.FromResult<Doc<Event>?>(known);
            if (PathOf(id) is not { } path || Files.ReadText(path) is not { } text || StoreJson.Read<Event>(text) is not { } e) return Task.FromResult<Doc<Event>?>(null);
            return Task.FromResult<Doc<Event>?>(Keep(e, text));
        }
    }

    public Task<List<Doc<Event>>> ListAsync(Func<Doc<Event>, bool> where, CancellationToken ct)
    {
        lock (gate)
        {
            Refresh();
            return Task.FromResult(read.Where(where).ToList());
        }
    }

    public void Add(Doc<Event> entity)
    {
        lock (gate) added.Add(entity);
    }

    public void Remove(Doc<Event> entity)
    {
        lock (gate) removed.Add(entity);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        List<Doc<Event>> adds, removes;
        lock (gate)
        {
            adds = [.. added];
            removes = [.. removed];
            added.Clear();
            removed.Clear();
        }
        foreach (var doc in adds)
        {
            var path = PathOf(doc.Id) ?? throw new ArgumentException($"{doc.Id} is not an envelope id: it has no time");
            var text = StoreJson.Write(doc.Value);
            if (!Files.Create(path, text)) throw new StoreConflictException(StoreConflict.Exists, doc.Id, $"envelope {doc.Id} exists. Nothing changed.");
            doc.Saved(Versions.Of(text));
            lock (gate) Keep(doc);
        }
        foreach (var doc in removes)
        {
            if (PathOf(doc.Id) is not { } path) continue;
            var current = Files.ReadText(path);
            if ((current is null ? 0 : Versions.Of(current)) != doc.Version)
                throw new StoreConflictException(StoreConflict.Stale, doc.Id, $"envelope {doc.Id} changed after the read. Nothing changed.");
            Files.Delete(path);
            doc.Saved(0);
            lock (gate)
            {
                byId.Remove(doc.Id);
                read.Remove(doc);
            }
        }
        return Task.CompletedTask;
    }

    void Refresh()
    {
        if (!Directory.Exists(Folder)) return;
        var days = Files.Names(Folder);
        foreach (var day in days)
        {
            if (closedDays.Contains(day)) continue;
            foreach (var name in Files.Names(Path.Combine(Folder, day)))
            {
                if (byId.ContainsKey(Path.GetFileNameWithoutExtension(name))) continue;
                if (Files.ReadText(Path.Combine(Folder, day, name)) is { } text && StoreJson.Read<Event>(text) is { } e) Keep(e, text);
            }
        }
        // A day closes when two newer days exist: a late writer can still add a file to yesterday.
        foreach (var day in days.Take(Math.Max(0, days.Count - 2))) closedDays.Add(day);
    }

    Doc<Event> Keep(Event e, string text) => Keep(new Doc<Event>(e.Id, e) { Version = Versions.Of(text) });

    Doc<Event> Keep(Doc<Event> doc)
    {
        if (byId.TryAdd(doc.Id, doc)) read.Add(doc);
        return byId[doc.Id];
    }

    string? PathOf(string id) =>
        long.TryParse(id.Split('-')[0], out var ms) ? Path.Combine(Folder, Day(ms), StoreFiles.Json(id)) : null;

    public static string Day(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd");
}
