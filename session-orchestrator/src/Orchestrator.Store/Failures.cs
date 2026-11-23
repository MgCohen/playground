using Orchestrator.Core.Events;

namespace Orchestrator.Store;

/// <summary>
/// The failures that the supervisor sees (D, problem 4). One thread for each session and reason: a new try posts a new
/// <c>failure</c> in the thread, and a recovery posts the state <c>recovered</c>. The envelope knows nothing about
/// cards: the inbox view shows the newest failure of a thread while its state is open and it has no ack.
/// </summary>
public static class Failures
{
    public const string FailedTurns = "failed-turns";
    public const string NoAck = "no-ack";
    public const string Open = "open";
    public const string Recovered = "recovered";

    public static string Thread(string session, string reason) => $"failure-{session}-{reason}";

    /// <summary>The newest failure of a thread is open: its state is open and it has no ack ("Seen").</summary>
    public static bool IsOpen(FileStore store, Event e) =>
        e.Thread is { } thread && store.Events.Thread(thread).LastOrDefault()?.Id == e.Id
        && store.Events.Fields(e).GetValueOrDefault("state") as string == Open && !store.Events.IsAcked(e.Id);

    /// <summary>The fields of the newest failure of the session and reason while its state is open. Null: no open failure.</summary>
    public static IReadOnlyDictionary<string, object?>? Current(FileStore store, string session, string reason) =>
        store.Events.Thread(Thread(session, reason)).LastOrDefault() is { } last && store.Events.Fields(last) is var fields
        && fields.GetValueOrDefault("state") as string == Open ? fields : null;

    /// <summary>
    /// Posts the failure, or a new version of it when a fact changed: the tries, the error, the next try or the waiting
    /// envelopes. The same facts post nothing, so a sweep does not repeat the failure.
    /// </summary>
    public static Event? Report(FileStore store, string session, string reason, string text, string? error, int tries, string? next, IReadOnlyList<string> envelopes)
    {
        var current = Current(store, session, reason);
        var now = Core.Leases.Times.Format(store.Clock.GetUtcNow());
        if (current is not null && Same(current.GetValueOrDefault("tries"), tries) && current.GetValueOrDefault("error") as string == error
            && current.GetValueOrDefault("next") as string == next && Strings(current.GetValueOrDefault("envelopes")).SequenceEqual(envelopes)) return null;
        var body = new Dictionary<string, object?>
        {
            ["text"] = text, ["session"] = session, ["reason"] = reason, ["state"] = Open, ["error"] = error, ["tries"] = tries,
            ["first"] = current?.GetValueOrDefault("first") as string ?? now, ["last"] = now, ["next"] = next, ["envelopes"] = envelopes.ToList(),
        };
        return store.Events.Post(new PostInput(EventKinds.Failure, Parties.Supervisor, Parties.User, body) { Session = session, Thread = Thread(session, reason) });
    }

    /// <summary>Posts the state recovered when the session and reason have an open failure. Else nothing.</summary>
    public static Event? Recover(FileStore store, string session, string reason, string text)
    {
        if (Current(store, session, reason) is not { } current) return null;
        var body = new Dictionary<string, object?>
        {
            ["text"] = text, ["session"] = session, ["reason"] = reason, ["state"] = Recovered, ["tries"] = current.GetValueOrDefault("tries"),
            ["first"] = current.GetValueOrDefault("first"), ["last"] = Core.Leases.Times.Format(store.Clock.GetUtcNow()),
        };
        return store.Events.Post(new PostInput(EventKinds.Failure, Parties.Supervisor, Parties.User, body) { Session = session, Thread = Thread(session, reason) });
    }

    static bool Same(object? stored, int tries) => stored switch
    {
        double d => (int)d == tries,
        int i => i == tries,
        long l => l == tries,
        _ => false,
    };

    static IEnumerable<string> Strings(object? value) => value is IEnumerable<object?> list ? list.Select(o => o?.ToString() ?? "") : [];
}
