using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Sessions;
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
    public bool? Background { get; init; }

    /// <summary>The stream id (C1). Null: the stream of the parent.</summary>
    public string? Stream { get; init; }

    /// <summary>The custom agent of the session (C1). It replaces <see cref="Agent"/>, the old name, which stays valid.</summary>
    public string? Type { get; init; }

    /// <summary>The workflow that the session runs or works in (C1).</summary>
    public string? Workflow { get; init; }
}

/// <summary>An answer of the docs check or the policy: why it applies, and where it comes from.</summary>
public sealed record AnswerSource(string By)
{
    public string? Reason { get; init; }
    public string? Source { get; init; }
    public string? OnDefault { get; init; }
}

/// <summary>The posts of the user and of the reactors, one method for each message type.</summary>
public sealed class Messages(FileStore store)
{
    /// <param name="role">The role of the new session. Null: <see cref="Roles.Initial"/>. A workflow run gives it.</param>
    public string NewSession(BriefFields brief, string createdBy, string from = Parties.User, string? role = null)
    {
        var fields = Fields(("title", brief.Title), ("goal", brief.Goal), ("scope", brief.Scope), ("doneWhen", brief.DoneWhen),
            ("skills", brief.Skills), ("agent", brief.Agent), ("cwd", brief.Cwd), ("worktree", brief.Worktree), ("parent", brief.Parent), ("allowDefaults", brief.AllowDefaults), ("background", brief.Background),
            ("stream", brief.Stream), ("type", brief.Type), ("workflow", brief.Workflow));
        Check(Kinds.Brief, fields);
        if (string.IsNullOrEmpty(brief.Cwd)) throw new MessageException("brief: cwd is necessary");
        var id = store.Create(new SessionSpec
        {
            Title = brief.Title,
            Parent = brief.Parent,
            Work = new SessionWork { Cwd = brief.Cwd, Worktree = brief.Worktree },
            Type = (brief.Type ?? brief.Agent) is null && brief.Skills is null ? null : new SessionType { Agent = brief.Type ?? brief.Agent, Skills = brief.Skills?.ToList() },
            Stream = brief.Stream,
            Workflow = brief.Workflow,
            Role = role,
            Settings = BriefSettings(brief),
            By = createdBy,
        }).Id;
        store.Events.Post(new PostInput(EventKinds.Brief, from, Parties.Session(id), Clean(fields)) { Session = id, Refs = brief.Parent is { } p ? Fields(("parent", p)) : null });
        return id;
    }

    /// <summary>
    /// The settings that a brief sets: <c>allowDefaults</c> (tier permissions) and <c>background</c> (tier behavior,
    /// visibility private). Each other value is a copy of the parent value.
    /// </summary>
    static SessionSettings BriefSettings(BriefFields brief)
    {
        var settings = new SessionSettings();
        if (brief.AllowDefaults is { } allow) settings = settings.With(SettingTiers.Permissions, SettingTiers.AllowDefaults, allow);
        if (brief.Background is { } background) settings = settings.With(SettingTiers.Behavior, SettingTiers.Visibility, background ? SettingTiers.Private : SettingTiers.Public);
        return settings;
    }

    /// <summary>Free text to a session. The sender is the user, a session or the supervisor.</summary>
    public Event Send(string session, string text, string from = Parties.User, string? via = null)
    {
        if (text.Trim().Length == 0) throw new MessageException("message: the text is empty");
        return store.Events.Post(new PostInput(EventKinds.Message, from, Parties.Session(session), Fields(("text", text))) { Session = session, Refs = via is null ? null : Fields((RefKeys.Via, via)) });
    }

    /// <summary>
    /// The answer to a question goes to the session. An answer of the docs or the policy goes to the user too,
    /// as a forward that needs a review.
    /// </summary>
    public Event Answer(string session, string decision, string text, string by = Parties.User, string? via = null, AnswerSource? source = null)
    {
        var asked = store.DecisionEvent(decision) ?? throw new MessageException($"no session has decision {decision}");
        var body = Fields(("decision", decision), ("text", text), ("by", by), ("question", store.Events.Fields(asked).GetValueOrDefault("question") as string),
            ("reason", source?.Reason), ("source", source?.Source), ("onDefault", source?.OnDefault));
        Check(Kinds.Answer, body);
        var refs = Fields((RefKeys.Reply, asked.Id), (RefKeys.Via, via));
        var answer = store.Events.Post(new PostInput(EventKinds.Answer, by, Parties.Session(session), Clean(body)) { Session = session, Thread = decision, Refs = refs });
        if (by is Parties.Docs or Parties.Policy) store.Events.Forward(answer, Parties.User, Needs.Review);
        return answer;
    }

    /// <summary>The verdict of the user on a default decision or a plan. It closes the card of the default answer or the plan.</summary>
    public Event Review(string session, string decision, string verdict, string text)
    {
        Check(Kinds.Review, Fields(("decision", decision), ("verdict", verdict), ("text", text)));
        var thread = store.Events.Thread(decision);
        var plan = thread.FirstOrDefault(e => e.Kind == EventKinds.Plan && !e.IsForward);
        Dictionary<string, object?> body;
        string? reply;
        if (plan is not null)
        {
            body = Fields(("decision", decision), ("verdict", verdict), ("text", text), ("question", store.Events.Fields(plan).GetValueOrDefault("title") as string), ("target", "plan"));
            reply = plan.Id;
        }
        else
        {
            var question = store.Decision(decision)?.Question ?? "question";
            var (target, versionId) = Target(session, decision);
            body = Fields(("decision", decision), ("verdict", verdict), ("text", text), ("question", question), ("version", versionId),
                ("branch", target.Branch), ("base", target.Base), ("nextBranch", target.NextBranch));
            reply = thread.LastOrDefault(e => e.Kind == EventKinds.Answer && e.To == Parties.User && e.Needs == Needs.Review)?.Id;
        }
        return store.Events.Post(new PostInput(EventKinds.Review, Parties.User, Parties.Session(session), Clean(body))
        {
            Session = session,
            Thread = decision,
            Refs = reply is null ? null : Fields((RefKeys.Reply, reply)),
        });
    }

    /// <summary>The user read a card that needs an ack, for example a done report.</summary>
    public Event Ack(string envelope)
    {
        var e = store.Events.Find(envelope) ?? throw new MessageException($"no envelope {envelope}");
        return store.Events.Ack(e, Parties.User, Parties.User, "read");
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

    static Dictionary<string, object?> Clean(Dictionary<string, object?> fields) => fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value);

    public static Dictionary<string, object?> Fields(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    public static JsonElement Json(IReadOnlyDictionary<string, object?> fields) => JsonSerializer.SerializeToElement(fields, StoreJson.Options);
}
