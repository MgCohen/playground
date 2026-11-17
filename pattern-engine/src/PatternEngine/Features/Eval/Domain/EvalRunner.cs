using System.Text.Json;
using System.Text.Json.Serialization;
using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Trace.Domain.Shared;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Eval.Domain;

public sealed record RunRecord
{
    [JsonPropertyName("case")]
    public required string Case { get; init; }

    [JsonPropertyName("run")]
    public required int Run { get; init; }

    [JsonPropertyName("tags")]
    public required List<string> Tags { get; init; }

    [JsonPropertyName("request")]
    public required string Request { get; init; }

    [JsonPropertyName("expect")]
    public required Expect Expect { get; init; }

    [JsonPropertyName("hidden")]
    public Dictionary<string, string>? Hidden { get; init; }

    [JsonPropertyName("trace")]
    public RunTrace? Trace { get; init; }

    [JsonPropertyName("grades")]
    public required GradeReport Grades { get; init; }

    [JsonPropertyName("pass")]
    public required bool Pass { get; init; }

    [JsonPropertyName("agent")]
    public required RunStats Agent { get; init; }
}

public sealed record EvalResult
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "csharp-hybrid";

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("k")]
    public required int K { get; init; }

    [JsonPropertyName("ablate")]
    public required List<string> Ablate { get; init; }

    [JsonPropertyName("graders")]
    public required List<string> Graders { get; init; }

    [JsonPropertyName("solution_graders")]
    public List<string> SolutionGraders { get; init; } = [];

    [JsonPropertyName("catalog")]
    public required string Catalog { get; init; }

    [JsonPropertyName("cases")]
    public required int Cases { get; init; }

    [JsonPropertyName("runs")]
    public required List<RunRecord> Runs { get; init; }
}

public sealed record EvalOptions(string Mode, int Sample, int K, string Model, IReadOnlyList<string> Only, int Limit, int Concurrency, IReadOnlyList<string> Ablate, string? Label, GeneratorSettings Generator);

/// <summary>
/// Runs the engine on many cases, each run in a clean sandbox, then grades each run and writes one result file.
/// The case sources, the graders, the judge and the ablations are each chosen by name, so each can change alone.
/// </summary>
public sealed class EvalRunner(PackRegistry packs, GraderSet graders, IReadOnlyList<ICaseSource> sources, IJudgeFactory judges)
{
    public IReadOnlyList<Case> Cases(PackContext pack, EvalOptions options)
    {
        var names = pack.Settings.Section("eval").List("case_sources");
        var context = new CaseContext(pack.Dir, pack.Catalog, options.Generator);
        var cases = names.SelectMany(n => (sources.FirstOrDefault(s => s.Name == n) ?? throw new InvalidOperationException($"no case source \"{n}\"")).Load(context)).ToList();
        if (options.Only.Count > 0)
        {
            cases = cases.Where(c => options.Only.Any(o => c.Id == o || c.Tags.Contains(o) || (o == "gen" && c.Source == "generator"))).ToList();
        }

        var sampled = Sampler.Sample(cases, options.Sample, pack.Settings.Section("eval").List("sample_buckets")).ToList();
        return options.Limit > 0 ? sampled.Take(options.Limit).ToList() : sampled;
    }

    public async Task<EvalResult> RunAsync(string domain, EvalOptions options, Action<string> log, CancellationToken ct)
    {
        var pack = packs.Load(domain);
        var cases = Cases(pack, options);
        var gradersToRun = graders.Plan(pack.Settings.Section("grading"), pack.Pack);
        var ablations = Ablations.Named(options.Ablate);
        var runId = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss", System.Globalization.CultureInfo.InvariantCulture) + (options.Ablate.Count > 0 ? "-" + string.Join("+", options.Ablate) : string.Empty);
        var paths = pack.Settings.Section("paths");
        var tempRoot = paths.Text("sandboxes") is { Length: > 0 } t ? packs.Root.Path(t) : Path.GetTempPath();
        var sandboxRoot = Path.Combine(tempRoot, "pattern-engine-runs", runId);
        var keepRoot = Path.Combine(packs.Root.Path(paths.Text("runs")), runId);
        var jobs = cases.SelectMany(c => Enumerable.Range(1, options.K).Select(i => (Case: c, Run: i))).ToList();
        log($"Run {runId} [{options.Mode}]: model {options.Model}, {cases.Count} cases × k={options.K} = {jobs.Count} runs, concurrency {options.Concurrency}{(options.Ablate.Count > 0 ? ", ablate " + string.Join(",", options.Ablate) : string.Empty)}");
        var baseSettings = EngineSettings.From(pack.Settings);
        var records = new List<RunRecord>();
        var done = 0;
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Concurrency), CancellationToken = ct }, async (job, token) =>
        {
            var dir = Path.Combine(sandboxRoot, $"{job.Case.Id}-{job.Run}");
            EngineRun.Prepare(pack, dir);
            var settings = baseSettings;
            foreach (var a in ablations)
            {
                settings = a.Apply(settings, new SandboxSetup(dir, Path.Combine(dir, "patterns")));
            }

            var run = await EngineRun.RunAsync(pack, job.Case.Request, dir, settings, judges.Create(pack, options.Model), new SimulatedUser(job.Case), token);
            var output = File.Exists(run.OutputFile) ? await File.ReadAllTextAsync(run.OutputFile, token) : null;
            var grades = RunGrading.Grade(job.Case, pack.Pack, run.Catalog, run.Trace, output, gradersToRun);
            var record = new RunRecord
            {
                Case = job.Case.Id,
                Run = job.Run,
                Tags = job.Case.Tags,
                Request = job.Case.Request,
                Expect = job.Case.Expect,
                Hidden = job.Case.Hidden,
                Trace = run.Trace,
                Grades = grades,
                Pass = grades.Pass && run.Stats.Error is null,
                Agent = run.Stats,
            };
            Keep(dir, Path.Combine(keepRoot, $"{job.Case.Id}-{job.Run}"), pack.Pack.OutputFile, run.Trace);
            lock (records)
            {
                records.Add(record);
                done++;
                var fails = grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures.FirstOrDefault()}");
                log($"[{done}/{jobs.Count}] {(record.Pass ? "PASS" : "FAIL")} {job.Case.Id}#{job.Run} {run.Stats.Ms / 1000}s {run.Stats.Calls} calls{(run.Stats.Error is null ? string.Empty : " error: " + run.Stats.Error)}{string.Concat(fails.Select(f => "\n      " + f))}{string.Concat((run.Trace.Compose?.Scores ?? []).Select(sc => "\n      anchor " + sc))}");
            }
        });

        var result = new EvalResult
        {
            RunId = runId,
            Domain = pack.Pack.Name,
            Mode = options.Mode,
            Label = options.Label,
            Model = options.Model,
            K = options.K,
            Ablate = [.. options.Ablate],
            Graders = gradersToRun.Output.Select(g => g.Name).ToList(),
            SolutionGraders = gradersToRun.Solution.Select(g => g.Name).ToList(),
            Catalog = pack.Catalog.Hash,
            Cases = cases.Count,
            Runs = records.OrderBy(r => r.Case, StringComparer.Ordinal).ThenBy(r => r.Run).ToList(),
        };
        var resultsDir = packs.Root.Path(paths.Text("results"));
        Directory.CreateDirectory(resultsDir);
        await File.WriteAllTextAsync(Path.Combine(resultsDir, $"{runId}.json"), JsonSerializer.Serialize(result, EngineRun.Json), ct);
        return result;
    }

    private static void Keep(string from, string to, string outputFile, RunTrace trace)
    {
        Directory.CreateDirectory(to);
        foreach (var name in new[] { outputFile, "trace.json", "agent.log.json" })
        {
            var f = Path.Combine(from, name);
            if (File.Exists(f))
            {
                File.Copy(f, Path.Combine(to, name), true);
            }
        }

        if (trace.Proposed is { } proposed && File.Exists(Path.Combine(from, proposed)))
        {
            File.Copy(Path.Combine(from, proposed), Path.Combine(to, Path.GetFileName(proposed)), true);
        }
    }
}
