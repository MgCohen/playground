using System.Text.Json;
using PatternEngine.Features.Catalog.Domain;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Messages;

namespace PatternEngine.Features.Catalog.Slices.BuildCatalog;

public sealed record BuildCatalogCommand(string? Domain) : ICommand<BuildCatalogResult>;

public sealed record BuildCatalogResult(string Domain, int Patterns, int Edges, string Hash, IReadOnlyList<Finding> Findings)
{
    public bool Ok => Findings.All(f => f.Severity != Severity.Error);
}

/// <summary>Validates the patterns of a pack and writes build/graph.json and build/index.md.</summary>
public sealed class BuildCatalogHandler(PackRegistry packs) : IHandler<BuildCatalogCommand, BuildCatalogResult>
{
    public async Task<Result<BuildCatalogResult>> HandleAsync(BuildCatalogCommand command, CancellationToken ct)
    {
        var pack = packs.Load(command.Domain);
        var findings = CatalogRules.Validate(pack.Catalog);
        var graph = CatalogViews.Graph(pack.Catalog);
        var result = new BuildCatalogResult(pack.Pack.Name, pack.Catalog.All.Count, graph.Edges.Count, pack.Catalog.Hash, findings);
        if (!result.Ok)
        {
            return result;
        }

        var dir = Path.Combine(pack.Dir, "build");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "graph.json"), JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
        await File.WriteAllTextAsync(Path.Combine(dir, "index.md"), CatalogViews.IndexMarkdown(pack.Catalog, pack.Pack.Name), ct);
        return result;
    }
}
