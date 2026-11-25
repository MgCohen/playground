using Orchestrator.Core.Contract;
using Orchestrator.Core.Templates;

namespace Orchestrator.Core.Policy;

public sealed record ReviewTarget(string Branch, string Base, string NextBranch);

/// <summary>The fixed texts. Each one is a render of a template, so there is one source for each text.</summary>
public sealed class Texts(TemplateSet templates)
{
    public TemplateSet Templates => templates;

    /// <summary>The note that goes with an answer of the policy or the docs (answer/render/note-*.md).</summary>
    public string? AnswerNote(string? by, string? onDefault, string decision, string? reason, string? source) => by switch
    {
        "policy" => templates.Render(Kinds.Answer, Fields(("decision", decision), ("reason", reason ?? "")), $"note-default-{onDefault ?? OnDefault.StackedPr}"),
        "docs" => templates.Render(Kinds.Answer, Fields(("source", source ?? "no source given")), "note-docs"),
        _ => null,
    };

    public static string DefaultText(Policy policy, string risk, string? defaultOption)
    {
        var level = policy.LevelOf(risk);
        return defaultOption is not { } option ? "There is no default: the child waits for an answer."
            : level.TimeoutSeconds is { } seconds ? $"If nobody answers in {seconds:0} s, the policy answers \"{option}\"."
            : $"The default is \"{option}\", but this level never applies it.";
    }

    /// <summary>Checks the fields of a notice kind against the notice schema.</summary>
    public Dictionary<string, object?> Notice(string kind, params (string Key, object? Value)[] fields)
    {
        var data = Fields([("kind", kind), .. fields]);
        var check = templates.Validate(Kinds.Notice, data);
        if (!check.IsValid) throw new InvalidDataException($"notice {kind}: {check.Message}");
        return data;
    }

    public static Dictionary<string, object?> Fields(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}

public static class Verdict
{
    public static bool IsValid(string? verdict) => verdict is not null && Verdicts.All.Contains(verdict);
}
