using Orchestrator.Core.Events;

namespace Orchestrator.Store;

/// <summary>The filter of the primitives Query and Subscribe.</summary>
public sealed record EventFilter
{
    public DateTimeOffset? Since { get; init; }
    public string? After { get; init; }
    public IReadOnlySet<string>? Kinds { get; init; }
    public string? Session { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Thread { get; init; }
    public bool Needs { get; init; }
    public bool Open { get; init; }
    public bool Ledger { get; init; }
}

public static class EventQuery
{
    public static bool IsLedger(Event e) =>
        !e.IsForward && (e.Kind == EventKinds.Review || (e.Kind == EventKinds.Answer && e.From is Parties.Docs or Parties.Policy));

    public static IEnumerable<Event> Query(this FileStore store, EventFilter filter)
    {
        var events = filter.After is { } after ? store.Events.After(after)
            : filter.Since is { } since ? store.Events.Since(since)
            : store.Events.All();
        return events.Where(e => Matches(store, e, filter));
    }

    public static bool Matches(FileStore store, Event e, EventFilter filter)
    {
        if (filter.Kinds is { Count: > 0 } kinds && !kinds.Contains(e.Kind)) return false;
        if (filter.Session is { Length: > 0 } session && !Touches(e, session)) return false;
        if (filter.From is { Length: > 0 } from && !e.From.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return false;
        if (filter.To is { Length: > 0 } to && !e.To.StartsWith(to, StringComparison.OrdinalIgnoreCase)) return false;
        if (filter.Thread is { Length: > 0 } thread && e.Thread != thread) return false;
        if (filter.Needs && e.Needs is null) return false;
        if (filter.Ledger && !IsLedger(e)) return false;
        if (filter.Open && !IsOpen(store, e)) return false;
        return true;
    }

    static bool Touches(Event e, string session) =>
        (e.Session?.StartsWith(session, StringComparison.OrdinalIgnoreCase) ?? false)
        || e.From.StartsWith(Parties.Session(session), StringComparison.OrdinalIgnoreCase)
        || e.To.StartsWith(Parties.Session(session), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An envelope waits for an act: a question with no answer in its thread, or an envelope with needs and no reply or ack.
    /// </summary>
    public static bool IsOpen(FileStore store, Event e)
    {
        if (EventKinds.Questions.Contains(e.Kind) && !e.IsForward) return e.Thread is { } thread && store.Events.FirstAnswer(thread) is null;
        if (e.Needs is null) return false;
        if (store.Events.RepliesTo(e.Id).Count > 0 || store.Events.IsAcked(e.Id)) return false;
        return !(e.Needs == Needs.Ack && store.Events.ForwardsOf(e.Id).Any(f => store.Events.IsAcked(f.Id)));
    }
}
