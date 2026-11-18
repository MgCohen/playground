using Microsoft.Extensions.DependencyInjection;
using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Catalog.Slices.BuildCatalog;
using PatternEngine.Features.Catalog.Slices.Lifecycle;
using PatternEngine.Features.Report.Slices.Trials;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Engine.Slices.Solve;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Eval.Slices.RunEval;
using PatternEngine.Features.GraderTrust.Slices.RunTrust;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Features.Report.Slices.Regrade;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.App.Host;

/// <summary>The pe command line. Each command sends one slice command; the agents and the system use the same commands.</summary>
public sealed class Cli(IServiceProvider services)
{
    private const string Usage = """
        pe build [domain]                         validate the patterns; write build/index.md and build/graph.json
        pe cases [domain] [--count n --seed n --max-children n]
        pe trust [domain]                         test the graders: fixtures, references, mutations (no model)
        pe solve "request" [--domain d] [--model m] [--ask]
        pe eval [domain] [--mode smoke|dev|full] [--sample n] [--k n] [--model m] [--only a,b] [--limit n]
                [--concurrency n] [--ablate a,b] [--label text] [--count n --seed n --max-children n] [--dry]
        pe report <result.json> [baseline.json]
        pe regrade <result.json>
        pe propose <run folder>/<id>.md [--domain d]   copy a proposed pattern of a run into the catalog
        pe approve <id> [--domain d]              a person approves: proposed -> trial (in the index)
        pe stable <id> [--domain d]               final call: trial -> stable
        pe remove <id> [--domain d]               final call: the pattern leaves the catalog
        pe trials [domain]                        metrics of each trial pattern; notifications at the thresholds
        pe review <result.json> [...] [--title t]  build the review page: one card for each run
        pe compose eval [--rung r | --only a,b] [--strategy s] [--mode smoke|dev] [--k n] [--model m] [--label t] [--dry]
                        [--spec decompose|given] [--lore on|off]   (prose cases)
                                                  run party cases with the compose loop; the oracle grades each run
        pe compose grader-check [--mode dev] [--model m]   grade the fixed pairs; compare each verdict with the oracle
        pe grader-check <domain|party> [--mode dev] [--model m]
                                                  run the grader fixtures with known answers; write the stamp of the grader files
        """;

    private readonly ISender sender = services.GetRequiredService<ISender>();

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var a = new Args(args.Skip(1).ToArray());
        try
        {
            return args[0] switch
            {
                "build" => await BuildAsync(a),
                "cases" => Cases(a),
                "trust" => await TrustAsync(a),
                "solve" => await SolveAsync(a),
                "eval" => await EvalAsync(a),
                "report" => await Print(sender.SendAsync(new ShowReportCommand(a.Positional(0)!, a.Positional(1)))),
                "regrade" => await RegradeAsync(a),
                "propose" => await Print(sender.SendAsync(new LifecycleCommand(LifecycleAction.Propose, a.Positional(0)!, a.Value("domain")))),
                "approve" => await Print(sender.SendAsync(new LifecycleCommand(LifecycleAction.Approve, a.Positional(0)!, a.Value("domain")))),
                "stable" => await Print(sender.SendAsync(new LifecycleCommand(LifecycleAction.Stable, a.Positional(0)!, a.Value("domain")))),
                "remove" => await Print(sender.SendAsync(new LifecycleCommand(LifecycleAction.Remove, a.Positional(0)!, a.Value("domain")))),
                "trials" => await TrialsAsync(a),
                "compose" => await ComposeAsync(a),
                "grader-check" => a.Positional(0) == "party" ? await ComposeAsync(new Args(["grader-check", .. args.Skip(2)])) : await GraderCheckAsync(a),
                "review" => await Print(sender.SendAsync(new Features.Review.Slices.BuildReview.BuildReviewCommand(a.All, a.Value("title")))),
                _ => Fail($"unknown command \"{args[0]}\"\n\n{Usage}"),
            };
        }
        catch (Exception e) when (e is InvalidOperationException or Plugins.Settings.SettingsException or FileNotFoundException or Plugins.Agents.AgentFileException)
        {
            return Fail(e.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 2;
    }

    private static async Task<int> Print(Task<Result<string>> task)
    {
        var r = await task;
        Console.WriteLine(r.IsSuccess ? r.Value : r.Error!.ToString());
        return r.IsSuccess ? 0 : 1;
    }

    private async Task<int> BuildAsync(Args a)
    {
        var r = (await sender.SendAsync(new BuildCatalogCommand(a.Positional(0)))).Value;
        foreach (var f in r.Findings)
        {
            Console.WriteLine($"{f.Severity.ToString().ToLowerInvariant()}: {f}");
        }

        Console.WriteLine(r.Ok ? $"{r.Domain}: {r.Patterns} patterns, {r.Edges} edges, catalog {r.Hash}. Wrote build/graph.json and build/index.md." : $"{r.Domain}: the catalog has errors.");
        return r.Ok ? 0 : 1;
    }

    private int Cases(Args a)
    {
        var packs = services.GetRequiredService<PackRegistry>();
        var pack = packs.Load(a.Positional(0));
        var runner = services.GetRequiredService<EvalRunner>();
        foreach (var c in runner.Cases(pack, Options(a, pack, "dev") with { Sample = 0 }))
        {
            Console.WriteLine($"{c.Id,-16} {string.Join(",", c.Tags),-36} {c.Request}");
        }

        return 0;
    }

    private async Task<int> TrustAsync(Args a)
    {
        var r = (await sender.SendAsync(new RunTrustCommand(a.Positional(0)))).Value;
        foreach (var check in r.Checks)
        {
            Console.WriteLine($"[{check.Check}]");
            foreach (var line in check.Lines)
            {
                Console.WriteLine($"{(line.Ok ? "ok  " : "FAIL")}  {line.Text}");
            }
        }

        Console.WriteLine(r.Ok ? "\nAll grader checks passed." : "\nSome grader checks failed.");
        return r.Ok ? 0 : 1;
    }

    private async Task<int> SolveAsync(Args a)
    {
        var request = a.Positional(0) ?? throw new InvalidOperationException("give the request in quotes");
        IUserChannel user = a.Flag("ask") ? new ConsoleUser() : new NoUser();
        var r = (await sender.SendAsync(new SolveCommand(request, a.Value("domain"), a.Value("model"), user))).Value;
        Console.WriteLine($"Outcome: {r.Trace.Outcome}{(r.Trace.StopReason is null ? string.Empty : $" (stop: {r.Trace.StopReason})")}");
        Console.WriteLine($"Tree: {r.Trace.Tree?.ToString() ?? "-"}");
        foreach (var q in r.Trace.Questions)
        {
            Console.WriteLine($"Question ({q.About} {q.Key}): {q.Text} → {q.Answer ?? "no answer"}");
        }

        Console.WriteLine($"{r.Stats.Calls} agent calls, ${r.Stats.Cost:0.000}, {r.Stats.Ms / 1000.0:0.0} s{(r.Stats.Error is null ? string.Empty : ", error: " + r.Stats.Error)}");
        Console.WriteLine($"Folder: {r.WorkDir}");
        return r.Stats.Error is null ? 0 : 1;
    }

    private async Task<int> EvalAsync(Args a)
    {
        var packs = services.GetRequiredService<PackRegistry>();
        var pack = packs.Load(a.Positional(0));
        var mode = a.Value("mode") ?? "dev";
        var options = Options(a, pack, mode);
        if (a.Flag("dry"))
        {
            var cases = services.GetRequiredService<EvalRunner>().Cases(pack, options);
            Console.WriteLine($"Mode {mode}: model {options.Model}, k={options.K}, {cases.Count} cases, {cases.Count * options.K} runs");
            foreach (var c in cases)
            {
                Console.WriteLine($"  {c.Id,-16} {Sampler.BucketOf(c, pack.Settings.Section("eval").List("sample_buckets")),-10} {c.Request}");
            }

            return 0;
        }

        WarnOldStamp(pack.Pack.Name, options.Model, StampFiles(pack), EngineSettings.From(pack.Settings).Decider == EngineSettings.ComposeDecider);
        var r = (await sender.SendAsync(new RunEvalCommand(pack.Pack.Name, options, Console.WriteLine))).Value;
        Console.WriteLine();
        Console.WriteLine(r.Report);
        Console.WriteLine(r.Targets.Text(mode));
        return r.Targets.Ok ? 0 : 1;
    }

    private async Task<int> TrialsAsync(Args a)
    {
        var r = (await sender.SendAsync(new TrialsCommand(a.Positional(0)))).Value;
        Console.WriteLine($"{"trial pattern",-24}{"version",-14}{"runs",6}{"pass",6}  state");
        foreach (var m in r.Metrics)
        {
            Console.WriteLine($"{m.Id,-24}{m.Hash,-14}{m.Runs,6}{m.Passes,6}  {m.State}");
        }

        if (r.Metrics.Count == 0)
        {
            Console.WriteLine("No trial patterns.");
        }

        foreach (var n in r.Notifications)
        {
            Console.WriteLine($"notification: {n}");
        }

        return 0;
    }

    private async Task<int> RegradeAsync(Args a)
    {
        var r = (await sender.SendAsync(new RegradeCommand(a.Positional(0)!))).Value;
        Console.WriteLine($"{r.RunId}: pass {r.PassBefore} -> {r.PassNow}");
        foreach (var c in r.Changed)
        {
            Console.WriteLine($"  {c}");
        }

        return 0;
    }

    private async Task<int> ComposeAsync(Args a)
    {
        var root = services.GetRequiredService<EngineRoot>();
        var settings = services.GetRequiredService<Plugins.Settings.Settings>();
        var pack = Domains.Party.PartyPack.Load(Path.Combine(root.Path(settings.Section("paths").Text("domains")), "Party"), Path.Combine(root.Dir, EngineRoot.SettingsFile));
        if (pack.Errors.Count > 0)
        {
            return Fail($"the party pack has errors:\n{string.Join('\n', pack.Errors)}");
        }

        var mode = a.Value("mode") ?? "dev";
        var m = pack.Settings.Section("eval", "modes", mode);
        var rung = a.Value("rung");
        var cases = a.List("only").Count > 0 ? a.List("only") : pack.Cases.Where(c => rung is null || c.Rung == rung).Select(c => c.Id).ToList();
        var decomposer = pack.Settings.Section("party", "decomposer");
        var options = new Domains.Party.PartyRunOptions(
            mode,
            a.Value("model") ?? m.Text("model"),
            a.Int("k") ?? m.Int("k"),
            cases,
            a.Value("strategy") ?? pack.Settings.Section("compose").Text("strategy"),
            a.Value("label"),
            a.Value("spec") ?? decomposer.Text("spec"),
            (a.Value("lore") ?? decomposer.Text("lore")) is "on" or "true" or "True");
        if (a.Flag("dry"))
        {
            Console.WriteLine($"Mode {mode}: model {options.Model}, k={options.K}, strategy {options.Strategy}, cases {string.Join(", ", cases)}");
            return 0;
        }

        var judge = pack.Settings.Section("judge", "cli");
        var agents = new Plugins.Agents.AgentCatalog([root.Path(pack.Settings.Section("paths").Text("agents"))]);
        var cli = new Plugins.Agents.CliJudgeSettings(judge.Text("executable"), options.Model, judge.Int("retries"), judge.Int("timeout_seconds"));
        var limits = pack.Settings.Section("engine", "limits", "run");
        var runner = new Domains.Party.PartyRunner(
            pack,
            () => new Plugins.Agents.CliJudge(agents, cli),
            new Features.Engine.Domain.RunLimits(limits.Int("agent_calls"), limits.Number("cost_usd"), limits.Int("seconds")),
            Path.Combine(Path.GetTempPath(), "pattern-engine-runs"));
        if (a.Positional(0) == "eval")
        {
            WarnOldStamp("party", options.Model, (root.Path(pack.Settings.Section("paths").Text("results")), GraderStamp.Hashes(root.Dir, [Path.Combine(root.Path(pack.Settings.Section("paths").Text("agents")), "grader.md"), Path.Combine(pack.Dir, "grading.md")])), true);
        }

        var result = a.Positional(0) switch
        {
            "eval" => await runner.RunAsync(options, Console.WriteLine, CancellationToken.None),
            "grader-check" => await runner.GraderCheckAsync(options, Console.WriteLine, CancellationToken.None),
            _ => throw new InvalidOperationException("use pe compose eval or pe compose grader-check"),
        };
        var results = root.Path(pack.Settings.Section("paths").Text("results"));
        var graderFiles = GraderStamp.Hashes(root.Dir, [Path.Combine(root.Path(pack.Settings.Section("paths").Text("agents")), "grader.md"), Path.Combine(pack.Dir, "grading.md")]);
        if (a.Positional(0) == "grader-check")
        {
            var right = result.GraderCheck.Sum(l => l.Right);
            new GraderStamp("party", options.Model, graderFiles, right, result.GraderCheck.Sum(l => l.Total), DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture)).Write(results);
        }

        var file = Domains.Party.PartyRunner.Write(result, results);
        Console.WriteLine();
        Console.WriteLine(result.Report);
        Console.WriteLine($"Result: {Path.GetRelativePath(root.Dir, file)}");
        return 0;
    }

    /// <summary>Runs the grader fixtures of an engine pack with compose hooks, and writes the stamp (G2, B8).</summary>
    private async Task<int> GraderCheckAsync(Args a)
    {
        var packs = services.GetRequiredService<PackRegistry>();
        var pack = packs.Load(a.Positional(0));
        var mode = a.Value("mode") ?? "dev";
        var model = a.Value("model") ?? pack.Settings.Section("eval", "modes", mode).Text("model");
        var work = Path.Combine(Path.GetTempPath(), "pattern-engine-runs", $"grader-check-{pack.Pack.Name}-{DateTime.UtcNow:yyyyMMddHHmmss}");
        Directory.CreateDirectory(work);
        var gateway = new Features.Engine.Domain.AgentGateway(services.GetRequiredService<IJudgeFactory>().Create(pack, model), work, Features.Engine.Domain.EngineSettings.From(pack.Settings).Run);
        var lines = await GraderCheck.RunAsync(pack, gateway, CancellationToken.None);
        foreach (var l in lines)
        {
            Console.WriteLine($"  {l.Fixture,-32} [{string.Join(", ", l.Set)}]: {l.Right} of {l.Total}{(l.Wrong.Count > 0 ? " - " + string.Join("; ", l.Wrong) : string.Empty)}{(l.Error is null ? string.Empty : " error: " + l.Error)}");
        }

        var (results, files) = StampFiles(pack);
        var right = lines.Sum(l => l.Right);
        var total = lines.Sum(l => l.Total);
        new GraderStamp(pack.Pack.Name, model, files, right, total, DateTime.UtcNow.ToString("s", System.Globalization.CultureInfo.InvariantCulture)).Write(results);
        Console.WriteLine($"\nGrader fixtures of {pack.Pack.Name}: {right} of {total} verdicts as known, model {model}. {gateway.Calls} agent calls, ${gateway.Cost:0.000}. Stamp: {Path.GetRelativePath(packs.Root.Dir, GraderStamp.FileOf(results, pack.Pack.Name))}");
        return right == total ? 0 : 1;
    }

    private (string Results, IReadOnlyDictionary<string, string> Files) StampFiles(PackContext pack)
    {
        var root = services.GetRequiredService<PackRegistry>().Root;
        var files = GraderCheck.GraderFiles(pack, root.Path(pack.Settings.Section("paths").Text("agents")));
        return (root.Path(pack.Settings.Section("paths").Text("results")), GraderStamp.Hashes(root.Dir, files));
    }

    /// <summary>Warns when the grader fixtures of a pack did not run with this model and these grader files (B8). It does not stop the run.</summary>
    private static void WarnOldStamp(string pack, string model, (string Results, IReadOnlyDictionary<string, string> Files) stamp, bool usesGrader)
    {
        if (usesGrader && GraderStamp.Old(stamp.Results, pack, model, stamp.Files) is { } reason)
        {
            Console.Error.WriteLine($"warning: {reason}");
        }
    }

    private static EvalOptions Options(Args a, PackContext pack, string mode)
    {
        var eval = pack.Settings.Section("eval");
        var m = eval.Section("modes", mode);
        var g = eval.Section("generator");
        return new EvalOptions(
            mode,
            a.Int("sample") ?? m.Int("sample"),
            a.Int("k") ?? m.Int("k"),
            a.Value("model") ?? m.Text("model"),
            a.List("only"),
            a.Int("limit") ?? 0,
            a.Int("concurrency") ?? eval.Int("concurrency"),
            a.List("ablate"),
            a.Value("label"),
            GeneratorSettings.From(g, a.Int("count"), a.Int("seed"), a.Int("max-children")));
    }

    private sealed class Args
    {
        private readonly List<string> positional = [];
        private readonly Dictionary<string, string?> named = new(StringComparer.Ordinal);

        public Args(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    var key = args[i][2..];
                    var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                    named[key] = hasValue ? args[++i] : null;
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
        }

        public string? Positional(int i) => i < positional.Count ? positional[i] : null;

        public IReadOnlyList<string> All => positional;

        public bool Flag(string key) => named.ContainsKey(key);

        public string? Value(string key) => named.GetValueOrDefault(key);

        public int? Int(string key) => named.GetValueOrDefault(key) is { } v ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : null;

        public IReadOnlyList<string> List(string key) => named.GetValueOrDefault(key)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
    }
}
