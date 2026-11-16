using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;

namespace PatternEngine.Features.Packs.Domain.Shared;

/// <summary>
/// A domain pack: everything about one subject behind one contract. The pack folder holds the data:
/// patterns/, cases/, fixtures/, instructions.md, pack.settings.yaml and optional agents/.
/// Optional hooks are null or empty when a pack does not have them.
/// </summary>
public interface IDomainPack
{
    string Name { get; }

    /// <summary>The folder name under the domains folder.</summary>
    string Folder { get; }

    string OutputFile { get; }

    IOutputAdapter Adapter { get; }

    ILayout Layout { get; }

    IReferenceRenderer? Reference { get; }

    IReadOnlyList<IMutationOperator> Mutations { get; }

    IReviewRenderer? Review { get; }

    /// <summary>Graders that only this pack has, for example a behavior grader that simulates the output.</summary>
    IReadOnlyList<IGrader> Graders { get; }
}

/// <summary>Draws an expected tree as a known-good output, with fixed geometry. Null if a pattern has no drawing.</summary>
public interface IReferenceRenderer
{
    string? Render(TreeNode tree, PatternCatalog catalog);
}

public sealed record MutationInput(string Output, TreeNode Tree, IOutputAdapter Adapter);

/// <summary>Breaks a known-good output in one way. Expect names the graders that must catch it (the right reason).</summary>
public interface IMutationOperator
{
    string Id { get; }

    IReadOnlyList<string> Expect { get; }

    string Describe { get; }

    string? Apply(MutationInput input);
}

/// <summary>A place for one composed pattern instance, in the units of the domain.</summary>
public sealed record Slot(IReadOnlyDictionary<string, double> Values)
{
    public double this[string key] => Values[key];
}

/// <summary>The compose hooks: where each part goes, how code joins the parts, and the check of one fragment in its slot.</summary>
public interface ILayout
{
    /// <summary>True if code assembles the pattern from the fragments of its children (no agent call).</summary>
    bool IsAssembled(string pattern);

    IReadOnlyList<Slot> Slots(string? pattern, int children);

    string SlotText(Slot slot);

    string Assemble(string pattern, string? variant, IReadOnlyList<string> fragments);

    string Wrap(string fragment);

    IReadOnlyList<string> CheckSlot(OutputNode? root, Slot slot);

    /// <summary>Joins the items of a solution into one output with the wires of the plan. Null means that this pack cannot join yet.</summary>
    string? Join(IReadOnlyList<SolutionItem> items, JoinPlan plan) => null;

    /// <summary>The typed ports of a fragment whose pattern has no ports in the catalog, for example a block that code assembles. Null if not known.</summary>
    (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs)? Ports(string fragment, PatternCatalog catalog) => null;
}

/// <summary>HTML for the review card.</summary>
public interface IReviewRenderer
{
    string RenderOutput(string output);

    string? RenderExpected(Case c, PatternCatalog catalog);
}
