using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store.Local;

/// <summary>
/// The local folder implementation of the envelopes: <c>events/&lt;day&gt;/&lt;id&gt;.json</c>, with the day of the id time.
/// The repository keeps the envelopes that it read in memory and reads only new files at each list. An envelope does
/// not change after its write, so an envelope has no change save.
/// </summary>
/// <remarks>
/// The day cache: a day folder that has two newer days is closed and never listed again. An open day folder is
/// listed again only when its write time changed, or when its write time is newer than <see cref="Settle"/>
/// (a file system can give two creates in one clock tick the same write time). A create, a rename or a delete of a
/// file in a folder changes the write time of the folder.
/// </remarks>
public sealed class LocalEventRepository(string root) : IRepository<Doc<Event>, string>, IAppendFeed<Doc<Event>>
{
    /// <summary>A folder write time newer than this can still hide a create in the same clock tick.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    readonly Lock gate = new();
    readonly List<Doc<Event>> read = [];
    readonly List<Doc<Event>> feed = [];
    readonly Dictionary<string, Doc<Event>> byId = [];
    readonly HashSet<string> closedDays = [];
    readonly Dictionary<string, DateTime> dayWrites = [];
    IReadOnlyList<string> days = [];
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

    public IReadOnlyList<Doc<Event>> ReadFrom(int position)
    {
        lock (gate)
        {
            Refresh();
            return position >= feed.Count ? [] : feed.GetRange(position, feed.Count - position);
        }
    }

    /// <summary>The number of day folder listings since the start: for the tests of the day cache.</summary>
    public int Listings { get; private set; }

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
        // A new day folder changes the write time of events/, so the list of days has the same cache.
        if (Changed("", Folder)) days = Files.Names(Folder);
        foreach (var day in days)
        {
            if (closedDays.Contains(day)) continue;
            var folder = Path.Combine(Folder, day);
            if (!Changed(day, folder)) continue;
            Listings++;
            foreach (var name in Files.Names(folder))
            {
                if (byId.ContainsKey(Path.GetFileNameWithoutExtension(name))) continue;
                if (Files.ReadText(Path.Combine(Folder, day, name)) is { } text && StoreJson.Read<Event>(text) is { } e) Keep(e, text);
            }
        }
        // A day closes when two newer days exist: a late writer can still add a file to yesterday.
        foreach (var day in days.Take(Math.Max(0, days.Count - 2)))
            if (closedDays.Add(day)) dayWrites.Remove(day);
    }

    /// <summary>
    /// True when the folder needs a listing: its write time changed, or it is newer than <see cref="Settle"/>. The
    /// write time is read before the listing: a file that arrives during the listing changes it again.
    /// </summary>
    bool Changed(string key, string folder)
    {
        var write = Directory.GetLastWriteTimeUtc(folder);
        if (dayWrites.TryGetValue(key, out var seen) && seen == write && DateTime.UtcNow - write > Settle) return false;
        dayWrites[key] = write;
        return true;
    }

    Doc<Event> Keep(Event e, string text) => Keep(new Doc<Event>(e.Id, e) { Version = Versions.Of(text) });

    Doc<Event> Keep(Doc<Event> doc)
    {
        if (byId.TryAdd(doc.Id, doc))
        {
            read.Add(doc);
            feed.Add(doc);
        }
        return byId[doc.Id];
    }

    string? PathOf(string id) =>
        long.TryParse(id.Split('-')[0], out var ms) ? Path.Combine(Folder, Day(ms), StoreFiles.Json(id)) : null;

    public static string Day(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd");
}
