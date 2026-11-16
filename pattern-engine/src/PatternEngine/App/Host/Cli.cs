using Microsoft.Extensions.DependencyInjection;
using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Catalog.Slices.BuildCatalog;
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

        var r = (await sender.SendAsync(new RunEvalCommand(pack.Pack.Name, options, Console.WriteLine))).Value;
        Console.WriteLine();
        Console.WriteLine(r.Report);
        Console.WriteLine(r.Targets.Text(mode));
        return r.Targets.Ok ? 0 : 1;
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

        public bool Flag(string key) => named.ContainsKey(key);

        public string? Value(string key) => named.GetValueOrDefault(key);

        public int? Int(string key) => named.GetValueOrDefault(key) is { } v ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : null;

        public IReadOnlyList<string> List(string key) => named.GetValueOrDefault(key)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
    }
}
