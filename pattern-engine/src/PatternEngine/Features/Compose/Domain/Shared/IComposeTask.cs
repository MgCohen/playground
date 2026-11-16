namespace PatternEngine.Features.Compose.Domain.Shared;

/// <summary>
/// One composition task, as the loop sees it. A domain makes it: the specs (anchors), the catalog text, and the rules for the agents.
/// The loop knows only entry ids and rubric ids. It never decides a rubric.
/// </summary>
public interface IComposeTask
{
    string Id { get; }

    /// <summary>The specs of the run. The loop sends only the visible rubrics of each spec to the agents.</summary>
    IReadOnlyList<Anchor> Specs { get; }

    /// <summary>The text of the catalog, with only the fields that the agents see.</summary>
    string Catalog { get; }

    /// <summary>Extra context for the decider, for example the rubrics over all sets. Can be empty.</summary>
    string Brief { get; }

    /// <summary>The strategy text of the decider (a strategy file).</summary>
    string Strategy { get; }

    /// <summary>The rules of the pack for the grader.</summary>
    string GraderRules { get; }

    bool HasEntry(string id);

    /// <summary>The entries of one set, with the fields that the grader sees.</summary>
    string SetText(IReadOnlyList<string> ids);
}
