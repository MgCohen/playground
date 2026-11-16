namespace Orchestrator.Core.Policy;

public static class Texts
{
    public static string DefaultNote(Policy policy, string decision, string reason)
    {
        var head = $"This is a default decision ({reason}). The user did not answer yet and will review it later.";
        if (policy.OnDefault == OnDefault.None) return head;
        if (policy.OnDefault == OnDefault.Commit)
            return $"{head} Commit your current work first. Then make the work of this decision in its own commit, with \"decision {decision}\" in the commit message.";
        return string.Join('\n',
            head,
            "Before you act on it, keep the work of this decision apart so that the user can approve or refuse it alone:",
            "1. Commit your current work on the current branch.",
            $"2. Create the branch orch/decision-{decision} from the current branch.",
            "3. Make the work of this decision on that branch, and commit it.",
            "4. If the repository has a GitHub remote, push the branch and open a draft PR with the base set to the previous branch. Put the question, the default answer and the decision id in the PR description.",
            "5. Call mcp__orch__report with state working, and give the branch and the PR link in details.",
            "Continue your later work on top of this branch.");
    }

    public static string DocsNote(string? source) =>
        $"This answer comes from the documentation ({source ?? "no source given"}), not from the user. The user can review it later.";

    public static string ReviewText(string decision, string question, string verdict, string text)
    {
        var said = text.Length > 0 ? $" The user said: {text}" : "";
        return verdict switch
        {
            Contract.Verdicts.Approve => $"Review of decision {decision} ({question}): approved.{said} If it has a PR, mark it ready for review. Do not merge it unless the user asks.",
            Contract.Verdicts.Change => $"Review of decision {decision} ({question}): change requested.{said} Make the change on the branch orch/decision-{decision} and commit it. Then rebase each branch that is stacked above it, and push the branches that have a remote.",
            _ => $"Review of decision {decision} ({question}): refused.{said} Close the PR of orch/decision-{decision} and each PR stacked above it, with a comment that gives the reason. Keep the branches. Go back to the base branch of orch/decision-{decision} and continue from there in the direction that the user gave. Reuse work from the closed branches where it still fits.",
        };
    }

    public static string AnswerTurn(string decision, string? question, string answer, string? note) =>
        $"Answer to decision {decision} ({question ?? "question"}): {answer}{(string.IsNullOrEmpty(note) ? "" : $"\n\n{note}")}";

    public static string ChildReport(string title, string id, string state, string summary, string? details) =>
        $"Report from child \"{title}\" ({id}): {state}. {summary}{(string.IsNullOrEmpty(details) ? "" : $"\n\n{details}")}";
}
