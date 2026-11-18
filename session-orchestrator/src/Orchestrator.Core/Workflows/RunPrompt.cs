using System.Text;
using Orchestrator.Core.Templates;

namespace Orchestrator.Core.Workflows;

/// <summary>
/// The prompt of one step: the instructions, the input, the output, the templates, the messages, and the conditions
/// that the session must report with <c>orch run step</c>. The runner gives it to the executor of the step.
/// </summary>
public static class RunPrompt
{
    public static string For(WorkflowRun run, WorkflowDefinition definition, WorkflowStep step)
    {
        var text = new StringBuilder();
        text.AppendLine($"Workflow run {run.Id} (workflow {run.Workflow}), step {step.Id}: {step.Title}.");
        text.AppendLine(step.Instructions);
        foreach (var (key, value) in run.Input) text.AppendLine($"Run input {key}: {value}");
        if (step.Input is { Count: > 0 } input)
        {
            text.AppendLine($"Input: {string.Join(", ", input)}.");
            foreach (var name in input)
                if (run.Outputs.TryGetValue(name, out var value)) text.AppendLine($"- {name}: {value}");
        }
        if (step.Output is { Count: > 0 } output) text.AppendLine($"Output: {string.Join(", ", output)}. Give each one with --output-<name> \"text\", or write it where the instructions say.");
        if (step.Templates is { Count: > 0 } templates) text.AppendLine($"Templates: {string.Join(", ", templates)}.");
        foreach (var m in step.Messages ?? []) text.AppendLine($"Send a {m.Kind} to {m.To}{(m.When is { } w ? $" when {w}" : "")}.");
        if (step.Approval is { } a) text.AppendLine($"Approval: {a.By}, {a.Strategy}.");
        var conditions = Conditions(definition, step).ToList();
        if (conditions.Count > 0)
        {
            text.AppendLine("At the end of the step, give each condition that is true, with the same words:");
            foreach (var (condition, use) in conditions) text.AppendLine($"- \"{condition}\" ({use})");
        }
        text.Append($"To end the step, run: orch run step {run.Id} --holds \"<condition>; <condition>\"");
        if ((step.Next?.Count ?? 0) > 1) text.Append(" [--pick <next step>]");
        text.AppendLine();
        return text.ToString().TrimEnd();
    }

    /// <summary>The conditions that the runner checks after the step, each with its use.</summary>
    public static IEnumerable<(string Condition, string Use)> Conditions(WorkflowDefinition definition, WorkflowStep step)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (step.Exit is { } exit && seen.Add(exit)) yield return (exit, "the exit condition of this step");
        if (step.Done && seen.Add(definition.Done)) yield return (definition.Done, "the definition of done");
        foreach (var next in step.Next ?? [])
        {
            if (next.When is { } when && seen.Add(when)) yield return (when, $"go to {next.Step}");
            if (definition.Step(next.Step)?.When is { } entry && seen.Add(entry)) yield return (entry, $"the entry condition of {next.Step}");
        }
    }
}
