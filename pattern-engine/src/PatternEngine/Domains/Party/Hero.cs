using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Domains.Party;

/// <summary>One catalog entry of the party domain: tags, a cost (0 or more) and stats (1 to 10).</summary>
public sealed record Hero(string Id, IReadOnlyList<string> Tags, int Cost, IReadOnlyDictionary<string, int> Stats, string Text)
{
    public int Stat(string name) => Stats.GetValueOrDefault(name);
}

/// <summary>The known words of the domain, from the settings party.tags and party.stats.</summary>
public sealed record PartyWords(IReadOnlyList<string> Tags, IReadOnlyList<string> Stats, int StatMin, int StatMax);

/// <summary>The hero files of Domains/Party/heroes, in a fixed order (by id).</summary>
public sealed class HeroCatalog
{
    private readonly Dictionary<string, Hero> byId;

    public HeroCatalog(IReadOnlyList<Hero> heroes, IReadOnlyList<string> errors)
    {
        Heroes = heroes.OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
        byId = Heroes.ToDictionary(h => h.Id, StringComparer.Ordinal);
        Errors = errors;
    }

    public IReadOnlyList<Hero> Heroes { get; }

    public IReadOnlyList<string> Errors { get; }

    public Hero? Get(string id) => byId.GetValueOrDefault(id);

    public bool Has(string id) => byId.ContainsKey(id);

    public static HeroCatalog Load(string dir, PartyWords words)
    {
        var heroes = new List<Hero>();
        var errors = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.md").Order(StringComparer.Ordinal))
        {
            var (hero, problems) = Parse(File.ReadAllText(file), words);
            var name = Path.GetFileNameWithoutExtension(file);
            if (hero is not null && hero.Id != name)
            {
                problems.Add($"id \"{hero.Id}\" is not the file name");
            }

            errors.AddRange(problems.Select(p => $"heroes/{name}.md: {p}"));
            if (hero is not null)
            {
                heroes.Add(hero);
            }
        }

        foreach (var dup in heroes.GroupBy(h => h.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"hero \"{dup.Key}\" is in the catalog more than once");
        }

        return new HeroCatalog(heroes.DistinctBy(h => h.Id).ToList(), errors);
    }

    public static (Hero? Hero, List<string> Errors) Parse(string text, PartyWords words)
    {
        var errors = new List<string>();
        var split = FrontMatter.Split(text);
        if (split is null)
        {
            return (null, ["no front matter"]);
        }

        var map = Node.Map(Yaml.Parse(split.Value.Head));
        var id = map.Get("id") ?? string.Empty;
        var tags = Node.Strings(map.Raw("tags"));
        if (id.Length == 0)
        {
            errors.Add("no id");
        }

        if (tags.Count == 0)
        {
            errors.Add("no tags");
        }

        errors.AddRange(tags.Where(t => !words.Tags.Contains(t)).Select(t => $"unknown tag \"{t}\""));
        if (!int.TryParse(map.Get("cost"), out var cost) || cost < 0)
        {
            errors.Add("cost must be an integer, 0 or more");
        }

        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in Node.Map(map.Raw("stats")))
        {
            if (!words.Stats.Contains(key))
            {
                errors.Add($"unknown stat \"{key}\"");
            }
            else if (!int.TryParse(Node.Text(value), out var v) || v < words.StatMin || v > words.StatMax)
            {
                errors.Add($"stat \"{key}\" must be an integer from {words.StatMin} to {words.StatMax}");
            }
            else
            {
                stats[key] = v;
            }
        }

        errors.AddRange(words.Stats.Where(s => !stats.ContainsKey(s) && !errors.Any(e => e.Contains($"\"{s}\"", StringComparison.Ordinal))).Select(s => $"no stat \"{s}\""));
        return (errors.Count == 0 ? new Hero(id, tags, cost, stats, split.Value.Body.Trim()) : null, errors);
    }
}
