using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>Short ways for the tests to put posts in the stream, as the plugin and the user do.</summary>
public static class StoreHelpers
{
    /// <summary>A new session with its first message from the user.</summary>
    public static string Create(this FileStore store, Meta meta, string text)
    {
        var id = store.Create(meta);
        store.Send(id, text);
        return id;
    }

    public static Event Send(this FileStore store, string id, string text, string from = Parties.User) => new Messages(store).Send(id, text, from);

    /// <summary>A decision of a session, as the ask_user tool posts it.</summary>
    public static Event SaveDecision(this FileStore store, Decision d) =>
        store.Events.Post(new PostInput(EventKinds.Decision, Parties.Session(d.Session), Parties.User, new Dictionary<string, object?>
        {
            ["question"] = d.Question ?? "Q",
            ["context"] = d.Context ?? "context",
            ["options"] = d.Options ?? ["A", "B"],
            ["recommendation"] = d.Recommendation ?? "A",
            ["risk"] = d.Risk,
            ["defaultOption"] = d.DefaultOption,
            ["waitSeconds"] = d.WaitSeconds,
        }) { Session = d.Session, Thread = d.Id, Needs = Needs.Answer });

    public static Event SaveAnswer(this FileStore store, string id, string decision, Answer answer) =>
        new Messages(store).Answer(id, decision, answer.Value ?? "", answer.By ?? Parties.User,
            source: answer.By is Parties.Docs or Parties.Policy ? new AnswerSource(answer.By) { Reason = answer.Reason ?? "test", Source = answer.Source } : null);

    /// <summary>A review of one version of a decision, as the user posts it.</summary>
    public static Event SaveReview(this FileStore store, string id, string versionId, string verdict, string text) =>
        store.Events.Post(new PostInput(EventKinds.Review, Parties.User, Parties.Session(id), new Dictionary<string, object?>
        {
            ["decision"] = StackNames.DecisionOf(versionId),
            ["verdict"] = verdict,
            ["text"] = text,
            ["version"] = versionId,
        }) { Session = id, Thread = StackNames.DecisionOf(versionId) });

    /// <summary>A report of a session, as the report tool posts it.</summary>
    public static Event SaveReport(this FileStore store, string id, string state, string summary) =>
        store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(id), Parties.User, new Dictionary<string, object?> { ["state"] = state, ["summary"] = summary })
        {
            Session = id,
            Needs = state is ReportStates.Done or ReportStates.Blocked ? Needs.Ack : null,
        });

    /// <summary>True when the answer to the decision has an ack: the session got it.</summary>
    public static bool IsDelivered(this FileStore store, string id, string decision) =>
        store.Events.FirstAnswer(decision) is { } answer && store.Events.IsAcked(answer.Id);
}
