using Orchestrator.Core.Sessions;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;

namespace Orchestrator.Core.Workflows;

/// <summary>
/// The deterministic workflow runner (C2). It moves a run from step to step and keeps the run state in
/// <see cref="IRepository{T, TId}"/>. Traversal "valid, then pick": after a step, each next step checks its own
/// condition and the entry condition of its target. One valid step: go there. Many valid steps: the agent picks. No
/// valid step: the run stops and asks. The work ends when a step with <c>"done": true</c> finds that the definition of
/// done is true.
/// </summary>
public sealed class WorkflowRunner
{
    /// <summary>The maximum number of step visits of one run. A loop with no end stops the run here.</summary>
    public const int MaxVisits = 200;

    readonly IRepository<Doc<WorkflowRun>, string> runs;
    readonly Func<string, WorkflowDefinition?> definitions;
    readonly Dictionary<string, IStepExecutor> executors;
    readonly IRunSessions sessions;
    readonly IRunMessages messages;
    readonly TimeProvider clock;

    public WorkflowRunner(IRepository<Doc<WorkflowRun>, string> runs, Func<string, WorkflowDefinition?> definitions, IEnumerable<IStepExecutor> executors,
        IRunSessions sessions, IRunMessages messages, TimeProvider? clock = null)
    {
        this.runs = runs;
        this.definitions = definitions;
        this.executors = executors.ToDictionary(e => e.Kind);
        this.sessions = sessions;
        this.messages = messages;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>The executors of C2: tool, ask and agent.</summary>
    public static IReadOnlyList<IStepExecutor> Executors(IRunSessions sessions, IRunMessages messages, IEnumerable<IWorkflowTool>? tools = null) =>
        [new ToolExecutor(tools ?? []), new AskExecutor(messages), new AgentExecutor(sessions)];

    string Now => Leases.Times.Format(clock.GetUtcNow());

    public WorkflowRun? Get(string id) => runs.Read(id);

    public WorkflowRun Require(string id) => Get(id) ?? Find(id) ?? throw new WorkflowException($"no run {id}");

    public IReadOnlyList<WorkflowRun> List(Func<WorkflowRun, bool>? where = null) =>
        runs.List(d => where is null || where(d.Value)).Select(d => d.Value).OrderBy(r => r.Created?.At, StringComparer.Ordinal).ToList();

    /// <summary>A run by the first characters of its id, when only one run matches.</summary>
    WorkflowRun? Find(string prefix)
    {
        var found = List(r => r.Id.StartsWith(prefix, StringComparison.Ordinal));
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// Starts a run of <paramref name="workflow"/> that <paramref name="owner"/> owns. The owner gets the role
    /// orchestrator. The entry step starts at once.
    /// </summary>
    public WorkflowRun Start(string workflow, string owner, IReadOnlyDictionary<string, string>? input, string by)
    {
        var definition = definitions(workflow) ?? throw new WorkflowException($"no workflow {workflow}");
        if (sessions.Get(owner) is null) throw new WorkflowException($"no session {owner}");
        var id = $"{workflow}-{Guid.NewGuid().ToString("N")[..8]}";
        var stamp = new Stamp(Now, by);
        var run = new WorkflowRun(id, definition.Name, definition.Version, owner)
        {
            Input = input?.ToDictionary() ?? [],
            Created = stamp,
            Updated = stamp,
        };
        if (!runs.TryCreate(id, run)) throw new WorkflowException($"run {id} exists");
        sessions.SetRole(owner, Roles.Orchestrator, definition.Name);
        run = Enter(run, definition, definition.Entry, by);
        return Save(run, by);
    }

    /// <summary>
    /// The step command: gives the result of the current step, the verdict of an approval, or the pick of a next step.
    /// With no input, it checks a waiting ask step, tool step or approval for its answer.
    /// </summary>
    public WorkflowRun Step(string id, StepResult? input, string by)
    {
        var run = Require(id);
        if (!run.IsOpen) throw new WorkflowException($"run {run.Id} is {run.Status}: {run.Reason}");
        var definition = Definition(run);
        var step = definition.Step(run.Step!) ?? throw new WorkflowException($"run {run.Id}: the workflow has no step {run.Step}");
        var wait = run.Wait;
        run = wait?.Kind switch
        {
            WaitKinds.Approval => Approval(run, definition, step, input, by),
            WaitKinds.Pick => Pick(run, definition, step, input?.Pick, wait.Options ?? [], by),
            WaitKinds.Stuck => Pick(run, definition, step, input?.Pick ?? FirstWord(wait.Thread is { } t ? messages.Answer(t) : null), definition.Steps.Select(s => s.Id).ToList(), by),
            _ => Result(run, definition, step, input, by),
        };
        return Save(run, by);
    }

    /// <summary>Stops an open run. The run keeps its history.</summary>
    public WorkflowRun Stop(string id, string reason, string by)
    {
        var run = Require(id);
        if (!run.IsOpen) return run;
        return Save(run with { Status = RunStates.Stopped, Wait = null, Reason = reason }, by);
    }

    WorkflowDefinition Definition(WorkflowRun run) => definitions(run.Workflow) ?? throw new WorkflowException($"run {run.Id}: no workflow {run.Workflow}");

    WorkflowRun Save(WorkflowRun run, string by)
    {
        run = run with { Updated = new Stamp(Now, by) };
        runs.Put(run.Id, run);
        return run;
    }

    /// <summary>Starts a visit of a step and calls its executor.</summary>
    WorkflowRun Enter(WorkflowRun run, WorkflowDefinition definition, string stepId, string by)
    {
        var step = definition.Step(stepId) ?? throw new WorkflowException($"run {run.Id}: no step {stepId}");
        if (run.History.Count >= MaxVisits)
            return run with { Status = RunStates.Stopped, Wait = null, Reason = $"the run made {MaxVisits} step visits: a loop with no end" };
        var executor = Executor(step);
        run = run with { Status = RunStates.Running, Step = step.Id, Wait = null, History = [.. run.History, new RunStep(step.Id, step.Executor, Now)] };
        var start = executor.Start(Context(run, definition, step));
        run = WithCurrent(run, v => v with { Session = start.Session ?? v.Session, Thread = start.Thread ?? v.Thread });
        if (start.Result is { } result) return Complete(run, definition, step, result, by);
        return run with { Status = RunStates.Waiting, Wait = start.Wait };
    }

    /// <summary>The result of the current step: from the input of the step command, else from the poll of its executor.</summary>
    WorkflowRun Result(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, StepResult? input, string by)
    {
        var result = input is not null && HasContent(input) ? input : Executor(step).Poll(Context(run, definition, step));
        if (result is null) return run;
        if (input is not null && result != input) result = Merge(result, input);
        if (step.Executor == WorkflowStep.Ask && result.Answer is { } answer)
        {
            // The answer of an ask step is recorded in the store: that meets the exit condition. With an approval, the answer is also the verdict.
            if (step.Exit is { } exit) result = result with { Holds = [.. result.Holds, exit] };
            if (step.Approval is not null && result.Verdict is null) result = result with { Verdict = Verdicts.Parse(answer) };
        }
        return Complete(run, definition, step, result, by);
    }

    static bool HasContent(StepResult r) =>
        r.Holds.Count > 0 || r.Output.Count > 0 || r.Pick is not null || r.Answer is not null || r.Verdict is not null || r.SkipApproval || r.Done;

    static StepResult Merge(StepResult a, StepResult b) => a with
    {
        Holds = [.. a.Holds, .. b.Holds],
        Output = a.Output.Concat(b.Output).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value),
        Pick = b.Pick ?? a.Pick,
        Verdict = b.Verdict ?? a.Verdict,
        SkipApproval = a.SkipApproval || b.SkipApproval,
        Done = a.Done || b.Done,
    };

    /// <summary>Records the result, then checks the exit condition, the approval, the definition of done and the next steps.</summary>
    WorkflowRun Complete(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, StepResult result, string by)
    {
        run = WithCurrent(run, v => v with
        {
            Holds = v.Holds.Concat(result.Holds).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Output = v.Output.Concat(result.Output).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value),
            Answer = result.Answer ?? v.Answer,
        });
        run = run with { Outputs = run.Outputs.Concat(result.Output).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value) };
        var visit = run.Current!;
        if (step.Exit is { } exit && !Holds(visit.Holds, exit))
            return run with { Status = RunStates.Waiting, Wait = new RunWait(WaitKinds.Exit, $"The exit condition of step {step.Id} is not true: \"{exit}\".") { Session = visit.Session ?? run.Owner } };
        return Approve(run, definition, step, result, by);
    }

    WorkflowRun Approve(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, StepResult result, string by)
    {
        if (step.Approval is { } approval && run.Current!.Approval is null)
        {
            string? outcome = null;
            if (approval.Strategy == ApprovalStrategies.Ignored) outcome = ApprovalStrategies.Ignored;
            else if (result.Verdict is { } verdict)
            {
                if (!Verdicts.All.Contains(verdict)) throw new WorkflowException($"verdict {verdict} is unknown (known: {string.Join(", ", Verdicts.All)})");
                outcome = verdict;
            }
            else if (result.SkipApproval)
            {
                if (approval.Strategy != ApprovalStrategies.Skippable) throw new WorkflowException($"step {step.Id}: the approval is {approval.Strategy}. Only a skippable approval can be skipped");
                outcome = "skipped";
            }
            if (outcome is null)
            {
                var thread = messages.Ask(run.Owner, approval.By, $"Approve the step \"{step.Title}\" of run {run.Id}?", Context(run, definition, step).Prompt, Verdicts.All.ToList());
                run = WithCurrent(run, v => v with { Thread = thread });
                return run with
                {
                    Status = RunStates.Waiting,
                    Wait = new RunWait(WaitKinds.Approval, $"The step {step.Id} waits for the approval of {approval.By} ({approval.Strategy}).") { By = approval.By, Options = Verdicts.All.ToList(), Thread = thread },
                };
            }
            run = WithCurrent(run, v => v with { Approval = outcome });
            if (outcome == Verdicts.Refuse) return run with { Status = RunStates.Stopped, Wait = null, Reason = $"{approval.By} refused the step {step.Id}" };
        }
        return Route(run, definition, step, result.Pick, result.Done, by);
    }

    /// <summary>A waiting approval: the verdict or the skip of the step command, else the answer of the approval question.</summary>
    WorkflowRun Approval(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, StepResult? input, string by)
    {
        var verdict = input?.Verdict ?? (input?.SkipApproval == true ? null : run.Wait?.Thread is { } t ? Verdicts.Parse(messages.Answer(t)) : null);
        if (verdict is null && input?.SkipApproval != true) return run;
        return Approve(run with { Wait = null }, definition, step, new StepResult { Verdict = verdict, SkipApproval = input?.SkipApproval == true, Pick = input?.Pick }, by);
    }

    /// <summary>The definition of done, then "valid, then pick".</summary>
    WorkflowRun Route(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, string? pick, bool done, string by)
    {
        var facts = Facts(run.Current!);
        if (step.Done && (done || Holds(facts, definition.Done)))
        {
            run = WithCurrent(run, v => v with { Ended = Now });
            messages.Notify(run.Owner, $"The run {run.Id} of workflow {run.Workflow} is done at step {step.Id}: {definition.Done}");
            return run with { Status = RunStates.Done, Wait = null, Reason = "the definition of done is true" };
        }
        var valid = Valid(definition, step, facts);
        if (pick is not null && valid.Contains(pick)) return Go(run, definition, pick, by);
        if (valid.Count == 1) return Go(run, definition, valid[0], by);
        if (valid.Count > 1)
        {
            var session = run.Current!.Session ?? run.Owner;
            messages.Notify(session, $"The run {run.Id} has more than one valid next step after {step.Id}: {string.Join(", ", valid)}. Pick one: orch run step {run.Id} --pick <step>");
            return run with { Status = RunStates.Waiting, Wait = new RunWait(WaitKinds.Pick, $"More than one next step is valid after {step.Id}. The agent picks.") { Options = [.. valid], Session = session } };
        }
        var options = (step.Next ?? []).Select(n => n.Step).Distinct().ToList();
        var text = options.Count == 0
            ? $"The run {run.Id} stopped at step {step.Id}: the step has no next step, and the definition of done is not true."
            : $"The run {run.Id} stopped at step {step.Id}: no next step is valid.";
        var thread = messages.Ask(run.Owner, "user", $"{text} Which step comes next?", Describe(definition, step, facts), options.Count is >= 2 and <= 4 ? options : null);
        run = WithCurrent(run, v => v with { Thread = thread });
        return run with { Status = RunStates.Waiting, Wait = new RunWait(WaitKinds.Stuck, text) { Options = options, Thread = thread, Session = run.Owner } };
    }

    WorkflowRun Pick(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step, string? pick, IReadOnlyList<string> allowed, string by)
    {
        if (pick is null) return run;
        if (!allowed.Contains(pick)) throw new WorkflowException($"run {run.Id}: {pick} is not a valid next step (valid: {string.Join(", ", allowed)})");
        return Go(run with { Wait = null }, definition, pick, by);
    }

    WorkflowRun Go(WorkflowRun run, WorkflowDefinition definition, string next, string by)
    {
        run = WithCurrent(run, v => v with { Next = next, Ended = Now });
        return Enter(run, definition, next, by);
    }

    /// <summary>The next steps whose condition and whose target entry condition are true, in the order of the definition.</summary>
    public static IReadOnlyList<string> Valid(WorkflowDefinition definition, WorkflowStep step, IReadOnlyCollection<string> facts) =>
        (step.Next ?? [])
            .Where(n => Holds(facts, n.When) && Holds(facts, definition.Step(n.Step)?.When))
            .Select(n => n.Step)
            .Distinct()
            .ToList();

    /// <summary>The true conditions of a visit: the holds, and the verdict of the approval.</summary>
    static List<string> Facts(RunStep visit) =>
        visit.Approval is { } a && Verdicts.All.Contains(a) ? [.. visit.Holds, Verdicts.Fact(a)] : visit.Holds;

    /// <summary>A condition with no text is always true. Else the facts must hold the same text (case and outer spaces do not count).</summary>
    public static bool Holds(IReadOnlyCollection<string> facts, string? condition) =>
        condition is null || facts.Any(f => Same(f, condition));

    static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    static string Norm(string s) => s.Trim().TrimEnd('.').Trim().Trim('"');

    static string? FirstWord(string? text) =>
        text is null ? null : new string(text.TrimStart().TakeWhile(c => char.IsLetterOrDigit(c) || c == '-').ToArray()).ToLowerInvariant() is { Length: > 0 } w ? w : null;

    IStepExecutor Executor(WorkflowStep step) =>
        executors.TryGetValue(step.Executor, out var e) ? e : throw new WorkflowException($"step {step.Id}: no executor {step.Executor}");

    StepContext Context(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step) => new(run, definition, step, RunPrompt.For(run, definition, step));

    static string Describe(WorkflowDefinition definition, WorkflowStep step, IReadOnlyCollection<string> facts) =>
        $"True conditions: {(facts.Count == 0 ? "none" : string.Join("; ", facts))}. Next steps: "
        + string.Join("; ", (step.Next ?? []).Select(n => $"{n.Step} when \"{n.When ?? "always"}\"" + (definition.Step(n.Step)?.When is { } w ? $" and \"{w}\"" : "")));

    static WorkflowRun WithCurrent(WorkflowRun run, Func<RunStep, RunStep> change) =>
        run.History.Count == 0 ? run : run with { History = [.. run.History.Take(run.History.Count - 1), change(run.History[^1])] };
}
