using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;

namespace Orchestrator.Cli.Git;

public sealed record CarriedDecision(string Session, string Decision, int Version, string Branch, string? Question, bool IsDefault, string? Verdict, bool Allowed)
{
    public string VersionId => StackNames.VersionId(Decision, Version);

    public bool Blocks => IsDefault && Verdict != Verdicts.Approve && !Allowed;
}

public sealed record MergeTarget(string Head, string Base, int? Pr);

public sealed class MergeGuard(FileStore store, Shell shell, TextWriter output)
{
    public IReadOnlyList<CarriedDecision> Carried(MergeTarget target, bool remote)
    {
        var head = Ref(target.Head, remote);
        var baseRef = Ref(target.Base, remote);
        var carried = new List<CarriedDecision>();
        foreach (var session in store.AllSessions())
            foreach (var entry in store.Stacks(session))
                foreach (var version in entry.Versions.Where(v => v.State is not VersionStates.Planned and not VersionStates.Closed))
                {
                    var tip = new[] { Ref(version.Branch, remote), version.Branch }.FirstOrDefault(r => shell.Git("rev-parse", "--verify", "--quiet", r).Ok);
                    if (tip is null) continue;
                    if (!IsAncestor(tip, head) || IsAncestor(tip, baseRef)) continue;
                    var answer = store.Answer(session, entry.Decision);
                    var review = store.Review(session, StackNames.VersionId(entry.Decision, version.Version));
                    carried.Add(new CarriedDecision(session, entry.Decision, version.Version, version.Branch, store.Decision(session, entry.Decision)?.Question,
                        answer?.By == Parties.Policy, review?.Verdict, AllowsDefaults(session)));
                }
        return carried;
    }

    public int Merge(MergeTarget target)
    {
        var remote = target.Pr is not null;
        if (remote) shell.MustGit("fetch", "--quiet", "origin");
        var carried = Carried(target, remote);
        var blockers = carried.Where(c => c.Blocks).ToList();
        var name = target.Pr is { } n ? $"PR #{n}" : target.Head;
        if (blockers.Count > 0)
        {
            foreach (var b in blockers)
            {
                store.Events.Append(EventKinds.MergeBlock, Parties.Session(b.Session), Parties.User,
                    Messages.Fields(("text", $"{name} is blocked: default decision {b.VersionId} has no approve review"), ("head", target.Head), ("base", target.Base)),
                    Messages.Fields(("decision", b.Decision), ("version", b.VersionId), ("pr", target.Pr)), b.Session);
            }
            output.WriteLine($"{name} is not merged. It carries default decisions that the user did not approve:");
            foreach (var b in blockers) output.WriteLine($"  {b.VersionId} on {b.Branch}: {b.Question}{(b.Verdict is { } v ? $" (review: {v})" : "")}");
            output.WriteLine("Report the PR as ready and wait. The user reviews each one with: orch review <decision> approve|change|refuse");
            return 1;
        }
        if (target.Pr is { } pr) shell.Must("gh", "pr", "merge", pr.ToString(), "--merge");
        else
        {
            shell.Checkout(target.Base);
            shell.MustGit("merge", "--quiet", "--no-ff", "-m", $"Merge {target.Head}", target.Head);
        }
        foreach (var c in carried)
        {
            if (store.Stack(c.Session, c.Decision) is { } entry && entry.Versions.FirstOrDefault(v => v.Version == c.Version) is { } version)
                store.SaveStack(entry.With(version with { State = VersionStates.Merged }));
        }
        var session = carried.FirstOrDefault()?.Session;
        store.Events.Append(EventKinds.PrMerge, session is null ? Parties.User : Parties.Session(session), Parties.User,
            Messages.Fields(("text", $"{name} merged into {target.Base}"), ("head", target.Head), ("base", target.Base)),
            Messages.Fields(("pr", target.Pr), ("decisions", carried.Select(c => c.VersionId).ToArray())), session);
        output.WriteLine($"{name} is merged into {target.Base}.{(carried.Count > 0 ? $" It carried: {string.Join(", ", carried.Select(c => c.VersionId))}." : "")}");
        return 0;
    }

    public MergeTarget Resolve(string target, string? baseBranch)
    {
        if (int.TryParse(target, out var pr))
        {
            var json = JsonDocument.Parse(shell.Must("gh", "pr", "view", pr.ToString(), "--json", "headRefName,baseRefName")).RootElement;
            return new MergeTarget(json.GetProperty("headRefName").GetString()!, json.GetProperty("baseRefName").GetString()!, pr);
        }
        return new MergeTarget(target, baseBranch ?? "main", null);
    }

    bool AllowsDefaults(string session)
    {
        for (var id = session; id is not null; id = store.Meta(id).Parent)
            if (store.Meta(id).AllowDefaults == true) return true;
        return false;
    }

    bool IsAncestor(string ancestor, string of) => shell.Git("merge-base", "--is-ancestor", ancestor, of).Ok;

    string Ref(string branch, bool remote) => remote ? $"origin/{branch}" : branch;
}
