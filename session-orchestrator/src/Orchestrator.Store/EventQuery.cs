using Orchestrator.Core.Events;

namespace Orchestrator.Store;

public sealed record EventFilter
{
    public DateTimeOffset? Since { get; init; }
    public IReadOnlySet<string>? Kinds { get; init; }
    public string? Session { get; init; }
    public string? From { get; init; }
    public bool Open { get; init; }
    public bool Ledger { get; init; }
}

public static class EventQuery
{
    public static bool IsLedger(Event e) =>
        e.Kind == EventKinds.Review || (e.Kind == EventKinds.Answer && e.From is Parties.Docs or Parties.Policy);

    public static IEnumerable<Event> Query(this FileStore store, EventFilter filter)
    {
        var events = filter.Since is { } since ? store.Events.Since(since) : store.Events.All();
        return events.Where(e => Matches(store, e, filter));
    }

    public static bool Matches(FileStore store, Event e, EventFilter filter)
    {
        if (filter.Kinds is { Count: > 0 } kinds && !kinds.Contains(e.Kind)) return false;
        if (filter.Session is { Length: > 0 } session && !Touches(e, session)) return false;
        if (filter.From is { Length: > 0 } from && !e.From.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return false;
        if (filter.Ledger && !IsLedger(e)) return false;
        if (filter.Open && !IsOpen(store, e)) return false;
        return true;
    }

    static bool Touches(Event e, string session) =>
        (e.Session?.StartsWith(session, StringComparison.OrdinalIgnoreCase) ?? false)
        || e.From.StartsWith(Parties.Session(session), StringComparison.OrdinalIgnoreCase)
        || e.To.StartsWith(Parties.Session(session), StringComparison.OrdinalIgnoreCase);

    static bool IsOpen(FileStore store, Event e) =>
        e.Kind == EventKinds.Decision && e.Session is { } session && e.Refs.TryGetValue("decision", out var d) && d.GetString() is { } decision && store.IsOpen(session, decision);
}
