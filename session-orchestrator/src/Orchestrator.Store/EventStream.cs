using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Ordering;

namespace Orchestrator.Store;

public sealed class EventStream(string root, TimeProvider clock, string writer = Parties.Supervisor)
{
    public Event Append(string kind, string from, string to, IReadOnlyDictionary<string, object?> data, IReadOnlyDictionary<string, object?>? refs = null, string? session = null)
    {
        var at = clock.GetUtcNow();
        var id = Names.NewEventId(at, writer);
        var day = Day(at);
        var body = $"{StoreFiles.Bodies}/{day}/{id}.json";
        Files.WriteJson(Path.Combine(root, body), data);
        var e = new Event(id, Times.Format(at), session, kind, from, to, Subjects.Of(kind, data), body, ToElements(refs));
        Files.WriteJson(Path.Combine(root, StoreFiles.Events, day, $"{id}.json"), e);
        return e;
    }

    public IEnumerable<Event> All() => Read(null);

    public IEnumerable<Event> Since(DateTimeOffset since) => Read(since);

    public Event? Find(string id) =>
        long.TryParse(id.Split('-')[0], out var ms) ? Files.ReadJson<Event>(Path.Combine(root, StoreFiles.Events, Day(DateTimeOffset.FromUnixTimeMilliseconds(ms)), $"{id}.json")) : null;

    public JsonElement? Body(Event e) => Files.ReadJson<JsonElement>(Path.Combine(root, e.Body)) is { ValueKind: not JsonValueKind.Undefined } body ? body : null;

    IEnumerable<Event> Read(DateTimeOffset? since)
    {
        var folder = Path.Combine(root, StoreFiles.Events);
        var first = since is { } s ? Day(s) : null;
        var minId = since?.ToUnixTimeMilliseconds().ToString();
        foreach (var day in Files.Names(folder))
        {
            if (first is not null && string.CompareOrdinal(day, first) < 0) continue;
            foreach (var name in Files.Names(Path.Combine(folder, day)))
            {
                if (minId is not null && Names.Order.Compare(name, minId) < 0) continue;
                if (Files.ReadJson<Event>(Path.Combine(folder, day, name)) is { } e) yield return e;
            }
        }
    }

    static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd");

    static IReadOnlyDictionary<string, JsonElement> ToElements(IReadOnlyDictionary<string, object?>? refs) =>
        (refs ?? new Dictionary<string, object?>()).Where(r => r.Value is not null).ToDictionary(r => r.Key, r => JsonSerializer.SerializeToElement(r.Value, StoreJson.Options));
}
