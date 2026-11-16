using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Plugins.Settings;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Domains.Party;

/// <summary>
/// One fixed case. A one-quest case is its own quest. A many-quest case lists its quests and adds the pool rubric.
/// </summary>
public sealed record PartyCase(string Id, string Rung, Anchor Anchor, IReadOnlyList<Anchor> Quests)
{
    public bool Many => Node.Strings(Anchor.Domain.Raw("quests")).Count > 0;

    /// <summary>The rubrics over the full assignment (the pool rubric). Empty for a one-quest case.</summary>
    public IReadOnlyList<Rubric> CaseRubrics => Many ? Anchor.Rubrics : [];

    public string? Text => Anchor.Field("text");
}

/// <summary>The party domain on disk: settings, heroes, cases and the eval anchor.</summary>
public sealed class PartyPack
{
    public static readonly string[] Rungs = ["cover", "budget", "available", "many", "stats", "prose"];

    public static readonly string[] ArgKeys = ["tag", "max_cost", "stat", "min", "unavailable", "pool"];

    private PartyPack(string dir, Settings settings, PartyWords words, HeroCatalog heroes, IReadOnlyList<PartyCase> cases, Anchor eval, IReadOnlyList<string> errors)
    {
        Dir = dir;
        Settings = settings;
        Words = words;
        Heroes = heroes;
        Cases = cases;
        Eval = eval;
        Errors = errors;
    }

    public string Dir { get; }

    public Settings Settings { get; }

    public PartyWords Words { get; }

    public HeroCatalog Heroes { get; }

    public IReadOnlyList<PartyCase> Cases { get; }

    /// <summary>The eval anchor. The run never gets it.</summary>
    public Anchor Eval { get; }

    public IReadOnlyList<string> Errors { get; }

    public PartyCase Case(string id) => Cases.FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException($"no party case \"{id}\"");

    /// <summary>Loads the pack from its folder. engineSettings is the path of pe.settings.yaml; the pack settings override it.</summary>
    public static PartyPack Load(string dir, string engineSettings)
    {
        var settings = Settings.Load(engineSettings, Path.Combine(dir, "pack.settings.yaml"));
        var party = settings.Section("party");
        var range = party.List("stat_range");
        var words = new PartyWords(party.List("tags"), party.List("stats"), int.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture));
        var heroes = HeroCatalog.Load(Path.Combine(dir, "heroes"), words);
        var errors = new List<string>(heroes.Errors);

        var anchors = Directory.GetFiles(Path.Combine(dir, "cases"), "*.md").Order(StringComparer.Ordinal).Select(Anchor.Load).ToList();
        foreach (var a in anchors)
        {
            var name = Path.GetFileNameWithoutExtension(a.File);
            errors.AddRange(a.Errors.Select(e => $"cases/{name}.md: {e}"));
            errors.AddRange(Check(a, heroes, words).Select(e => $"cases/{name}.md: {e}"));
            if (a.Id != name)
            {
                errors.Add($"cases/{name}.md: id \"{a.Id}\" is not the file name");
            }
        }

        var byId = anchors.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var cases = new List<PartyCase>();
        foreach (var a in anchors.Where(a => a.Level == "case"))
        {
            var rung = a.Domain.Get("rung") ?? string.Empty;
            if (!Rungs.Contains(rung))
            {
                errors.Add($"case {a.Id}: rung must be one of {string.Join(", ", Rungs)}");
            }

            var questIds = Node.Strings(a.Domain.Raw("quests"));
            var quests = new List<Anchor>();
            foreach (var q in questIds)
            {
                if (byId.TryGetValue(q, out var quest) && quest.Level == "quest")
                {
                    quests.Add(quest);
                }
                else
                {
                    errors.Add($"case {a.Id}: no quest anchor \"{q}\"");
                }
            }

            if (rung == "prose" && string.IsNullOrWhiteSpace(a.Field("text")))
            {
                errors.Add($"case {a.Id}: a prose case needs text");
            }

            if (rung == "prose" && a.Rubrics.Any(r => !r.Hidden))
            {
                errors.Add($"case {a.Id}: each rubric of a prose case must have visibility eval");
            }

            cases.Add(new PartyCase(a.Id, rung, a, questIds.Count > 0 ? quests : [a]));
        }

        var eval = Anchor.Load(Path.Combine(dir, "anchors", "eval.md"));
        errors.AddRange(eval.Errors.Select(e => $"anchors/eval.md: {e}"));
        return new PartyPack(dir, settings, words, heroes, cases, eval, errors);
    }

    /// <summary>Checks the typed args of each rubric. A tag that no hero has is allowed: the answer is then "none".</summary>
    public static IEnumerable<string> Check(Anchor anchor, HeroCatalog heroes, PartyWords words)
    {
        foreach (var r in anchor.Rubrics)
        {
            foreach (var key in r.Args.Keys.Where(k => !ArgKeys.Contains(k)))
            {
                yield return $"rubric \"{r.Id}\": unknown arg \"{key}\"";
            }

            if (r.Args.Count == 0)
            {
                yield return $"rubric \"{r.Id}\": no args, so the oracle cannot check it";
            }

            if (r.Args.Get("stat") is { } stat && !words.Stats.Contains(stat))
            {
                yield return $"rubric \"{r.Id}\": unknown stat \"{stat}\"";
            }

            if (r.Args.ContainsKey("stat") != r.Args.ContainsKey("min"))
            {
                yield return $"rubric \"{r.Id}\": stat and min go together";
            }

            foreach (var id in Node.Strings(r.Args.Raw("unavailable")).Where(id => !heroes.Has(id)))
            {
                yield return $"rubric \"{r.Id}\": unknown hero \"{id}\"";
            }
        }

        foreach (var id in Node.Strings(anchor.Domain.Raw("unavailable")).Where(id => !heroes.Has(id)))
        {
            yield return $"domain.unavailable: unknown hero \"{id}\"";
        }
    }
}
