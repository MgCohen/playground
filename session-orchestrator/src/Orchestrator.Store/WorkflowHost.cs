using Orchestrator.Core.Leases;
using Orchestrator.Core.Events;
using Orchestrator.Core.Sessions;
using Orchestrator.Core.Workflows;

namespace Orchestrator.Store;

/// <summary>
/// The store side of the workflow runner: the sessions through the session registry and <see cref="Messages"/>, and
/// the questions and notices through the event stream. A new session gets a brief; the supervisor then runs its turns
/// through <c>IProvider</c>.
/// </summary>
public sealed class WorkflowHost(FileStore store, string writer) : IRunSessions, IRunMessages
{
    /// <summary>The runner of the store: the run repository, the workflow catalog of the store root and the three executors.</summary>
    public static WorkflowRunner Runner(FileStore store, string writer, IEnumerable<IWorkflowTool>? tools = null)
    {
        var host = new WorkflowHost(store, writer);
        var workflows = Home.Workflows(store.Root);
        return new WorkflowRunner(store.Repositories.WorkflowRuns, workflows.Get, WorkflowRunner.Executors(host, host, tools), host, host, store.Clock);
    }

    public SessionEntry? Get(string id) => store.Registry.Get(id);

    public IReadOnlyList<SessionEntry> Tree(string id)
    {
        var root = store.Registry.Chain(id)[0].Id;
        var all = store.Registry.List();
        var tree = new List<SessionEntry>();
        var queue = new Queue<string>([root]);
        var seen = new HashSet<string>();
        while (queue.TryDequeue(out var current))
        {
            if (!seen.Add(current)) continue;
            if (all.FirstOrDefault(e => e.Id == current) is { } entry) tree.Add(entry);
            foreach (var child in all.Where(e => e.Parent == current)) queue.Enqueue(child.Id);
        }
        return tree;
    }

    public string Start(RunSessionSpec spec)
    {
        var parent = store.Registry.Get(spec.Parent);
        var brief = new BriefFields
        {
            Title = spec.Title,
            Goal = spec.Goal,
            DoneWhen = spec.DoneWhen,
            Type = spec.Type,
            Workflow = spec.Workflow,
            Parent = spec.Parent,
            Cwd = store.Cwd(spec.Parent) ?? parent?.Work.Cwd,
            Skills = ["orch-report"],
        };
        return new Messages(store).NewSession(brief, writer, Parties.Session(spec.Parent), spec.Role);
    }

    public void Prompt(string session, string text) => new Messages(store).Send(session, text, Parties.Supervisor);

    public void SetRole(string session, string role, string? workflow)
    {
        if (store.Registry.Exists(session)) store.Registry.Update(session, e => e with { Role = role, Workflow = workflow ?? e.Workflow }, writer);
    }

    /// <summary>
    /// Posts a question with the question template. The party <paramref name="to"/>: <c>user</c>, <c>orchestrator</c> or
    /// <c>owner</c> (the session <paramref name="from"/>), <c>parent</c> (the parent of that session), or a session id.
    /// Other names go to the user.
    /// </summary>
    public string Ask(string from, string to, string question, string context, IReadOnlyList<string>? options)
    {
        var thread = $"{RunThread}{Guid.NewGuid():N}";
        var target = Party(from, to);
        var sender = target == Parties.Session(from) ? Parties.Supervisor : Parties.Session(from);
        var body = new Dictionary<string, object?> { ["question"] = question, ["context"] = context };
        if (options is { Count: >= 2 and <= 4 }) body["options"] = options.ToList();
        store.Events.Post(new PostInput(EventKinds.Question, sender, target, body) { Session = from, Thread = thread, Needs = Needs.Answer });
        return thread;
    }

    public string? Answer(string thread) => store.Events.FirstAnswer(thread) is { } e ? store.Events.Fields(e).GetValueOrDefault("text") as string : null;

    public void Notify(string session, string text) => new Messages(store).Send(session, text, Parties.Supervisor);

    /// <summary>The newest open question of the session itself. A question of the runner has a <c>run-</c> thread and does not count.</summary>
    public string? OpenQuestion(string session, string since)
    {
        var from = Times.Parse(since) ?? DateTimeOffset.MinValue;
        return EventKinds.Questions.SelectMany(kind => store.Events.PostsFrom(Parties.Session(session), kind))
            .Where(e => e.Thread is { } t && !t.StartsWith(RunThread, StringComparison.Ordinal) && store.Events.FirstAnswer(t) is null)
            .Where(e => (Times.Parse(e.At) ?? DateTimeOffset.MinValue) >= from)
            .OrderBy(e => e.Id, Core.Ordering.Names.Order)
            .LastOrDefault()?.Thread;
    }

    /// <summary>
    /// Closes a run question: an answer of the supervisor in its thread, then the notice <c>question-closed</c> to the
    /// asker and to the session that got the question.
    /// </summary>
    public void Close(string thread, string reason)
    {
        if (store.DecisionEvent(thread) is not { } asked || store.Events.FirstAnswer(thread) is not null) return;
        var session = Parties.SessionId(asked.From) ?? asked.Session ?? "";
        new Messages(store).Answer(session, thread, reason, Parties.Supervisor);
        var fields = store.Texts.Notice("question-closed", ("title", store.Registry.Get(session)?.Title ?? session), ("child", session), ("decision", thread),
            ("question", store.Events.Fields(asked).GetValueOrDefault("question") as string ?? "(no question text)"), ("text", reason), ("by", Parties.Supervisor));
        // A notice goes only to a session. A question to the user closes its card with the answer; the asker gets the notice.
        foreach (var to in new[] { Parties.Session(session), asked.To }.Where(p => Parties.SessionId(p) is not null).Distinct())
            store.Events.Post(new PostInput(EventKinds.Notice, Parties.Supervisor, to, fields) { Session = Parties.SessionId(to), Thread = thread });
    }

    const string RunThread = "run-";

    string Party(string owner, string to) => to switch
    {
        Parties.User => Parties.User,
        "orchestrator" or "owner" => Parties.Session(owner),
        "parent" => store.Registry.Get(owner)?.Parent is { } p ? Parties.Session(p) : Parties.User,
        _ when store.Registry.Exists(to) => Parties.Session(to),
        _ => Parties.User,
    };
}
