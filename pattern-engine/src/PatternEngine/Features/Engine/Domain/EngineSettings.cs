using PatternEngine.Plugins.Settings;

namespace PatternEngine.Features.Engine.Domain;

public sealed record NodeLimits(int MaxDepth, int ComposeRetries, int Escalations);

public sealed record RunLimits(int AgentCalls, double CostUsd, int Seconds);

public sealed record Triggers(bool Unclear, bool NearTie, double NearTieMargin, bool NotAllowed, bool RetriesUsedUp, bool OutOfDomain)
{
    public bool Any => Unclear || NearTie || NotAllowed || RetriesUsedUp || OutOfDomain;
}

public sealed record QuestionSettings(bool AskMissingRequired, int MaxRounds);

/// <summary>
/// All values of the engine, from pe.settings.yaml (engine section), merged with the pack settings.
/// Decider: status (one decider call for each node, agents/decider.md) or compose (decomposer, compose loop and grader, B1).
/// </summary>
public sealed record EngineSettings(bool AllowNoFit, int Concurrency, NodeLimits Node, RunLimits Run, Triggers Triggers, QuestionSettings Questions, string Decider = EngineSettings.StatusDecider)
{
    public const string StatusDecider = "status";

    public const string ComposeDecider = "compose";

    public static EngineSettings From(Settings settings)
    {
        var e = settings.Section("engine");
        var node = e.Section("limits", "node");
        var run = e.Section("limits", "run");
        var t = e.Section("triggers");
        var q = e.Section("questions");
        return new EngineSettings(
            e.Flag("allow_no_fit"),
            e.Int("concurrency"),
            new NodeLimits(node.Int("max_depth"), node.Int("compose_retries"), node.Int("escalations")),
            new RunLimits(run.Int("agent_calls"), run.Number("cost_usd"), run.Int("seconds")),
            new Triggers(t.Flag("unclear"), t.Flag("near_tie"), t.Number("near_tie_margin"), t.Flag("not_allowed"), t.Flag("retries_used_up"), t.Flag("out_of_domain")),
            new QuestionSettings(q.Flag("ask_missing_required"), q.Int("max_rounds")),
            e.Text("decider"));
    }

    /// <summary>The same settings with every escalation trigger turned off (an ablation).</summary>
    public EngineSettings WithoutEscalation() => this with { Triggers = new Triggers(false, false, Triggers.NearTieMargin, false, false, false) };
}
