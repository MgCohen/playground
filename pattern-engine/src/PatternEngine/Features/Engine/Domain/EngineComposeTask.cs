using System.Text;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>
/// One request of the engine as a compose task. The spec is the anchor of the decomposer. An entry is one variant of a composed
/// pattern (B2); a group pattern is not an entry, because code wraps a set in its group (B3). The pack gives the code checks.
/// </summary>
public sealed class EngineComposeTask(string id, Anchor spec, PatternCatalog catalog, IComposeHooks hooks, string strategy, string graderRules) : IComposeTask
{
    private readonly IReadOnlyDictionary<string, CatalogEntry> entries = Entries(catalog).ToDictionary(e => e.Id, StringComparer.Ordinal);

    public string Id => id;

    public IReadOnlyList<Anchor> Specs => [spec];

    public string Catalog => string.Join('\n', entries.Values.Select(Line));

    public Anchor? Whole => null;

    public string Strategy => strategy;

    public string GraderRules => graderRules;

    public bool HasEntry(string id) => entries.ContainsKey(id);

    public CatalogEntry? Entry(string id) => entries.GetValueOrDefault(id);

    public string SetText(IReadOnlyList<string> ids) => string.Join('\n', ids.Select(Entry).OfType<CatalogEntry>().Select(Line));

    public IReadOnlyList<RubricVerdict> CodeVerdicts(Anchor anchor, IReadOnlyList<string> set) =>
        hooks.CodeVerdicts(anchor, set.Select(Entry).OfType<CatalogEntry>().ToList(), catalog);

    /// <summary>The entries of a catalog: each variant of each active object pattern, lowest level first.</summary>
    public static IEnumerable<CatalogEntry> Entries(PatternCatalog catalog) =>
        catalog.Active.Where(p => p.Kind == PatternKind.Object).OrderBy(p => p.Level).ThenBy(p => p.Id, StringComparer.Ordinal)
            .SelectMany(p => p.Variants.Select(v => new CatalogEntry(CatalogEntry.IdOf(p.Id, v.Id), p, v.Id)));

    /// <summary>One line of an entry: what the decider and the grader see. The same fields as the candidates of the status decider.</summary>
    public static string Line(CatalogEntry e)
    {
        var p = e.Pattern;
        var v = p.Variants.First(x => x.Id == e.Variant);
        var b = new StringBuilder("- ").Append(e.Id).Append(": ").Append(p.Phrase ?? p.Id);
        if (v.Phrase is { } phrase)
        {
            b.Append(' ').Append(phrase);
        }

        b.Append(". Use when: ").Append(string.Join(" ", p.UseWhen));
        if (p.DoNotUseWhen.Count > 0)
        {
            b.Append(" Do not use when: ").Append(string.Join(" ", p.DoNotUseWhen.Select(d => $"{d.When.TrimEnd('.')} (use {d.Use}).")));
        }

        b.Append(" Variant: ").Append(v.When);
        b.Append(" Inputs: ").Append(Ports(p.Inputs)).Append(". Outputs: ").Append(Ports(p.Outputs)).Append('.');
        return b.ToString();
    }

    private static string Ports(IReadOnlyList<Port> ports) => ports.Count == 0 ? "none" : string.Join(", ", ports.Select(x => $"{x.Name} {x.Type}"));
}
