using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;

namespace PatternEngine.Features.Packs.Domain.Shared;

/// <summary>One catalog entry of the compose loop in the engine: one variant of one pattern (B2). The id is "pattern.variant".</summary>
public sealed record CatalogEntry(string Id, Pattern Pattern, string Variant)
{
    public static string IdOf(string pattern, string variant) => $"{pattern}.{variant}";
}

/// <summary>
/// The hooks of a pack for the compose loop in the engine (engine.decider: compose). The pack knows its words and its code checks;
/// the engine knows the loop, the steps and the trace.
/// </summary>
public interface IComposeHooks
{
    /// <summary>The known names of each need kind, for the decomposer input. For example the port types of the catalog.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Known(PatternCatalog catalog);

    /// <summary>Checks the needs of one decomposer answer, and writes the spec anchor. Errors go back to the decomposer.</summary>
    (Anchor? Spec, IReadOnlyList<string> Errors) Spec(string id, string text, IReadOnlyList<Need> needs, PatternCatalog catalog);

    /// <summary>The verdicts of code for one set (R1): the rubrics with check code, then the set checks of the pack.</summary>
    IReadOnlyList<RubricVerdict> CodeVerdicts(Anchor spec, IReadOnlyList<CatalogEntry> set, PatternCatalog catalog);
}
