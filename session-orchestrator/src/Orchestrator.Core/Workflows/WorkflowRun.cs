using Orchestrator.Core.Sessions;

namespace Orchestrator.Core.Workflows;

/// <summary>
/// The run state of one workflow run: <c>runs/&lt;id&gt;.json</c> in the local folder store. Only
/// <see cref="WorkflowRunner"/> writes it. The run keeps a copy of the definition version that it started with.
/// </summary>
public sealed record WorkflowRun(string Id, string Workflow, int Version, string Owner)
{
    /// <summary><see cref="RunStates"/>: running, waiting, stopped or done.</summary>
    public string Status { get; init; } = RunStates.Running;

    /// <summary>The id of the current step.</summary>
    public string? Step { get; init; }

    /// <summary>Why the run waits, and for what. Null while the run is not waiting.</summary>
    public RunWait? Wait { get; init; }

    /// <summary>The input of the run, for example the goal. The prompt of each agent step names it.</summary>
    public Dictionary<string, string> Input { get; init; } = [];

    /// <summary>The outputs of all steps, by name. A later output of the same name replaces the earlier one.</summary>
    public Dictionary<string, string> Outputs { get; init; } = [];

    /// <summary>One entry for each visit of a step, oldest first. A loop gives more than one entry for a step.</summary>
    public List<RunStep> History { get; init; } = [];

    /// <summary>Why the run stopped or ended.</summary>
    public string? Reason { get; init; }

    public Stamp? Created { get; init; }

    public Stamp? Updated { get; init; }

    /// <summary>The visit of the current step: the last history entry.</summary>
    public RunStep? Current => History.Count > 0 ? History[^1] : null;

    public bool IsOpen => Status is RunStates.Running or RunStates.Waiting;
}

/// <summary>One visit of a step.</summary>
public sealed record RunStep(string Step, string Executor, string Started)
{
    /// <summary>The session that does an agent step.</summary>
    public string? Session { get; init; }

    /// <summary>The question thread of an ask step, or of a stop that asks.</summary>
    public string? Thread { get; init; }

    /// <summary>The conditions that are true after the step, as the executor gave them.</summary>
    public List<string> Holds { get; init; } = [];

    public Dictionary<string, string> Output { get; init; } = [];

    /// <summary>The answer of a person to an ask step.</summary>
    public string? Answer { get; init; }

    /// <summary>The approval of the step: the verdict, or <c>skipped</c> or <c>ignored</c>.</summary>
    public string? Approval { get; init; }

    /// <summary>The next step that the runner went to.</summary>
    public string? Next { get; init; }

    public string? Ended { get; init; }
}

/// <summary>Why a run waits. <see cref="Options"/> holds the valid next steps of a pick, or the verdicts of an approval.</summary>
public sealed record RunWait(string Kind, string Text)
{
    public List<string>? Options { get; init; }

    /// <summary>The session that must act, when one session must act.</summary>
    public string? Session { get; init; }

    /// <summary>The party that must approve.</summary>
    public string? By { get; init; }

    /// <summary>The question thread whose answer moves the run.</summary>
    public string? Thread { get; init; }
}

public static class RunStates
{
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Stopped = "stopped";
    public const string Done = "done";
}

/// <summary>The kinds of <see cref="RunWait"/>.</summary>
public static class WaitKinds
{
    /// <summary>An agent step waits for the turn of its session.</summary>
    public const string Agent = "agent";

    /// <summary>An ask step waits for the answer of a person.</summary>
    public const string Ask = "ask";

    /// <summary>A tool step waits: the tool gave no result.</summary>
    public const string Tool = "tool";

    /// <summary>The exit condition of the step is not true.</summary>
    public const string Exit = "exit";

    /// <summary>The step waits for an approval.</summary>
    public const string Approval = "approval";

    /// <summary>More than one next step is valid: the agent picks.</summary>
    public const string Pick = "pick";

    /// <summary>No next step is valid: the run stopped and asks.</summary>
    public const string Stuck = "stuck";
}

/// <summary>The approval strategies of a step (C1, W5).</summary>
public static class ApprovalStrategies
{
    /// <summary>The run waits for the verdict. Nobody can skip it.</summary>
    public const string Required = "required";

    /// <summary>The run waits for the verdict, but the owner or the user can skip it.</summary>
    public const string Skippable = "skippable";

    /// <summary>The run does not wait. The history records <c>ignored</c>.</summary>
    public const string Ignored = "ignored";
}

public static class RunVerdicts
{
    public const string Approve = "approve";
    public const string Change = "change";
    public const string Refuse = "refuse";

    public static readonly IReadOnlyList<string> All = [Approve, Change, Refuse];

    /// <summary>The condition text that a verdict makes true, for example <c>the verdict is approve</c>.</summary>
    public static string Fact(string verdict) => $"the verdict is {verdict}";

    /// <summary>The verdict at the start of a text, for example "approve, but ..." gives approve. Null: no verdict.</summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var first = new string(text.TrimStart().TakeWhile(char.IsLetter).ToArray()).ToLowerInvariant();
        return first switch
        {
            "approve" or "approved" or "yes" or "ok" => Approve,
            "change" or "changes" => Change,
            "refuse" or "refused" or "no" => Refuse,
            _ => null,
        };
    }
}
