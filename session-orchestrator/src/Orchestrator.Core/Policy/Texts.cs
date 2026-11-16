using Orchestrator.Core.Contract;
using Orchestrator.Core.Templates;

namespace Orchestrator.Core.Policy;

public sealed record ReviewTarget(string Branch, string Base, string NextBranch);

public sealed class Texts(TemplateSet templates)
{
    public TemplateSet Templates => templates;

    public string DefaultNote(Policy policy, string decision, string reason) =>
        Notice($"default-{policy.OnDefault}", ("decision", decision), ("reason", reason));

    public string DocsNote(string? source) => Notice("docs", ("source", source ?? "no source given"));

    public string ReviewText(string decision, string question, string verdict, string text, ReviewTarget target) =>
        Notice($"review-{verdict}", ("decision", decision), ("question", question), ("text", text), ("branch", target.Branch), ("base", target.Base), ("nextBranch", target.NextBranch));

    public string AnswerTurn(string decision, string? question, string answer, string? note) =>
        templates.Render(Kinds.Answer, Fields(("decision", decision), ("question", question ?? "question"), ("text", answer), ("note", note)));

    public string ChildReport(string title, string id, string state, string summary, string? details) =>
        Notice("child-report", ("title", title), ("child", id), ("state", state), ("summary", summary), ("details", details));

    public string Blocked(int count, string error) => Notice("blocked", ("count", (double)count), ("error", error));

    public string Notice(string kind, params (string Key, object? Value)[] fields)
    {
        var data = Fields([("kind", kind), .. fields]);
        var check = templates.Validate(Kinds.Notice, data);
        if (!check.IsValid) throw new InvalidDataException($"notice {kind}: {check.Message}");
        return templates.Render(Kinds.Notice, data, kind);
    }

    static Dictionary<string, object?> Fields(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}

public static class Verdict
{
    public static bool IsValid(string? verdict) => verdict is not null && Verdicts.All.Contains(verdict);
}
