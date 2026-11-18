using System.Text.Json;
using Orchestrator.Core.Sessions;
using Orchestrator.Core.Templates;

namespace Orchestrator.Core.Workflows;

/// <summary>What an executor gets: the run, the definition, the step and the prompt that the runner made for the step.</summary>
public sealed record StepContext(WorkflowRun Run, WorkflowDefinition Workflow, WorkflowStep Step, string Prompt);

/// <summary>
/// The result of a step. The conditions are text (F2-2): <see cref="Holds"/> lists each condition that is true after
/// the step. The runner compares the texts with the <c>when</c> and <c>exit</c> conditions of the definition.
/// </summary>
public sealed record StepResult
{
    public IReadOnlyList<string> Holds { get; init; } = [];

    public IReadOnlyDictionary<string, string> Output { get; init; } = new Dictionary<string, string>();

    /// <summary>The next step that the agent picks when more than one next step is valid, or when the run is stuck.</summary>
    public string? Pick { get; init; }

    /// <summary>The answer of a person to an ask step.</summary>
    public string? Answer { get; init; }

    /// <summary>The verdict of the approval: approve, change or refuse.</summary>
    public string? Verdict { get; init; }

    /// <summary>True: skip a skippable approval.</summary>
    public bool SkipApproval { get; init; }

    /// <summary>True: the definition of done of the workflow is true. A step with <c>"done": true</c> checks it.</summary>
    public bool Done { get; init; }
}

/// <summary>The start of a step: a result at once, or a wait.</summary>
public sealed record StepStart(StepResult? Result, RunWait? Wait)
{
    /// <summary>The session that does an agent step.</summary>
    public string? Session { get; init; }

    /// <summary>The question thread of an ask step.</summary>
    public string? Thread { get; init; }

    public static StepStart Now(StepResult result) => new(result, null);
}

/// <summary>One kind of step executor: tool, ask or agent.</summary>
public interface IStepExecutor
{
    /// <summary>The executor name of the definition: <see cref="WorkflowStep.Agent"/>, <see cref="WorkflowStep.Ask"/> or <see cref="WorkflowStep.Tool"/>.</summary>
    string Kind { get; }

    StepStart Start(StepContext context);

    /// <summary>The result of a waiting step when it is ready, else null. The runner calls it at each step command.</summary>
    StepResult? Poll(StepContext context);
}

/// <summary>A deterministic tool of a tool step. The step names it in <c>tool</c>.</summary>
public interface IWorkflowTool
{
    string Name { get; }

    /// <summary>The result of the tool, or null when the tool cannot finish now.</summary>
    StepResult? Run(StepContext context);
}

/// <summary>The spec of a session that a run starts.</summary>
public sealed record RunSessionSpec(string Title, string Goal, string Parent, string Role, string Workflow)
{
    /// <summary>The custom agent of the session. Null: the default agent of the role.</summary>
    public string? Type { get; init; }

    public string? DoneWhen { get; init; }
}

/// <summary>
/// The sessions of a run. The implementation starts each session through the session registry (S2); the supervisor
/// then runs its turns through <c>IProvider</c>. No runner code names a provider.
/// </summary>
public interface IRunSessions
{
    SessionEntry? Get(string id);

    /// <summary>The work tree of a session: its root and each session under the root.</summary>
    IReadOnlyList<SessionEntry> Tree(string id);

    /// <summary>Starts a new session. Its brief carries the step prompt. Returns the session id.</summary>
    string Start(RunSessionSpec spec);

    /// <summary>Sends the step prompt to a session that exists. The next turn of the session gets it.</summary>
    void Prompt(string session, string text);

    /// <summary>Sets the role and the workflow of a session (C1: the run gives the role).</summary>
    void SetRole(string session, string role, string? workflow);
}

/// <summary>The questions and the notices of a run.</summary>
public interface IRunMessages
{
    /// <summary>Puts a question from <paramref name="from"/> to the party <paramref name="to"/>. Returns the question thread.</summary>
    string Ask(string from, string to, string question, string context, IReadOnlyList<string>? options);

    /// <summary>The text of the first answer of a question thread, or null.</summary>
    string? Answer(string thread);

    /// <summary>A free text message to a session.</summary>
    void Notify(string session, string text);
}

/// <summary>A tool step: the runner calls the named <see cref="IWorkflowTool"/>.</summary>
public sealed class ToolExecutor(IEnumerable<IWorkflowTool> tools) : IStepExecutor
{
    readonly Dictionary<string, IWorkflowTool> tools = tools.ToDictionary(t => t.Name);

    public string Kind => WorkflowStep.Tool;

    public StepStart Start(StepContext context) =>
        Poll(context) is { } result ? StepStart.Now(result) : new StepStart(null, new RunWait(WaitKinds.Tool, $"The tool {context.Step.ToolName} gave no result. Run the step again."));

    public StepResult? Poll(StepContext context) =>
        context.Step.ToolName is { } name && tools.TryGetValue(name, out var tool)
            ? tool.Run(context)
            : throw new WorkflowException($"step {context.Step.Id}: no tool \"{context.Step.ToolName}\" (known: {string.Join(", ", tools.Keys.Order())})");
}

/// <summary>
/// An ask step: one question to a person with the question template. The approval party of the step gets it, else
/// the user. The answer of the question thread completes the step. For a step with an approval, the answer is also the
/// verdict.
/// </summary>
public sealed class AskExecutor(IRunMessages messages) : IStepExecutor
{
    public string Kind => WorkflowStep.Ask;

    public StepStart Start(StepContext context)
    {
        var step = context.Step;
        var to = step.Approval?.By ?? "user";
        var options = step.Approval is not null ? RunVerdicts.All.ToList() : null;
        var thread = messages.Ask(context.Run.Owner, to, $"{step.Title}: {step.Instructions}", context.Prompt, options);
        return new StepStart(null, new RunWait(WaitKinds.Ask, $"The step {step.Id} waits for the answer of {to}.") { Thread = thread, By = to, Options = options }) { Thread = thread };
    }

    public StepResult? Poll(StepContext context) =>
        context.Run.Current?.Thread is { } thread && messages.Answer(thread) is { } answer
            ? new StepResult { Answer = answer }
            : null;
}

/// <summary>
/// An agent step: one turn of a session. The step names the session: the owner, an existing session, a new session,
/// or a session of a type (found in the work tree of the owner, else started). The session ends the step with
/// <c>orch run step</c>.
/// </summary>
public sealed class AgentExecutor(IRunSessions sessions) : IStepExecutor
{
    public const string Owner = "owner";
    public const string New = "new";

    public string Kind => WorkflowStep.Agent;

    public StepStart Start(StepContext context)
    {
        var run = context.Run;
        var step = context.Step;
        var (session, started) = Resolve(context);
        if (session != run.Owner)
        {
            // A session that owns an open run keeps the role orchestrator (C1, T3d).
            if (sessions.Get(session)?.Role != Roles.Orchestrator) sessions.SetRole(session, Roles.Worker, run.Workflow);
        }
        if (!started) sessions.Prompt(session, context.Prompt);
        return new StepStart(null, new RunWait(WaitKinds.Agent, $"The step {step.Id} waits for a turn of session {session}.") { Session = session }) { Session = session };
    }

    /// <summary>An agent step has no poll: the session ends the step with a step command.</summary>
    public StepResult? Poll(StepContext context) => null;

    (string Session, bool Started) Resolve(StepContext context)
    {
        var run = context.Run;
        var step = context.Step;
        var target = step.Session;
        if (target is null || target.Value.ValueKind == JsonValueKind.String && target.Value.GetString() == Owner) return (run.Owner, false);
        var spec = new RunSessionSpec($"{run.Workflow}: {step.Title}", context.Prompt, run.Owner, Roles.Worker, run.Workflow)
        {
            DoneWhen = step.Exit ?? $"The step {step.Id} of run {run.Id} is ended with orch run step.",
        };
        if (target.Value.ValueKind == JsonValueKind.String && target.Value.GetString() == New) return (sessions.Start(spec), true);
        if (target.Value.ValueKind == JsonValueKind.Object && target.Value.TryGetProperty("session", out var fixedId))
        {
            var id = fixedId.GetString()!;
            if (sessions.Get(id) is null) throw new WorkflowException($"step {step.Id}: no session {id}");
            return (id, false);
        }
        if (target.Value.ValueKind == JsonValueKind.Object && target.Value.TryGetProperty("type", out var typeName))
        {
            var type = typeName.GetString()!;
            var found = sessions.Tree(run.Owner)
                .Where(e => e.Id != run.Owner && e.Lifecycle.Phase == Contract.ControlStates.Active && SameType(e.Type?.Agent, type))
                .OrderBy(e => e.Created?.At, StringComparer.Ordinal)
                .FirstOrDefault();
            return found is not null ? (found.Id, false) : (sessions.Start(spec with { Type = type }), true);
        }
        throw new WorkflowException($"step {step.Id}: the session {target.Value.GetRawText()} is unknown");
    }

    /// <summary>A type matches with or without a namespace prefix: <c>decider</c> matches <c>orch:decider</c>.</summary>
    static bool SameType(string? agent, string type) => agent is not null && (agent == type || agent.Split(':')[^1] == type.Split(':')[^1]);
}

public sealed class WorkflowException(string message) : Exception(message);
