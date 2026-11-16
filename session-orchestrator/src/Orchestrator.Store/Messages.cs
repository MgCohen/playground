using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Stack;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

public sealed class MessageException(string message) : Exception(message);

public sealed record BriefFields
{
    public string? Title { get; init; }
    public string? Goal { get; init; }
    public string? Scope { get; init; }
    public string? DoneWhen { get; init; }
    public IReadOnlyList<string>? Skills { get; init; }
    public string? Agent { get; init; }
    public string? Cwd { get; init; }
    public bool? Worktree { get; init; }
    public string? Parent { get; init; }
    public bool? AllowDefaults { get; init; }
}

public sealed class Messages(FileStore store)
{
    public string NewSession(BriefFields brief, string createdBy, string from = Parties.User)
    {
        var fields = Fields(("title", brief.Title), ("goal", brief.Goal), ("scope", brief.Scope), ("doneWhen", brief.DoneWhen),
            ("skills", brief.Skills), ("agent", brief.Agent), ("cwd", brief.Cwd), ("worktree", brief.Worktree), ("parent", brief.Parent), ("allowDefaults", brief.AllowDefaults));
        Check(Kinds.Brief, fields);
        if (string.IsNullOrEmpty(brief.Cwd)) throw new MessageException("brief: cwd is necessary");
        var id = Guid.NewGuid().ToString();
        var text = store.Templates.Render(Kinds.Brief, fields);
        store.Events.Append(EventKinds.Brief, from, Parties.Session(id), new Dictionary<string, object?>(fields) { ["text"] = text }, brief.Parent is { } p ? Fields(("parent", p)) : null, id);
        store.Create(new Meta(id) { Title = brief.Title, Cwd = brief.Cwd, Parent = brief.Parent, CreatedBy = createdBy, Worktree = brief.Worktree, Agent = brief.Agent, Skills = brief.Skills?.ToList(), AllowDefaults = brief.AllowDefaults }, text);
        return id;
    }

    public (string Inbox, string Event) Send(string session, string text, string from = Parties.User)
    {
        var e = store.Events.Append(EventKinds.Message, from, Parties.Session(session), Fields(("text", text)), null, session);
        return (store.Send(session, text, from), e.Id);
    }

    public void Answer(string session, string decision, string text, string by = Parties.User)
    {
        Check(Kinds.Answer, Fields(("decision", decision), ("text", text), ("by", by)));
        var question = store.Decision(session, decision)?.Question;
        store.Events.Append(EventKinds.Answer, by, Parties.Session(session), Fields(("decision", decision), ("text", text), ("question", question)), Fields(("decision", decision)), session);
        store.SaveAnswer(session, decision, new Answer { Value = text, By = by });
    }

    public string Review(string session, string decision, string verdict, string text)
    {
        Check(Kinds.Review, Fields(("decision", decision), ("verdict", verdict), ("text", text)));
        var question = store.Decision(session, decision)?.Question ?? "question";
        var (target, versionId) = Target(session, decision);
        store.Events.Append(EventKinds.Review, Parties.User, Parties.Session(session), Fields(("decision", decision), ("verdict", verdict), ("text", text), ("version", versionId)), Fields(("decision", decision), ("version", versionId)), session);
        store.SaveReview(session, versionId, verdict, text);
        return store.Send(session, store.Texts.ReviewText(decision, question, verdict, text, target), Parties.Supervisor);
    }

    public (ReviewTarget Target, string VersionId) Target(string session, string decision)
    {
        var entry = store.Stack(session, decision);
        var current = entry?.Current;
        var version = current?.Version ?? 1;
        var branch = current?.Branch ?? StackNames.Branch(decision, 1);
        var baseBranch = current?.Base ?? $"the base branch of {branch}";
        var next = StackNames.Branch(decision, (entry?.Newest?.Version ?? version) + 1);
        return (new ReviewTarget(branch, baseBranch, next), StackNames.VersionId(decision, version));
    }

    public string RequireSession(string prefix) =>
        store.ResolveSession(prefix) ?? throw new MessageException($"no single session matches {prefix}");

    public string SessionOfDecision(string decision) =>
        store.SessionOf(decision) ?? throw new MessageException($"no session has decision {decision}");

    void Check(string kind, IReadOnlyDictionary<string, object?> fields)
    {
        var check = store.Templates.Validate(kind, fields);
        if (!check.IsValid) throw new MessageException($"{kind}: {check.Message}");
    }

    public static Dictionary<string, object?> Fields(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    public static JsonElement Json(IReadOnlyDictionary<string, object?> fields) => JsonSerializer.SerializeToElement(fields, StoreJson.Options);
}
