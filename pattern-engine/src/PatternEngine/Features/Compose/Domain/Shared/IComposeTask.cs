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

    /// <summary>The anchor with the rubrics over all sets together, for example "no entry in two sets". Null if the task has one spec only.</summary>
    Anchor? Whole { get; }

    /// <summary>The strategy text of the decider (a strategy file).</summary>
    string Strategy { get; }

    /// <summary>The rules of the pack for the grader.</summary>
    string GraderRules { get; }

    bool HasEntry(string id);

    /// <summary>The entries of one set, with the fields that the grader sees.</summary>
    string SetText(IReadOnlyList<string> ids);

    /// <summary>
    /// The verdicts that code gives for one set, when the setting compose.code_checks is on (R1): one for each rubric with check code,
    /// then one for each set check of the pack (for example "the ports connect"). A pack with no code checks gives none.
    /// </summary>
    IReadOnlyList<Domain.RubricVerdict> CodeVerdicts(Anchor spec, IReadOnlyList<string> set) => [];
}
