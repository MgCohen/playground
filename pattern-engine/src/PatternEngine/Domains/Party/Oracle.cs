using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Domains.Party;

/// <summary>The exact answer of one quest: the valid parties with the smallest size and with the best cost. Null values mean "none".</summary>
public sealed record QuestBest(int ValidCount, int? SmallestSize, IReadOnlyList<IReadOnlyList<string>> Smallest, int? BestCost, IReadOnlyList<IReadOnlyList<string>> Cheapest)
{
    public bool None => ValidCount == 0;
}

/// <summary>A party for each quest, in the order of the quests. A null party means "none" for that quest.</summary>
public sealed record Assignment(IReadOnlyList<IReadOnlyList<string>?> Parties, int? Total)
{
    public bool Complete => Parties.All(p => p is not null);
}

/// <summary>
/// The oracle of the party domain. It reads the typed args of each rubric and searches all parties of the catalog.
/// The run never uses it: it decides the pass after the run.
/// </summary>
public sealed class Oracle(HeroCatalog catalog)
{
    private readonly IReadOnlyList<Hero> heroes = catalog.Heroes;

    public HeroCatalog Catalog => catalog;

    /// <summary>The truth of one rubric for one party. All args of the rubric must hold.</summary>
    public static bool Truth(Rubric rubric, IReadOnlyList<Hero> party)
    {
        var args = rubric.Args;
        if (args.Get("tag") is { } tag && !party.Any(h => h.Tags.Contains(tag)))
        {
            return false;
        }

        if (args.Get("max_cost") is { } max && party.Sum(h => h.Cost) > Int(max))
        {
            return false;
        }

        if (args.Get("stat") is { } stat && party.Sum(h => h.Stat(stat)) < Int(args.Get("min") ?? "0"))
        {
            return false;
        }

        if (args.ContainsKey("pool"))
        {
            throw new InvalidOperationException($"rubric \"{rubric.Id}\" is about the full assignment; use PoolTruth");
        }

        return !Node.Strings(args.Raw("unavailable")).Any(id => party.Any(h => h.Id == id));
    }

    /// <summary>The truth of a rubric over the full assignment. pool: distinct means that no hero is in two parties.</summary>
    public static bool PoolTruth(Rubric rubric, IReadOnlyList<IReadOnlyList<string>?> parties) =>
        rubric.Args.Get("pool") != "distinct" || parties.OfType<IReadOnlyList<string>>().SelectMany(p => p).GroupBy(id => id).All(g => g.Count() == 1);

    /// <summary>The heroes of a party, or null if a hero id is not in the catalog or is in the party twice.</summary>
    public IReadOnlyList<Hero>? Resolve(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count)
        {
            return null;
        }

        var party = ids.Select(catalog.Get).OfType<Hero>().ToList();
        return party.Count == ids.Count ? party : null;
    }

    /// <summary>The rubrics of the quest that are false for the party. Unknown ids make all rubrics false.</summary>
    public IReadOnlyList<Rubric> Failed(Anchor quest, IReadOnlyList<string> ids) =>
        Resolve(ids) is { } party ? quest.Rubrics.Where(r => !Truth(r, party)).ToList() : quest.Rubrics;

    public bool Valid(Anchor quest, IReadOnlyList<string> ids) => Failed(quest, ids).Count == 0;

    /// <summary>Searches each party of the catalog (each subset with one hero or more).</summary>
    public QuestBest Best(Anchor quest)
    {
        var valid = ValidMasks(quest, 0);
        if (valid.Count == 0)
        {
            return new QuestBest(0, null, [], null, []);
        }

        var smallest = valid.Min(v => v.Size);
        var cheapest = valid.Min(v => v.Cost);
        return new QuestBest(
            valid.Count,
            smallest,
            valid.Where(v => v.Size == smallest).Select(v => Ids(v.Mask)).ToList(),
            cheapest,
            valid.Where(v => v.Cost == cheapest).Select(v => Ids(v.Mask)).ToList());
    }

    /// <summary>The best assignment of a case: a valid party for each quest, the case rubrics true, the smallest total cost. Null total = none.</summary>
    public Assignment BestAssignment(PartyCase c)
    {
        var distinct = c.CaseRubrics.Any(r => r.Args.Get("pool") == "distinct");
        var options = c.Quests.Select(q => ValidMasks(q, 0).OrderBy(v => v.Cost).ToList()).ToList();
        int? best = null;
        int[]? bestPick = null;
        var pick = new int[options.Count];

        void Search(int i, int used, int total)
        {
            if (best is { } b && total >= b && i < options.Count)
            {
                return;
            }

            if (i == options.Count)
            {
                if (best is null || total < best)
                {
                    best = total;
                    bestPick = (int[])pick.Clone();
                }

                return;
            }

            foreach (var o in options[i])
            {
                if (best is { } limit && total + o.Cost >= limit)
                {
                    break;
                }

                if (distinct && (o.Mask & used) != 0)
                {
                    continue;
                }

                pick[i] = o.Mask;
                Search(i + 1, used | o.Mask, total + o.Cost);
            }
        }

        Search(0, 0, 0);
        return bestPick is null
            ? new Assignment(c.Quests.Select(_ => (IReadOnlyList<string>?)null).ToList(), null)
            : new Assignment(bestPick.Select(m => (IReadOnlyList<string>?)Ids(m)).ToList(), best);
    }

    /// <summary>The "quests in order" baseline: the cheapest party for each quest in turn, from the heroes that are left.</summary>
    public Assignment InOrder(PartyCase c)
    {
        var distinct = c.CaseRubrics.Any(r => r.Args.Get("pool") == "distinct");
        var used = 0;
        var parties = new List<IReadOnlyList<string>?>();
        var total = 0;
        foreach (var q in c.Quests)
        {
            var o = ValidMasks(q, distinct ? used : 0).OrderBy(v => v.Cost).ThenBy(v => v.Size).ThenBy(v => string.Join(",", Ids(v.Mask)), StringComparer.Ordinal).FirstOrDefault();
            if (o.Size == 0)
            {
                parties.Add(null);
                continue;
            }

            used |= o.Mask;
            total += o.Cost;
            parties.Add(Ids(o.Mask));
        }

        return new Assignment(parties, parties.All(p => p is not null) ? total : null);
    }

    public int Cost(IReadOnlyList<string> ids) => ids.Sum(id => catalog.Get(id)?.Cost ?? 0);

    private List<(int Mask, int Size, int Cost)> ValidMasks(Anchor quest, int excluded)
    {
        var output = new List<(int, int, int)>();
        var party = new List<Hero>(heroes.Count);
        for (var mask = 1; mask < 1 << heroes.Count; mask++)
        {
            if ((mask & excluded) != 0)
            {
                continue;
            }

            party.Clear();
            for (var i = 0; i < heroes.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    party.Add(heroes[i]);
                }
            }

            if (quest.Rubrics.All(r => Truth(r, party)))
            {
                output.Add((mask, party.Count, party.Sum(h => h.Cost)));
            }
        }

        return output;
    }

    private IReadOnlyList<string> Ids(int mask) =>
        Enumerable.Range(0, heroes.Count).Where(i => (mask & (1 << i)) != 0).Select(i => heroes[i].Id).ToList();

    private static int Int(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
