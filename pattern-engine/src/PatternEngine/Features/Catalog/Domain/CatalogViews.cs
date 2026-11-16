using System.Text;

using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Features.Catalog.Domain;

public sealed record GraphNode(string Id, string Kind, int Level, string Status);

public sealed record GraphEdge(string From, string To, string Type, string? Count = null, string? Only = null);

public sealed record CatalogGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

/// <summary>The graph and the index of a catalog. The index is what an agent reads first; proposed patterns are not in it.</summary>
public static class CatalogViews
{
    public static CatalogGraph Graph(PatternCatalog catalog)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        foreach (var p in catalog.All)
        {
            nodes.Add(new GraphNode(p.Id, Lower(p.Kind), p.Level, Lower(p.Status)));
            edges.AddRange(p.Parts.Select(pt => new GraphEdge(p.Id, pt.Ref, "part", pt.Count, pt.Only)));
            edges.AddRange(p.DoNotUseWhen.Where(d => d.Use != InsteadRule.NoFit).Select(d => new GraphEdge(p.Id, d.Use, "instead")));
            edges.AddRange(p.Related.Select(r => new GraphEdge(p.Id, r, "related")));
        }

        return new CatalogGraph(nodes, edges);
    }

    public static string IndexMarkdown(PatternCatalog catalog, string domain)
    {
        var rows = catalog.Active.OrderBy(p => p.Level).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.Append($"# Pattern index: {domain}\n\nRead the full file `patterns/<id>.md` before you use a pattern.\n\n");
        sb.Append("## Composed patterns\n\n| id | level | status | use when | do not use when | variants |\n|---|---|---|---|---|---|\n");
        foreach (var p in rows.Where(p => p.Kind != PatternKind.Leaf))
        {
            var instead = string.Join("<br>", p.DoNotUseWhen.Select(d => d.Use == InsteadRule.NoFit ? $"{d.When} → no pattern fits" : $"{d.When} → use `{d.Use}`"));
            var variants = string.Join("<br>", p.Variants.Select(v => $"`{v.Id}`: {v.When}"));
            sb.Append($"| `{p.Id}` | {p.Level} | {Lower(p.Status)} | {string.Join("<br>", p.UseWhen)} | {(instead.Length > 0 ? instead : "—")} | {variants} |\n");
        }

        sb.Append("\n## Leaf parts\n\n| id | level | primitive |\n|---|---|---|\n");
        foreach (var p in rows.Where(p => p.Kind == PatternKind.Leaf))
        {
            sb.Append($"| `{p.Id}` | {p.Level} | {p.Primitive} |\n");
        }

        return sb.ToString();
    }

    public static string Lower<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();
}
