using System.Text.Json;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain;

public sealed record RunStats(long Ms, double Cost, int Calls, int Escalations, string? Error);

public sealed record EngineRunResult(string WorkDir, string OutputFile, RunTrace Trace, RunStats Stats, PatternCatalog Catalog);

/// <summary>
/// One engine run in its own folder: the folder gets a copy of the patterns and the subject rules, and the run writes
/// the output, trace.json and agent.log.json there. The agent calls run in that folder.
/// </summary>
public static class EngineRun
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Prepare(PackContext pack, string workDir)
    {
        var patterns = Path.Combine(workDir, "patterns");
        Directory.CreateDirectory(patterns);
        foreach (var file in Directory.GetFiles(Path.Combine(pack.Dir, "patterns"), "*.md"))
        {
            File.Copy(file, Path.Combine(patterns, Path.GetFileName(file)), true);
        }

        File.WriteAllText(Path.Combine(workDir, "instructions.md"), pack.Instructions);
    }

    public static async Task<EngineRunResult> RunAsync(PackContext pack, string request, string workDir, EngineSettings settings, IJudge judge, IUserChannel user, CancellationToken ct)
    {
        var agents = new AgentGateway(judge, workDir, settings.Run);
        var context = new SolveContext(pack with { Catalog = PatternCatalog.Load(Path.Combine(workDir, "patterns")) }, settings, agents, user, workDir);
        string? error = null;
        SolveOutcome outcome;
        try
        {
            outcome = await Solver.RunAsync(context, request, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            error = $"engine: {e.Message}";
            outcome = new SolveOutcome(string.Empty, TraceBuilder.Build(context, context.Nodes.Count > 0 ? context.Nodes[0] : context.Add(request, [], 0, null)), false, [error]);
        }

        var outputFile = Path.Combine(workDir, pack.Pack.OutputFile);
        if (outcome.Output.Length > 0)
        {
            File.WriteAllText(outputFile, outcome.Output);
        }

        File.WriteAllText(Path.Combine(workDir, "trace.json"), JsonSerializer.Serialize(outcome.Trace, Json));
        File.WriteAllText(Path.Combine(workDir, "agent.log.json"), JsonSerializer.Serialize(new { calls = agents.Calls, cost = agents.Cost, log = agents.Log }, Json));
        var escalations = context.Nodes.Sum(n => n.Escalations);
        error ??= outcome.Output.Length == 0 && outcome.Trace.Outcome != Cases.Domain.Shared.Outcomes.Ask ? "no output" : null;
        return new EngineRunResult(workDir, outputFile, outcome.Trace, new RunStats(agents.ElapsedMs, agents.Cost, agents.Calls, escalations, error), context.Catalog);
    }
}
