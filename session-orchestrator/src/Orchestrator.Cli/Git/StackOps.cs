using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Cli.Git;

public sealed class StackOps(FileStore store, Shell shell, TextWriter output)
{
    const int TitleAnswerLength = 60;

    public void Start(string decision)
    {
        var session = SessionOf(decision);
        var entry = store.Stack(session, decision) ?? new StackEntry(decision, session);
        var newest = entry.Newest;
        if (newest is { State: not VersionStates.Planned and not VersionStates.Closed })
        {
            shell.Checkout(newest.Branch);
            output.WriteLine($"Branch {newest.Branch} is already started, on top of {newest.Base}. It is checked out.");
            return;
        }
        if (newest is { State: VersionStates.Planned }) shell.Checkout(newest.Base);
        var version = newest is { State: VersionStates.Planned } ? newest.Version : (newest?.Version ?? 0) + 1;
        var branch = StackNames.Branch(decision, version);
        var baseBranch = shell.CurrentBranch();
        if (shell.BranchExists(branch)) shell.Checkout(branch);
        else shell.MustGit("checkout", "--quiet", "-b", branch);
        var started = new StackVersion(version, branch, baseBranch)
        {
            BaseDecision = StackNames.Parse(baseBranch)?.Decision,
            State = VersionStates.Started,
            Replaces = newest?.Replaces ?? entry.Versions.LastOrDefault(v => v.Version < version)?.Pr,
        };
        store.Events.Append(EventKinds.BranchCreate, Parties.Session(session), Parties.User, Messages.Fields(("text", $"{branch} on top of {baseBranch}"), ("branch", branch), ("base", baseBranch)), Messages.Fields(("decision", decision), ("version", version)), session);
        store.SaveStack(entry.With(started));
        output.WriteLine($"Branch {branch} is on top of {baseBranch}. Make the work of the decision here and commit it. Then run: orch decision pr {decision}");
    }

    public void OpenPr(string decision)
    {
        var (session, entry, current) = Current(decision);
        if (current.Pr is { } existing)
        {
            output.WriteLine($"PR #{existing} is already open: {current.Url}");
            return;
        }
        if (!shell.HasGitHubRemote())
        {
            output.WriteLine($"The repository has no GitHub remote. The branch {current.Branch} stays local, with no PR.");
            return;
        }
        shell.Push(current.Branch);
        var fields = PrFields(session, decision, entry, current);
        var title = store.Templates.Render(Kinds.Question, fields, "pr-title");
        var body = store.Templates.Render(Kinds.Question, fields, "pr-body");
        var bodyFile = Path.GetTempFileName();
        File.WriteAllText(bodyFile, body);
        string url;
        try
        {
            url = shell.Must("gh", "pr", "create", "--draft", "--base", current.Base, "--head", current.Branch, "--title", title, "--body-file", bodyFile)
                .Split('\n').Select(l => l.Trim()).Last(l => l.StartsWith("https://"));
        }
        finally
        {
            File.Delete(bodyFile);
        }
        var number = int.Parse(url.TrimEnd('/').Split('/')[^1]);
        store.Events.Append(EventKinds.PrOpen, Parties.Session(session), Parties.User, Messages.Fields(("text", title), ("url", url)), Messages.Fields(("decision", decision), ("version", current.Version), ("pr", number)), session);
        store.SaveStack(entry.With(current with { Pr = number, Url = url, State = VersionStates.Open }));
        output.WriteLine($"Draft PR #{number}: {url}");
    }

    public void Ready(string decision)
    {
        var (session, entry, current) = Current(decision);
        if (current.Pr is not { } pr)
        {
            output.WriteLine($"The branch {current.Branch} has no PR. Nothing to mark ready.");
            return;
        }
        shell.Must("gh", "pr", "ready", pr.ToString());
        store.Events.Append(EventKinds.PrReady, Parties.Session(session), Parties.User, Messages.Fields(("text", $"PR #{pr} is ready for review")), Messages.Fields(("decision", decision), ("pr", pr)), session);
        store.SaveStack(entry.With(current with { State = VersionStates.Ready }));
        output.WriteLine($"PR #{pr} is ready for review. Do not merge it unless the user asks.");
    }

    public void Rebase(string decision)
    {
        var (session, _, current) = Current(decision);
        var remote = shell.HasGitHubRemote();
        if (remote && current.Pr is not null) shell.Push(current.Branch);
        var top = current.Branch;
        foreach (var (childEntry, childVersion, parentBranch) in Above(session, current.Branch))
        {
            shell.Checkout(childVersion.Branch);
            var rebase = shell.Git("rebase", "--quiet", parentBranch);
            if (!rebase.Ok)
            {
                shell.Git("rebase", "--abort");
                throw new ShellException($"rebase of {childVersion.Branch} on {parentBranch} has a conflict. The rebase is stopped. Fix it by hand, then run this command again.");
            }
            if (remote && childVersion.Pr is not null) shell.Push(childVersion.Branch);
            store.Events.Append(EventKinds.BranchRebase, Parties.Session(session), Parties.User, Messages.Fields(("text", $"{childVersion.Branch} rebased on {parentBranch}")), Messages.Fields(("decision", childEntry.Decision)), session);
            output.WriteLine($"Rebased {childVersion.Branch} on {parentBranch}{(remote && childVersion.Pr is not null ? " and pushed it" : "")}.");
            top = childVersion.Branch;
        }
        shell.Checkout(top);
        output.WriteLine($"Checked out {top}, the top of the stack.");
    }

    public void Close(string decision, string? said)
    {
        var (session, entry, current) = Current(decision);
        var question = store.Decision(session, decision)?.Question ?? "question";
        var chain = new List<(StackEntry Entry, StackVersion Version, string? ParentDecision)> { (entry, current, null) };
        chain.AddRange(Above(session, current.Branch).Select(a => (a.Entry, a.Version, StackNames.Parse(a.ParentBranch)?.Decision)));
        var planned = new Dictionary<string, StackVersion>();
        foreach (var (e, v, parent) in chain)
        {
            var next = e.Newest!.Version + 1;
            var baseBranch = parent is not null && planned.TryGetValue(parent, out var p) ? p.Branch : current.Base;
            planned[e.Decision] = new StackVersion(next, StackNames.Branch(e.Decision, next), baseBranch)
            {
                BaseDecision = parent ?? current.BaseDecision,
                Replaces = v.Pr,
            };
        }
        foreach (var (e, v, parent) in chain)
        {
            var plan = planned[e.Decision];
            if (v.Pr is { } pr)
            {
                var comment = parent is null
                    ? store.Templates.Render(Kinds.Question, Messages.Fields(("decision", decision), ("question", question), ("said", said), ("branch", v.Branch), ("nextBranch", plan.Branch)), "pr-close")
                    : store.Templates.Render(Kinds.Question, Messages.Fields(("refusedPr", current.Pr?.ToString() ?? "?"), ("branch", v.Branch), ("nextBranch", plan.Branch)), "pr-close-above");
                shell.Must("gh", "pr", "close", pr.ToString(), "--comment", comment);
                store.Events.Append(EventKinds.PrClose, Parties.Session(session), Parties.User, Messages.Fields(("text", $"PR #{pr} closed"), ("comment", comment)), Messages.Fields(("decision", e.Decision), ("pr", pr)), session);
            }
            store.SaveStack(e.With(v with { State = VersionStates.Closed }).With(plan));
            output.WriteLine($"Closed {v.Branch}{(v.Pr is { } n ? $" (PR #{n})" : "")}. The branch stays. Next version: {plan.Branch} on top of {plan.Base}.");
        }
        shell.Checkout(current.Base);
        output.WriteLine($"Checked out {current.Base}. Now run, in this order:");
        foreach (var (e, _, _) in chain) output.WriteLine($"  orch decision start {e.Decision}   (makes {planned[e.Decision].Branch})");
    }

    Dictionary<string, object?> PrFields(string session, string decision, StackEntry entry, StackVersion current)
    {
        var answer = store.Answer(session, decision);
        var previous = entry.Previous(current);
        var refused = previous is not null ? store.Review(session, StackNames.VersionId(decision, previous.Version)) is { Verdict: Verdicts.Refuse } r ? r : null : null;
        var text = refused?.Text ?? answer?.Value ?? "";
        var reason = refused is not null ? $"the user refused version {previous!.Version} and gave this direction" : answer?.Reason ?? $"answer by {answer?.By ?? "the user"}";
        var origin = refused is not null ? "direction of the user" : answer?.By == Parties.Policy ? "default decision" : $"answer by {answer?.By ?? "the user"}";
        var basePr = current.BaseDecision is { } bd ? store.Stack(session, bd)?.Versions.LastOrDefault(v => v.Branch == current.Base)?.Pr : null;
        return Messages.Fields(("decision", decision), ("question", store.Decision(session, decision)?.Question ?? "question"),
            ("answer", text.Length > TitleAnswerLength ? text[..TitleAnswerLength] + "…" : text), ("reason", reason), ("origin", origin),
            ("version", current.Version.ToString()), ("branch", current.Branch), ("base", current.Base), ("basePr", basePr?.ToString()), ("replaces", current.Replaces?.ToString()));
    }

    IEnumerable<((StackEntry Entry, StackVersion Version) Child, string ParentBranch)> AboveRaw(string session, string branch) =>
        store.Stacks(session).Select(e => (Entry: e, Version: e.Current)).Where(c => c.Version is { State: not VersionStates.Closed } v && v.Base == branch)
            .Select(c => ((c.Entry, c.Version!), branch));

    IEnumerable<(StackEntry Entry, StackVersion Version, string ParentBranch)> Above(string session, string branch)
    {
        var queue = new Queue<string>([branch]);
        var seen = new HashSet<string> { branch };
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var (child, parentBranch) in AboveRaw(session, parent))
            {
                if (!seen.Add(child.Version.Branch)) continue;
                queue.Enqueue(child.Version.Branch);
                yield return (child.Entry, child.Version, parentBranch);
            }
        }
    }

    (string Session, StackEntry Entry, StackVersion Current) Current(string decision)
    {
        var session = SessionOf(decision);
        var entry = store.Stack(session, decision) ?? throw new UsageException($"decision {decision} has no branch. Run: orch decision start {decision}");
        var current = entry.Versions.LastOrDefault(v => v.State is VersionStates.Started or VersionStates.Open or VersionStates.Ready)
            ?? throw new UsageException($"decision {decision} has no open version. Run: orch decision start {decision}");
        return (session, entry, current);
    }

    string SessionOf(string decision) => store.SessionOf(decision) ?? throw new UsageException($"no session has decision {decision}");
}
