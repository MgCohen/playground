using PatternEngine.Features.Cases.Domain;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.GraderTrust.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Messages;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.GraderTrust.Slices.RunTrust;

public sealed record RunTrustCommand(string? Domain) : ICommand<RunTrustResult>;

public sealed record RunTrustResult(string Domain, IReadOnlyList<TrustResult> Checks)
{
    public bool Ok => Checks.All(c => c.Ok);
}

/// <summary>Tests the graders with no model: fixtures, references (known good) and mutations (known bad). The checks come from the settings.</summary>
public sealed class RunTrustHandler(PackRegistry packs, IEnumerable<ITrustCheck> checks, IEnumerable<ICaseSource> sources) : IHandler<RunTrustCommand, RunTrustResult>
{
    public Task<Result<RunTrustResult>> HandleAsync(RunTrustCommand command, CancellationToken ct)
    {
        var pack = packs.Load(command.Domain);
        var s = pack.Settings.Section("grader_trust");
        var tuned = pack.Settings.Section("eval", "generator").Int("tuned_seed");
        var hand = sources.First(x => x.Name == "hand");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<Case>();
        foreach (var set in Node.List(s.Raw("case_sets")).Select(Node.Map))
        {
            var g = new GeneratorSettings(int.Parse(set.Get("count")!), int.Parse(set.Get("seed")!), int.Parse(set.Get("max_children")!), tuned);
            cases.AddRange(GeneratorSource.Generate(pack.Catalog, g).Concat(hand.Load(new CaseContext(pack.Dir, pack.Catalog, g))).Where(c => seen.Add(c.Id)));
        }

        var mutations = s.Raw("mutations") is string all && all == "all" ? null : s.List("mutations");
        var settings = new TrustSettings(s.Number("reference_pass_rate"), s.Number("mutation_catch_rate"), s.Number("right_reason_rate"), mutations);
        var context = new TrustContext(pack.Pack, pack.Dir, pack.Catalog, cases, settings);
        var byName = checks.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var results = s.List("checks").Select(n => (byName.GetValueOrDefault(n) ?? throw new InvalidOperationException($"no trust check \"{n}\"")).Run(context)).ToList();
        return Task.FromResult(Result<RunTrustResult>.Ok(new RunTrustResult(pack.Pack.Name, results)));
    }
}
