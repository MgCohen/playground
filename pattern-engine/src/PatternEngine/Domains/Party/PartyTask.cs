using System.Text;
using PatternEngine.Features.Compose.Domain.Shared;

namespace PatternEngine.Domains.Party;

/// <summary>One party case as a compose task. A spec is a quest, an entry is a hero and a set is a party.</summary>
public sealed class PartyTask(PartyPack pack, PartyCase c, IReadOnlyList<string> fields, string strategy) : IComposeTask
{
    public static readonly string[] Fields = ["tags", "cost", "stats"];

    public string Id => c.Id;

    public IReadOnlyList<Anchor> Specs => c.Quests;

    public string Catalog => string.Join('\n', pack.Heroes.Heroes.Select(Line));

    public Anchor? Whole => c.Many ? c.Anchor : null;

    public string Strategy => File.ReadAllText(Path.Combine(pack.Dir, "strategies", $"{strategy}.md"));

    public string GraderRules => File.ReadAllText(Path.Combine(pack.Dir, "grading.md"));

    public bool HasEntry(string id) => pack.Heroes.Has(id);

    public string SetText(IReadOnlyList<string> ids) => string.Join('\n', ids.Select(pack.Heroes.Get).OfType<Hero>().Select(Line));

    /// <summary>The fields that the agents see for a rung, from the setting party.fields.</summary>
    public static IReadOnlyList<string> FieldsOf(PartyPack pack, string rung)
    {
        var fields = pack.Settings.Section("party", "fields").List(rung);
        var unknown = fields.Where(f => !Fields.Contains(f)).ToList();
        return unknown.Count == 0 ? fields : throw new InvalidOperationException($"party.fields.{rung}: unknown field {string.Join(", ", unknown)}");
    }

    private string Line(Hero h)
    {
        var b = new StringBuilder("- ").Append(h.Id).Append(':');
        var parts = new List<string>();
        if (fields.Contains("tags"))
        {
            parts.Add($"tags {string.Join(", ", h.Tags)}");
        }

        if (fields.Contains("cost"))
        {
            parts.Add($"cost {h.Cost}");
        }

        if (fields.Contains("stats"))
        {
            parts.Add(string.Join(", ", pack.Words.Stats.Select(s => $"{s} {h.Stat(s)}")));
        }

        return b.Append(' ').Append(string.Join("; ", parts)).ToString();
    }
}
