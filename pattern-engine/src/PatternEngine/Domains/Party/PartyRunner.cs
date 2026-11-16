using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Domains.Party;

/// <summary>The options of a run. Spec and Lore apply to prose cases only: Spec decompose or given, Lore on or off.</summary>
public sealed record PartyRunOptions(string Mode, string Model, int K, IReadOnlyList<string> Cases, string Strategy, string? Label, string Spec = "decompose", bool Lore = true);

public sealed record PartyCaseRun(
    [property: JsonPropertyName("case")] string Case,
    [property: JsonPropertyName("trial")] int Trial,
    [property: JsonPropertyName("pass")] bool Pass,
    [property: JsonPropertyName("verdicts")] IReadOnlyList<EvalVerdict> Verdicts,
    [property: JsonPropertyName("measures")] IReadOnlyDictionary<string, string> Measures,
    [property: JsonPropertyName("grader_accuracy")] string GraderAccuracy,
    [property: JsonPropertyName("trace")] ComposeTrace Trace,
    [property: JsonPropertyName("decomposition")] Decomposition? Decomposition = null);

public sealed record GraderCheckLine(
    [property: JsonPropertyName("quest")] string Quest,
    [property: JsonPropertyName("party")] IReadOnlyList<string> Party,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("wrong")] IReadOnlyList<string> Wrong,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("raw")] string? Raw);

public sealed record PartyRunResult(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("calls")] int Calls,
    [property: JsonPropertyName("cost")] double Cost,
    [property: JsonPropertyName("runs")] IReadOnlyList<PartyCaseRun> Runs,
    [property: JsonPropertyName("grader_check")] IReadOnlyList<GraderCheckLine> GraderCheck,
    [property: JsonPropertyName("report")] string Report);

/// <summary>
/// Runs party cases with the compose loop, grades each run with the oracle, and writes one result file.
/// The judge comes from the caller: the CLI judge for small runs, a scripted judge in tests.
/// </summary>
public sealed class PartyRunner(PartyPack pack, Func<IJudge> judge, RunLimits limits, string workRoot)
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly PartyEval eval = new(pack);

    public async Task<PartyRunResult> RunAsync(PartyRunOptions options, Action<string> log, CancellationToken ct)
    {
        var runId = RunId(options.Label);
        var settings = ComposeSettings.From(pack.Settings) with { Strategy = options.Strategy };
        var runs = new List<PartyCaseRun>();
        var prose = options.Cases.Any(id => pack.Case(id).Rung == "prose") ? $", spec {options.Spec}{(options.Spec == "decompose" ? $", lore {(options.Lore ? "on" : "off")}" : string.Empty)}" : string.Empty;
        log($"Run {runId} [{options.Mode}]: model {options.Model}, strategy {options.Strategy}{prose}, {options.Cases.Count} cases x k={options.K}");
        foreach (var id in options.Cases)
        {
            var c = pack.Case(id);
            for (var trial = 1; trial <= options.K; trial++)
            {
                var dir = Path.Combine(workRoot, runId, $"{id}-{trial}");
                Directory.CreateDirectory(dir);
                var gateway = new AgentGateway(judge(), dir, limits);
                Decomposition? decomposition = null;
                IReadOnlyList<Features.Compose.Domain.Shared.Anchor>? specs = null;
                if (c.Rung == "prose")
                {
                    if (options.Spec == "given")
                    {
                        specs = [Decomposer.Given(c)];
                    }
                    else
                    {
                        decomposition = await DecomposeAsync(gateway, c, options.Lore, ct);
                        specs = decomposition.Anchor is { } a ? [a] : null;
                    }
                }

                var task = new PartyTask(pack, c, PartyTask.FieldsOf(pack, c.Rung), options.Strategy, specs);
                var trace = c.Rung == "prose" && specs is null
                    ? new ComposeTrace(c.Id, options.Strategy, "stopped", "the decomposer gave no valid spec", c.Quests.Select(q => new SetCommit(q.Id, null, "no spec")).ToList(), [], 0, 0, gateway.Calls, gateway.Cost, gateway.ElapsedMs)
                    : await new ComposeLoop(task, settings, gateway).RunAsync(ct);
                var result = eval.Grade(c, c.Quests.Select(q => trace.CommitOf(q.Id)).ToList(), decomposition?.Anchor);
                var run = new PartyCaseRun(id, trial, result.Pass, result.Verdicts, result.Measures, GraderAccuracy(task, trace), trace, decomposition);
                runs.Add(run);
                log(Line(run));
            }
        }

        var report = Report(runs);
        return new PartyRunResult(runId, "compose", options.Mode, options.Model, options.Strategy, runs.Sum(r => r.Trace.Calls), runs.Sum(r => r.Trace.Cost), runs, [], report);
    }

    /// <summary>Calls the decomposer, checks the answer with code, and calls again with the errors (party.decomposer.retries).</summary>
    public async Task<Decomposition> DecomposeAsync(AgentGateway gateway, PartyCase c, bool lore, CancellationToken ct)
    {
        var settings = pack.Settings.Section("party", "decomposer");
        var ranges = DecomposeRanges.From(settings);
        var retries = settings.Int("retries");
        IReadOnlyList<string> errors = [];
        AgentAnswer? answer = null;
        for (var attempt = 1; attempt <= retries + 1; attempt++)
        {
            answer = await gateway.CallAsync(Decomposer.Agent, Decomposer.Input(pack, c.Text!, lore, errors), ct);
            var (anchor, problems) = Decomposer.Build(pack, ranges, c.Id, c.Text!, answer);
            if (anchor is not null)
            {
                return new Decomposition(anchor.Rubrics.Select(r => r.Ask).ToList(), [], answer.Text("note"), lore, attempt) { Anchor = anchor };
            }

            errors = problems;
        }

        return new Decomposition([], errors, answer?.Text("note"), lore, retries + 1);
    }

    /// <summary>Grades the fixed pairs of grader-check.yaml with the grader agent and compares each verdict with the oracle.</summary>
    public async Task<PartyRunResult> GraderCheckAsync(PartyRunOptions options, Action<string> log, CancellationToken ct)
    {
        var runId = RunId(options.Label ?? "grader-check");
        var dir = Path.Combine(workRoot, runId);
        Directory.CreateDirectory(dir);
        var gateway = new AgentGateway(judge(), dir, limits);
        var pairs = Node.List(Node.Map(Yaml.Parse(File.ReadAllText(Path.Combine(pack.Dir, "grader-check.yaml")))).Raw("pairs")).Select(Node.Map).ToList();
        log($"Grader check {runId}: model {options.Model}, {pairs.Count} pairs");
        using var gate = new SemaphoreSlim(Math.Max(1, ComposeSettings.From(pack.Settings).Concurrency));
        var lines = await Task.WhenAll(pairs.Select(async p =>
        {
            var c = pack.Case(p.Get("quest")!);
            var party = Node.Strings(p.Raw("party"));
            var task = new PartyTask(pack, c, PartyTask.FieldsOf(pack, c.Rung), options.Strategy);
            var quest = c.Quests[0];
            AgentAnswer answer;
            await gate.WaitAsync(ct);
            try
            {
                answer = await gateway.CallAsync(ComposeLoop.GraderAgent, ComposeLoop.GraderInput(task, quest, party), ct);
            }
            finally
            {
                gate.Release();
            }

            var verdicts = ComposeLoop.ReadVerdicts(answer, quest.Visible);
            var heroes = party.Select(pack.Heroes.Get).OfType<Hero>().ToList();
            var wrong = quest.Visible.Zip(verdicts).Where(x => x.Second.Ok != Oracle.Truth(x.First, heroes)).Select(x => $"{x.First.Id}: said {Say(x.Second.Ok)} ({x.Second.Reason})").ToList();
            return new GraderCheckLine(quest.Id, party, verdicts.Count - wrong.Count, verdicts.Count, wrong, answer.Error, verdicts.Any(v => v.Ok is null) ? answer.Data?.GetRawText() : null);
        }));

        foreach (var l in lines)
        {
            log($"  {l.Quest,-4} [{string.Join(", ", l.Party)}]: {l.Right} of {l.Total}{(l.Wrong.Count > 0 ? " - " + string.Join("; ", l.Wrong) : string.Empty)}{(l.Error is null ? string.Empty : " error: " + l.Error)}");
        }

        var right = lines.Sum(l => l.Right);
        var total = lines.Sum(l => l.Total);
        var report = $"Grader accuracy: {right} of {total} verdicts true. {gateway.Calls} agent calls, ${gateway.Cost:0.000}.";
        return new PartyRunResult(runId, "grader-check", options.Mode, options.Model, options.Strategy, gateway.Calls, gateway.Cost, [], lines, report);
    }

    public static string Write(PartyRunResult result, string resultsDir)
    {
        Directory.CreateDirectory(resultsDir);
        var file = Path.Combine(resultsDir, $"party-{result.RunId}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(result, Json));
        return file;
    }

    /// <summary>The grader verdicts of the run that agree with the oracle truth, for each graded party.</summary>
    private string GraderAccuracy(PartyTask task, ComposeTrace trace)
    {
        var right = 0;
        var total = 0;
        foreach (var g in trace.Turns.SelectMany(t => t.Grades).Where(g => !g.Cached))
        {
            if (g.Parts is { } parts)
            {
                foreach (var (rubric, verdict) in task.Whole!.Visible.Zip(g.Verdicts))
                {
                    total++;
                    right += verdict.Ok == Oracle.PoolTruth(rubric, parts.Select(p => p.Set).ToList()) ? 1 : 0;
                }

                continue;
            }

            var quest = task.Specs.First(q => q.Id == g.Spec);
            var heroes = g.Set.Select(pack.Heroes.Get).OfType<Hero>().ToList();
            foreach (var (rubric, verdict) in quest.Visible.Zip(g.Verdicts))
            {
                total++;
                right += verdict.Ok == Oracle.Truth(rubric, heroes) ? 1 : 0;
            }
        }

        return $"{right} of {total}";
    }

    private static string Line(PartyCaseRun r)
    {
        var commits = string.Join(" · ", r.Trace.Commits.Select(c => c.Set is null ? "none" : string.Join("+", c.Set)));
        var grades = string.Join(", ", r.Verdicts.Where(v => v.Rubric != "valid").Select(v => $"{v.Rubric} {(v.Ok ? "yes" : "no")}"));
        var measures = string.Join(", ", r.Measures.Select(m => $"{m.Key} {m.Value}"));
        var spec = r.Decomposition is null ? string.Empty
            : r.Decomposition.Anchor is { } a ? $" [spec: {Decomposer.Describe(a)}]"
            : $" [spec refused: {string.Join("; ", r.Decomposition.Errors)}]";
        return $"  {r.Case}-{r.Trial}: {(r.Pass ? "PASS" : "FAIL")}{spec} {commits} | {grades} | {measures} | grader {r.GraderAccuracy} | {r.Trace.Turns.Count} turns, {r.Trace.Grades} grades, {r.Trace.Calls} calls, ${r.Trace.Cost:0.000}{(r.Trace.StopReason is null ? string.Empty : " | " + r.Trace.StopReason)}{(r.Pass ? string.Empty : " | " + r.Verdicts.First(v => v.Rubric == "valid").Reason)}";
    }

    private static string Report(IReadOnlyList<PartyCaseRun> runs)
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"Pass: {runs.Count(r => r.Pass)} of {runs.Count}.");
        foreach (var rubric in runs.SelectMany(r => r.Verdicts).Select(v => v.Rubric).Distinct().Where(r => r != "valid"))
        {
            var with = runs.Where(r => r.Verdicts.Any(v => v.Rubric == rubric)).ToList();
            b.Append(CultureInfo.InvariantCulture, $" {rubric}: {with.Count(r => r.Verdicts.First(v => v.Rubric == rubric).Ok)} of {with.Count}.");
        }

        b.Append(CultureInfo.InvariantCulture, $" {runs.Sum(r => r.Trace.Calls)} agent calls, ${runs.Sum(r => r.Trace.Cost):0.000}.");
        return b.ToString();
    }

    private static string Say(bool? ok) => ok is { } v ? (v ? "true" : "false") : "nothing";

    private static string RunId(string? label) =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(label) ? string.Empty : "-" + label);
}
