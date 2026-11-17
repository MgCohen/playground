using Microsoft.AspNetCore.Components;
using Orchestrator.Core.Templates;

namespace Orchestrator.Web.Components.Ui.Looks;

/// <summary>The value that a look shows: the text, the list items, the marked item and the color token. Place is fact or block.</summary>
public sealed record LookValue(string Place, string Text, IReadOnlyList<string> Items, string? Marked, string? Tone)
{
    public static LookValue Of(Fact fact) => new(LookCatalog.Fact, fact.Text, [fact.Text], null, fact.Tone);

    public static LookValue Of(Block block) => new(LookCatalog.Block, block.Text, block.Items, block.Marked, block.Tone);
}

/// <summary>The base of each look component: one Razor file for each look of templates/_spec/looks.json.</summary>
public abstract class LookBase : ComponentBase
{
    [Parameter, EditorRequired] public LookValue Value { get; set; } = default!;

    protected bool IsFact => Value.Place == LookCatalog.Fact;
}

/// <summary>The component of each look name. The name of a component is the look name in Pascal case, plus "Look".</summary>
public static class LookComponents
{
    static readonly Dictionary<string, Type> types = typeof(LookBase).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(LookBase)) && !t.IsAbstract && t.Name.EndsWith("Look", StringComparison.Ordinal))
        .ToDictionary(t => Name(t), t => t);

    public static IReadOnlyDictionary<string, Type> All => types;

    /// <summary>The component of a look. An unknown look shows as text.</summary>
    public static Type Of(string look) => types.GetValueOrDefault(look) ?? typeof(TextLook);

    static string Name(Type t) => t.Name[..^"Look".Length].ToLowerInvariant();

    public static Dictionary<string, object?> Parameters(LookValue value) => new() { [nameof(LookBase.Value)] = value };
}
