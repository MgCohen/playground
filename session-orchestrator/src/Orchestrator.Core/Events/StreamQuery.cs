using System.Globalization;

namespace Orchestrator.Core.Events;

/// <summary>
/// The one typed query of the stream. Each reader (supervisor, orch, plugin through orch, UI back end) asks the stream
/// with this record. All filters must match. A filter with no value matches all envelopes.
/// </summary>
public sealed record StreamQuery
{
    /// <summary>The value of <see cref="Needs"/> that matches an envelope with any needs.</summary>
    public const string AnyNeeds = "any";

    /// <summary>Envelopes at this time or later (the id time).</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Envelopes before this time (the id time).</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>Envelopes with an id after this id.</summary>
    public string? After { get; init; }

    public IReadOnlySet<string>? Kinds { get; init; }

    /// <summary>The session that the envelope is about, or that sends or receives it. A prefix of the id is enough.</summary>
    public string? Session { get; init; }

    /// <summary>A party that sends or receives the envelope. A prefix is enough.</summary>
    public string? Party { get; init; }

    public string? From { get; init; }
    public string? To { get; init; }
    public string? Thread { get; init; }

    /// <summary>The work stream of the envelope (exact, no case). An envelope with no stream never matches.</summary>
    public string? Stream { get; init; }

    /// <summary><c>answer</c>, <c>review</c>, <c>ack</c>, or <see cref="AnyNeeds"/>.</summary>
    public string? Needs { get; init; }

    /// <summary>Only the envelopes that wait for an act: see <c>EventQuery.IsOpen</c> in the store.</summary>
    public bool Open { get; init; }

    /// <summary>Only the answers by the docs or the policy, and the reviews.</summary>
    public bool Ledger { get; init; }

    /// <summary>False: no forwards. A forward has the same body as its original.</summary>
    public bool Forwards { get; init; } = true;

    /// <summary>Only the newest envelope of each thread. An envelope with no thread is its own thread.</summary>
    public bool ThreadHeads { get; init; }

    /// <summary>The newest envelope first.</summary>
    public bool Newest { get; init; }

    /// <summary>The maximum number of envelopes, after the order.</summary>
    public int? Limit { get; init; }

    /// <summary>The filters that need only the envelope. <see cref="Open"/> and the time range need the store.</summary>
    public bool Matches(Event e)
    {
        if (Kinds is { Count: > 0 } kinds && !kinds.Contains(e.Kind)) return false;
        if (Session is { Length: > 0 } session && !Touches(e, session)) return false;
        if (Party is { Length: > 0 } party && !Starts(e.From, party) && !Starts(e.To, party)) return false;
        if (From is { Length: > 0 } from && !Starts(e.From, from)) return false;
        if (To is { Length: > 0 } to && !Starts(e.To, to)) return false;
        if (Thread is { Length: > 0 } thread && e.Thread != thread) return false;
        if (Stream is { Length: > 0 } stream && !string.Equals(e.Stream, stream, StringComparison.OrdinalIgnoreCase)) return false;
        if (Needs is { Length: > 0 } needs && (e.Needs is null || (needs != AnyNeeds && e.Needs != needs))) return false;
        if (!Forwards && e.IsForward) return false;
        if (Ledger && !IsLedger(e)) return false;
        if (Until is { } until && IdTime(e.Id) is { } ms && ms >= until.ToUnixTimeMilliseconds()) return false;
        return true;
    }

    public static bool IsLedger(Event e) =>
        !e.IsForward && (e.Kind == EventKinds.Review || (e.Kind == EventKinds.Answer && e.From is Parties.Docs or Parties.Policy));

    public static long? IdTime(string id) =>
        long.TryParse(id.Split('-')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ? ms : null;

    static bool Starts(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    static bool Touches(Event e, string session) =>
        (e.Session?.StartsWith(session, StringComparison.OrdinalIgnoreCase) ?? false)
        || Starts(e.From, Parties.Session(session))
        || Starts(e.To, Parties.Session(session));
}

/// <summary>The named queries of the common views. The HTTP routes and the CLI use the same objects.</summary>
public static class StreamQueries
{
    /// <summary>The open cards of the user: envelopes to the user that wait for an act, the newest of each thread.</summary>
    public static StreamQuery Inbox { get; } = new() { To = Parties.User, Open = true, ThreadHeads = true };

    /// <summary>The decisions and queries with no answer, oldest first. With a session: only the questions of that session.</summary>
    public static StreamQuery OpenDecisions(string? session = null) =>
        new() { Kinds = EventKinds.Questions, From = session is null ? Parties.SessionPrefix : Parties.Session(session), Forwards = false, Open = true };

    /// <summary>The newest envelopes first, at most <paramref name="limit"/>. The filters of <paramref name="filter"/> stay.</summary>
    public static StreamQuery Timeline(StreamQuery? filter = null, int limit = 300) =>
        (filter ?? new StreamQuery()) with { Newest = true, Limit = limit };

    /// <summary>All envelopes of one conversation, in order.</summary>
    public static StreamQuery Thread(string thread) => new() { Thread = thread };

    /// <summary>The answers by the docs or the policy, and each review.</summary>
    public static StreamQuery Ledger { get; } = new() { Ledger = true };

    /// <summary>The names of the named queries, for the routes and <c>orch events --view</c>.</summary>
    public static StreamQuery? Named(string name, string? argument = null) => name switch
    {
        "inbox" => Inbox,
        "decisions" => OpenDecisions(argument),
        "timeline" => Timeline(),
        "thread" when argument is { Length: > 0 } => Thread(argument),
        "ledger" => Ledger,
        _ => null,
    };
}
