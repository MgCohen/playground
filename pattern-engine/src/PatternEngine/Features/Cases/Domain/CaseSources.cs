using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Cases.Domain;

public sealed record GeneratorSettings(int Count, int Seed, int MaxChildren, int TunedSeed);

public sealed record CaseContext(string PackDir, PatternCatalog Catalog, GeneratorSettings Generator);

/// <summary>A source of eval cases. The sources that run come from the settings (eval.case_sources).</summary>
public interface ICaseSource
{
    string Name { get; }

    IReadOnlyList<Case> Load(CaseContext context);
}

/// <summary>The random numbers of the spike (mulberry32), so the same seed gives the same cases.</summary>
public sealed class SpikeRandom(int seed)
{
    private uint state = unchecked((uint)seed);

    public double Next()
    {
        unchecked
        {
            state += 0x6d2b79f5;
            var t = state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + ((t ^ (t >> 7)) * (t | 61));
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public int Index(int count) => (int)Math.Floor(Next() * count);
}

/// <summary>
/// Walks the pattern graph and writes cases with computed expectations. The request text comes from the phrase fields,
/// so this class has no domain words. A required param is always in the request, because the request must give it.
/// </summary>
public sealed class GeneratorSource : ICaseSource
{
    public string Name => "generator";

    public IReadOnlyList<Case> Load(CaseContext context) => Generate(context.Catalog, context.Generator);

    public static IReadOnlyList<Case> Generate(PatternCatalog catalog, GeneratorSettings settings)
    {
        var rand = new SpikeRandom(settings.Seed);
        var active = catalog.Active.ToList();
        var objects = active.Where(p => p.Kind == PatternKind.Object).ToList();
        var groups = active.Where(p => p.Kind == PatternKind.Group).ToList();
        var cases = new List<Case>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var guard = 0;
        var heldout = settings.Seed != settings.TunedSeed;
        var prefix = heldout ? $"gen-s{settings.Seed}" : "gen";
        while (cases.Count < settings.Count && guard++ < 1000)
        {
            string request;
            TreeNode tree;
            List<string> tags;
            if (rand.Next() < 0.35 || groups.Count == 0)
            {
                var p = objects[rand.Index(objects.Count)];
                var o = Instance(p, rand);
                request = $"Draw {o.Text}.";
                tree = o.Tree;
                tags = ["generated", "object"];
            }
            else
            {
                var g = groups[rand.Index(groups.Count)];
                var gv = g.Variants[0];
                var childTypes = g.PartsFor(gv.Id).Select(pt => catalog.Get(pt.Ref)).Where(p => p is { Kind: PatternKind.Object, Status: not PatternStatus.Proposed }).Select(p => p!).ToList();
                var n = 2 + rand.Index(settings.MaxChildren - 1);
                var kids = Enumerable.Range(0, n).Select(_ => Instance(childTypes[rand.Index(childTypes.Count)], rand)).ToList();
                request = $"Draw {g.Phrase} with, from left to right: {JoinList(kids.Select(k => k.Text).ToList())}.";
                tree = new TreeNode { P = g.Id, V = gv.Id, Parts = kids.Select(k => k.Tree).ToList() };
                tags = ["generated", "group", n >= 4 ? "deep" : "medium"];
            }

            if (!seen.Add(request))
            {
                continue;
            }

            if (heldout)
            {
                tags.Add("heldout");
            }

            cases.Add(new Case
            {
                Id = $"{prefix}-{cases.Count + 1:000}",
                Source = "generator",
                Tags = tags,
                Request = request,
                Expect = new Expect { Outcome = Outcomes.Match, Tree = tree },
            });
        }

        return cases;
    }

    private static (TreeNode Tree, string Text) Instance(Pattern p, SpikeRandom rand)
    {
        var v = p.Variants[rand.Index(p.Variants.Count)];
        var mods = new List<string>();
        if (v.Phrase is not null)
        {
            mods.Add(v.Phrase);
        }

        Dictionary<string, string>? parameters = null;
        var optional = p.Params.Where(x => !x.Required).ToList();
        if (optional.Count > 0 && rand.Next() < 0.5)
        {
            var def = optional[rand.Index(optional.Count)];
            var value = def.Values[rand.Index(def.Values.Count)];
            parameters = new Dictionary<string, string> { [def.Key] = value };
            mods.Add(ReplaceFirst(def.Phrase ?? string.Empty, "{value}", value));
        }

        foreach (var def in p.Params.Where(x => x.Required))
        {
            var value = def.Values[rand.Index(def.Values.Count)];
            (parameters ??= [])[def.Key] = value;
            mods.Add(ReplaceFirst(def.Phrase ?? string.Empty, "{value}", value));
        }

        var tail = string.Join(" and ", mods.Select((m, i) => i == 0 ? m : (m.StartsWith("with ", StringComparison.Ordinal) ? m[5..] : m)));
        var tree = new TreeNode { P = p.Id, V = v.Id, Params = parameters };
        return (tree, tail.Length > 0 ? $"{p.Phrase} {tail}" : p.Phrase!);
    }

    private static string JoinList(IReadOnlyList<string> items) =>
        items.Count < 2 ? string.Concat(items) : string.Join("; ", items.Take(items.Count - 1)) + "; and " + items[^1];

    private static string ReplaceFirst(string s, string find, string with)
    {
        var at = s.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? s : s[..at] + with + s[(at + find.Length)..];
    }
}

/// <summary>Hand-written cases: every YAML file in the cases folder of the pack.</summary>
public sealed class HandSource : ICaseSource
{
    public string Name => "hand";

    public IReadOnlyList<Case> Load(CaseContext context)
    {
        var dir = Path.Combine(context.PackDir, "cases");
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.GetFiles(dir).Where(f => f.EndsWith(".yaml", StringComparison.Ordinal) || f.EndsWith(".yml", StringComparison.Ordinal))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .SelectMany(f => Node.List(Yaml.Parse(File.ReadAllText(f))).Select(c => Case.From(c, "hand")))
            .ToList();
    }
}

/// <summary>Takes n cases spread over buckets, the rarest kind first, in a fixed order.</summary>
public static class Sampler
{
    public static string BucketOf(Case c, IReadOnlyList<string> buckets) => buckets.FirstOrDefault(b => c.Tags.Contains(b)) ?? "other";

    public static IReadOnlyList<Case> Sample(IReadOnlyList<Case> cases, int n, IReadOnlyList<string> buckets)
    {
        if (n <= 0 || n >= cases.Count)
        {
            return cases;
        }

        var groups = new Dictionary<string, Queue<Case>>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var b = BucketOf(c, buckets);
            if (!groups.TryGetValue(b, out var q))
            {
                groups[b] = q = new Queue<Case>();
            }

            q.Enqueue(c);
        }

        var order = buckets.Append("other").Where(groups.ContainsKey).ToList();
        var output = new List<Case>();
        while (output.Count < n)
        {
            foreach (var b in order)
            {
                if (output.Count < n && groups[b].TryDequeue(out var c))
                {
                    output.Add(c);
                }
            }
        }

        return output;
    }
}
