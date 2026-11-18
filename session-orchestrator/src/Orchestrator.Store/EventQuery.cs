using Orchestrator.Core.Events;
using Orchestrator.Core.Ordering;

namespace Orchestrator.Store;

/// <summary>Runs a <see cref="StreamQuery"/> on the stream of a store. It starts from the smallest index that the query names.</summary>
public static class EventQuery
{
    public static IReadOnlyList<Event> Query(this FileStore store, StreamQuery query)
    {
        var since = query.Since?.ToUnixTimeMilliseconds();
        var matches = Candidates(store.Events, query)
            .Where(e => since is null || StreamQuery.IdTime(e.Id) is not { } ms || ms >= since)
            .Where(e => query.Matches(e) && (!query.Open || IsOpen(store, e)));
        if (query.ThreadHeads) matches = matches.GroupBy(e => e.Thread ?? e.Id).Select(g => g.Last()).OrderBy(e => e.Id, Names.Order);
        if (query.Newest) matches = matches.Reverse();
        if (query.Limit is { } limit) matches = matches.Take(Math.Max(0, limit));
        return matches.ToList();
    }

    static IEnumerable<Event> Candidates(EventStream events, StreamQuery query)
    {
        if (query.Thread is { Length: > 0 } thread) return events.Thread(thread).Where(e => query.After is null || Names.Order.Compare(e.Id, query.After) > 0);
        if (query.After is { } after) return events.After(after);
        if (query.Kinds is { Count: > 0 and <= 4 } kinds) return kinds.SelectMany(events.OfKind).OrderBy(e => e.Id, Names.Order);
        if (query.Since is { } since) return events.Since(since);
        return events.All();
    }

    public static bool IsLedger(Event e) => StreamQuery.IsLedger(e);

    /// <summary>
    /// An envelope waits for an act: a question with no answer in its thread, or an envelope with needs and no reply or ack.
    /// A report that needs an ack is closed when its forward to the parent has an ack.
    /// </summary>
    public static bool IsOpen(FileStore store, Event e)
    {
        if (EventKinds.Questions.Contains(e.Kind) && !e.IsForward) return e.Thread is { } thread && store.Events.FirstAnswer(thread) is null;
        if (e.Needs is null) return false;
        if (store.Events.RepliesTo(e.Id).Count > 0 || store.Events.IsAcked(e.Id)) return false;
        return !(e.Needs == Needs.Ack && store.Events.ForwardsOf(e.Id).Any(f => store.Events.IsAcked(f.Id)));
    }
}

/// <summary>
/// The text form of a <see cref="StreamQuery"/>: the HTTP query string and the options of <c>orch events</c> use the same keys.
/// </summary>
public static class StreamQueryText
{
    public static readonly IReadOnlyList<string> Keys =
        ["view", "since", "until", "after", "kind", "session", "party", "from", "to", "thread", "stream", "needs", "open", "ledger", "forwards", "heads", "newest", "limit"];

    /// <summary>
    /// Makes the query from the values of the keys. <c>view</c> names a named query; the other keys add to it.
    /// A flag key is true when it is present with no value, or with <c>true</c> or <c>1</c>.
    /// </summary>
    public static StreamQuery Parse(Func<string, (bool Present, string? Value)> get, DateTimeOffset now, string? viewArgument = null)
    {
        string? Text(string key) => get(key) is { Present: true, Value: { Length: > 0 } value } ? value : null;
        bool? Flag(string key) => get(key) is { Present: true } found ? found.Value is null or "" or "true" or "1" : null;
        var query = new StreamQuery();
        if (Text("view") is { } view)
            query = StreamQueries.Named(view, viewArgument ?? Text("thread") ?? Text("session"))
                ?? throw new FormatException($"unknown view {view}: use inbox, decisions, timeline, thread or ledger");
        var needs = get("needs") is { Present: true } n ? n.Value is null or "" or "true" or "1" ? StreamQuery.AnyNeeds : n.Value == "false" ? null : n.Value : query.Needs;
        return query with
        {
            Since = Text("since") is { } since ? Durations.Since(since, now) : query.Since,
            Until = Text("until") is { } until ? Durations.Since(until, now) : query.Until,
            After = Text("after") ?? query.After,
            Kinds = Text("kind")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() ?? query.Kinds,
            Session = Text("session") ?? query.Session,
            Party = Text("party") ?? query.Party,
            From = Text("from") ?? query.From,
            To = Text("to") ?? query.To,
            Thread = Text("thread") ?? query.Thread,
            Stream = Text("stream") ?? query.Stream,
            Needs = needs,
            Open = Flag("open") ?? query.Open,
            Ledger = Flag("ledger") ?? query.Ledger,
            Forwards = Flag("forwards") ?? query.Forwards,
            ThreadHeads = Flag("heads") ?? query.ThreadHeads,
            Newest = Flag("newest") ?? query.Newest,
            Limit = Text("limit") is { } limit ? int.Parse(limit, System.Globalization.CultureInfo.InvariantCulture) : query.Limit,
        };
    }
}
