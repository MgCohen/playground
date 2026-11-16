using Orchestrator.Core.Events;

namespace Orchestrator.Store;

/// <summary>One open card of the user inbox: an envelope to the user that needs an act.</summary>
public sealed record Card(Event Envelope, string Session, string Title, IReadOnlyDictionary<string, object?> Fields)
{
    public string Kind => Envelope.Kind;
    public string? Needs => Envelope.Needs ?? (EventKinds.Questions.Contains(Envelope.Kind) ? Core.Events.Needs.Answer : null);
}

/// <summary>
/// The user inbox reactor: open cards = envelopes to the user that need an act, minus the ones with a reply or an ack.
/// It keeps the newest card of each thread. Accept and refuse post the reply type of the card.
/// </summary>
public sealed class Inbox(FileStore store)
{
    readonly Messages messages = new(store);

    /// <summary>The named query <see cref="StreamQueries.Inbox"/> as cards.</summary>
    public IReadOnlyList<Card> Cards() => store.Query(StreamQueries.Inbox).Select(ToCard).ToList();

    Card ToCard(Event e)
    {
        var session = e.Session ?? Parties.SessionId(e.From) ?? "";
        return new Card(e, session, store.Meta(session).Title ?? (session.Length > 8 ? session[..8] : session), store.Events.Fields(e));
    }

    public Card Find(string id) =>
        Cards().FirstOrDefault(c => c.Envelope.Id == id) ?? throw new MessageException($"no open card {id}");

    /// <summary>Accept: a decision takes its default, a review card is approved, a report gets an ack.</summary>
    public Event Accept(string id, string? text = null)
    {
        var card = Find(id);
        var e = card.Envelope;
        switch (card.Needs)
        {
            case Needs.Answer:
                var answer = text is { Length: > 0 } ? text : card.Fields.GetValueOrDefault("defaultOption") as string
                    ?? throw new MessageException($"{e.Kind} {e.Thread} has no default: give the answer as text");
                return messages.Answer(card.Session, e.Thread ?? e.Id, answer);
            case Needs.Review:
                return messages.Review(card.Session, e.Thread ?? e.Id, Core.Contract.Verdicts.Approve, text ?? "");
            default:
                return messages.Ack(e.Id);
        }
    }

    /// <summary>Refuse: a decision gets the text as answer, a review card is refused, a report gets the text as a message and an ack.</summary>
    public Event Refuse(string id, string text)
    {
        var card = Find(id);
        var e = card.Envelope;
        switch (card.Needs)
        {
            case Needs.Answer:
                if (text.Trim().Length == 0) throw new MessageException("refuse needs a text: the answer");
                return messages.Answer(card.Session, e.Thread ?? e.Id, text);
            case Needs.Review:
                return messages.Review(card.Session, e.Thread ?? e.Id, Core.Contract.Verdicts.Refuse, text);
            default:
                if (text.Trim().Length == 0) throw new MessageException("refuse needs a text: the message to the session");
                messages.Send(card.Session, text);
                return messages.Ack(e.Id);
        }
    }
}
