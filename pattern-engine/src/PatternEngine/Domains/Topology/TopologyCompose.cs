using System.Globalization;
using System.Text;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Topology;

/// <summary>
/// The compose hooks of topology. The decomposer gives the input types, the output types and the jobs of a request.
/// Code writes the spec: one io rubric with check code, and one job rubric for the grader for each job.
/// Code answers the io rubric from the port types of the set, and checks each set: the ports connect, and each loop has an entry (R1).
/// </summary>
public sealed class TopologyCompose : IComposeHooks
{
    public const string Io = "io";

    public const string Connect = "set:connect";

    public const string LoopEntry = "set:loop-entry";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Known(PatternCatalog catalog)
    {
        var types = Types(catalog);
        return new Dictionary<string, IReadOnlyList<string>>
        {
            ["input"] = types,
            ["output"] = types,
            ["job"] = ["a short sentence with the words of the request"],
        };
    }

    public (Anchor? Spec, IReadOnlyList<string> Errors) Spec(string id, string text, IReadOnlyList<Need> needs, PatternCatalog catalog)
    {
        var types = Types(catalog);
        var errors = needs.Where(n => n.Kind is not ("input" or "output" or "job")).Select(n => $"unknown kind \"{n.Kind}\"").ToList();
        string? Type(Need n)
        {
            var t = types.FirstOrDefault(x => string.Equals(x, n.Name, StringComparison.OrdinalIgnoreCase));
            if (t is null)
            {
                errors.Add($"unknown type \"{n.Name}\" for {n.Kind}; use one of {string.Join(", ", types)}");
            }

            return t;
        }

        var inputs = needs.Where(n => n.Kind == "input").Select(Type).OfType<string>().Distinct().ToList();
        var outputs = needs.Where(n => n.Kind == "output").Select(Type).OfType<string>().Distinct().ToList();
        var jobs = needs.Where(n => n.Kind == "job").Select(n => n.Name.Trim().TrimEnd('.')).Where(j => j.Length > 0).Distinct().ToList();
        if (inputs.Count == 0)
        {
            errors.Add("no input type");
        }

        if (outputs.Count == 0)
        {
            errors.Add("no output type");
        }

        if (jobs.Count == 0)
        {
            errors.Add("no job");
        }

        return errors.Count > 0 ? (null, errors) : (Anchor(id, text, inputs, outputs, jobs), []);
    }

    /// <summary>Writes the spec anchor of a request from its types and jobs.</summary>
    public static Anchor Anchor(string id, string text, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, IReadOnlyList<string> jobs)
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"---\nid: {id}\nkind: success\nlevel: case\nscore: {{ rule: all }}\n---\n{text.Replace("\n", " ", StringComparison.Ordinal)}\n\n");
        b.Append(CultureInfo.InvariantCulture, $"- id: {Io}\n  ask: Does the set take only {string.Join(", ", inputs)} from outside, and give {string.Join(", ", outputs)}?\n  check: code\n  args: {{ inputs: [{string.Join(", ", inputs)}], outputs: [{string.Join(", ", outputs)}] }}\n");
        for (var i = 0; i < jobs.Count; i++)
        {
            b.Append(CultureInfo.InvariantCulture, $"- id: job-{i + 1}\n  ask: \"Does a pattern of the set do this job: {jobs[i].Replace("\"", "'", StringComparison.Ordinal)}?\"\n  check: grader\n");
        }

        return Features.Compose.Domain.Shared.Anchor.Parse(b.ToString(), $"decomposed:{id}");
    }

    public IReadOnlyList<RubricVerdict> CodeVerdicts(Anchor spec, IReadOnlyList<CatalogEntry> set, PatternCatalog catalog)
    {
        var verdicts = new List<RubricVerdict>();
        var given = new List<string>();
        foreach (var r in spec.Visible.Where(r => r.ByCode))
        {
            if (r.Id != Io)
            {
                verdicts.Add(new RubricVerdict(r.Id, null, $"topology has no code check \"{r.Id}\""));
                continue;
            }

            given = Plugins.Yaml.Node.Strings(r.Args.GetValueOrDefault("inputs")).ToList();
            verdicts.Add(IoVerdict(set, given, Plugins.Yaml.Node.Strings(r.Args.GetValueOrDefault("outputs")).ToList()));
        }

        verdicts.Add(ConnectVerdict(set));
        verdicts.Add(LoopVerdict(set, given));
        return verdicts;
    }

    /// <summary>
    /// The io rubric: each input type that no pattern of the set gives must come from outside (the given inputs),
    /// and each expected output type must come from a pattern of the set.
    /// </summary>
    public static RubricVerdict IoVerdict(IReadOnlyList<CatalogEntry> set, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
    {
        var made = set.SelectMany(e => e.Pattern.Outputs).Select(p => p.Type).ToHashSet(StringComparer.Ordinal);
        var needed = set.SelectMany(e => e.Pattern.Inputs).Select(p => p.Type).Distinct().ToList();
        var outside = needed.Where(t => !made.Contains(t) && !inputs.Contains(t)).ToList();
        var absent = outputs.Where(t => !made.Contains(t)).ToList();
        var problems = outside.Select(t => $"needs a {t} input that the request does not give and no pattern gives")
            .Concat(absent.Select(t => $"gives no {t}")).ToList();
        return new RubricVerdict(Io, problems.Count == 0, problems.Count == 0 ? $"takes {string.Join(", ", inputs)}, gives {string.Join(", ", outputs)}" : string.Join("; ", problems));
    }

    /// <summary>The ports connect: with two entries or more, each entry gives a type that another entry takes, or takes a type that another entry gives.</summary>
    public static RubricVerdict ConnectVerdict(IReadOnlyList<CatalogEntry> set)
    {
        if (set.Count < 2)
        {
            return new RubricVerdict(Connect, true, "one pattern");
        }

        var lone = set.Where(e => !set.Any(o => o != e && (Links(e, o) || Links(o, e)))).Select(e => e.Id).ToList();
        return new RubricVerdict(Connect, lone.Count == 0, lone.Count == 0 ? "each pattern has a port type in common with another pattern" : $"no port of {string.Join(", ", lone)} connects to another pattern of the set");
    }

    /// <summary>Each loop has an entry: in each cycle of the type links, one pattern takes a type from outside the cycle (a given input or another pattern).</summary>
    public static RubricVerdict LoopVerdict(IReadOnlyList<CatalogEntry> set, IReadOnlyList<string> given)
    {
        var closed = new List<string>();
        foreach (var cycle in Cycles(set))
        {
            var inside = cycle.SelectMany(e => e.Pattern.Outputs).Select(p => p.Type).ToHashSet(StringComparer.Ordinal);
            var fromOutside = set.Except(cycle).SelectMany(e => e.Pattern.Outputs).Select(p => p.Type).Concat(given).ToHashSet(StringComparer.Ordinal);
            if (!cycle.Any(e => e.Pattern.Inputs.Any(p => fromOutside.Contains(p.Type) || !inside.Contains(p.Type))))
            {
                closed.Add(string.Join(" -> ", cycle.Select(e => e.Id)));
            }
        }

        return new RubricVerdict(LoopEntry, closed.Count == 0, closed.Count == 0 ? "each loop has an entry" : $"a loop with no entry: {string.Join("; ", closed)}");
    }

    /// <summary>True if an output type of the first entry is an input type of the second.</summary>
    private static bool Links(CatalogEntry from, CatalogEntry to) =>
        from.Pattern.Outputs.Any(o => to.Pattern.Inputs.Any(i => i.Type == o.Type));

    /// <summary>The strongly connected groups of the type links with a loop (Tarjan).</summary>
    private static List<List<CatalogEntry>> Cycles(IReadOnlyList<CatalogEntry> set)
    {
        var index = new Dictionary<CatalogEntry, int>();
        var low = new Dictionary<CatalogEntry, int>();
        var stack = new Stack<CatalogEntry>();
        var result = new List<List<CatalogEntry>>();
        var next = 0;

        void Visit(CatalogEntry v)
        {
            index[v] = low[v] = next++;
            stack.Push(v);
            foreach (var w in set.Where(w => Links(v, w)))
            {
                if (!index.ContainsKey(w))
                {
                    Visit(w);
                    low[v] = Math.Min(low[v], low[w]);
                }
                else if (stack.Contains(w))
                {
                    low[v] = Math.Min(low[v], index[w]);
                }
            }

            if (low[v] == index[v])
            {
                var group = new List<CatalogEntry>();
                CatalogEntry w;
                do
                {
                    w = stack.Pop();
                    group.Add(w);
                }
                while (w != v);
                if (group.Count > 1 || Links(v, v))
                {
                    result.Add(group);
                }
            }
        }

        foreach (var v in set.Where(v => !index.ContainsKey(v)))
        {
            Visit(v);
        }

        return result;
    }

    private static List<string> Types(PatternCatalog catalog) =>
        catalog.Active.SelectMany(p => p.Inputs.Concat(p.Outputs)).Select(p => p.Type).Distinct().Order(StringComparer.Ordinal).ToList();
}
