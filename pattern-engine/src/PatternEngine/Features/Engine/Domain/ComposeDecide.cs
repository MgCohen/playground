using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>The spec and the compose trace of one compose decision, for the run trace.</summary>
public sealed record ComposeRecord(Anchor? Spec, IReadOnlyList<string> SpecErrors, ComposeTrace? Trace);

/// <summary>
/// The decide phase of the engine with engine.decider compose (B1). The decomposer turns the request into a spec anchor (R2),
/// the compose loop picks a set of entries with the decider and the grader, and code turns the commit into work nodes:
/// a group when the set fits the parts of a group (B3), else one node and remainder nodes that the join step joins.
/// A gap becomes a no-fit remainder node, and the author step writes its pattern (B4).
/// </summary>
public static class ComposeDecide
{
    public static async Task<WorkNode> RunAsync(SolveContext s, string request, CancellationToken ct)
    {
        var root = s.Add(request, s.TopIds, 0, null);
        var hooks = s.Pack.Pack.Compose ?? throw new InvalidOperationException($"the pack \"{s.Pack.Pack.Name}\" has no compose hooks; set engine.decider to status");
        var settings = ComposeSettings.From(s.Pack.Settings);
        var (spec, errors) = await SpecAsync(s, hooks, request, ct);
        if (spec is null)
        {
            s.Compose = new ComposeRecord(null, errors, null);
            if (root.Status == NodeStatus.Open)
            {
                root.Status = NodeStatus.Unresolved;
                root.Reason = $"the decomposer gave no valid spec: {string.Join("; ", errors)}";
            }

            return root;
        }

        var task = new EngineComposeTask("request", spec, s.Catalog, hooks, PackFile(s, "strategies", $"{settings.Strategy}.md"), PackFile(s, "grading.md"));
        var trace = await new ComposeLoop(task, settings, s.Agents).RunAsync(ct);
        s.Compose = new ComposeRecord(spec, errors, trace);
        Apply(s, root, task, trace.Commits.Single(), trace);
        foreach (var n in s.Nodes.Where(n => n.Status == NodeStatus.Solved).ToList())
        {
            await AskStep.RunAsync(s, n, ct);
        }

        return root;
    }

    /// <summary>
    /// Calls the decomposer until code accepts the spec. A missing value goes to the ask step: the answer joins the text, and the
    /// decomposer runs again. With no answer, the root waits for the user (outcome ask).
    /// </summary>
    private static async Task<(Anchor? Spec, IReadOnlyList<string> Errors)> SpecAsync(SolveContext s, IComposeHooks hooks, string request, CancellationToken ct)
    {
        var retries = s.Pack.Settings.Section("compose", "decomposer").Int("retries");
        var rules = PackFile(s, "decomposer.md");
        var text = request;
        IReadOnlyList<string> errors = [];
        var attempts = 0;
        while (attempts <= retries)
        {
            attempts++;
            var answer = await s.Agents.CallAsync(Decompose.Agent, Decompose.Input(rules, text, hooks.Known(s.Catalog), null, errors), ct);
            if (!answer.Ok)
            {
                errors = [$"no answer: {answer.Error}"];
                continue;
            }

            var (needs, missing) = Decompose.Read(answer, lowerNames: false);
            if (missing.Count > 0)
            {
                var answers = new List<string>();
                foreach (var m in missing)
                {
                    var reply = await AskStep.AskAsync(s, new Question { Node = 0, About = QuestionTopics.Requirement, Key = "spec", Text = m }, ct);
                    if (reply is null)
                    {
                        s.Nodes[0].Status = NodeStatus.Asking;
                        s.Nodes[0].Reason = $"the spec needs an answer: {m}";
                        return (null, [$"missing: {m}"]);
                    }

                    answers.Add($"{m} {reply}");
                }

                // An answer is not a failed attempt. The ask step stops after engine.questions.max_rounds answers, so this ends.
                text = $"{text}\nAnswers of the user: {string.Join(" ", answers)}";
                attempts--;
                continue;
            }

            var (spec, problems) = hooks.Spec("request", text, needs, s.Catalog);
            if (spec is not null)
            {
                return (spec, []);
            }

            errors = problems;
        }

        return (null, errors);
    }

    /// <summary>Turns the commit into work nodes. Code does each step: the loop already holds each verdict.</summary>
    public static void Apply(SolveContext s, WorkNode root, EngineComposeTask task, SetCommit commit, ComposeTrace trace)
    {
        var entries = (commit.Set ?? []).Select(task.Entry).OfType<CatalogEntry>().ToList();
        var remainder = commit.Remainder ?? [];
        if (entries.Count == 0)
        {
            if (commit.Gap is { } only && s.Settings.AllowNoFit)
            {
                NoFit(root, only, task, remainder.Count > 0 ? remainder : task.Specs[0].Visible.Select(r => r.Id).ToList(), EngineComposeTask.Entries(s.Catalog).FirstOrDefault()?.Pattern.Id);
                return;
            }

            root.Status = NodeStatus.Unresolved;
            root.Reason = trace.Outcome == "committed" ? $"no set fits: {commit.Reason}" : $"compose {trace.Outcome}: {trace.StopReason}";
            return;
        }

        var group = commit.Gap is null ? Group(s.Catalog, entries) : null;
        if (group is not null)
        {
            Fill(root, group.Id, group.VariantIds.First());
            foreach (var e in entries)
            {
                var child = s.Add(root.Text, [e.Pattern.Id], 1, root.Id);
                Fill(child, e.Pattern.Id, e.Variant);
                root.Children.Add(child.Id);
            }
        }
        else
        {
            Fill(root, entries[0].Pattern.Id, entries[0].Variant);
            foreach (var e in entries.Skip(1))
            {
                var child = s.Add(root.Text, [e.Pattern.Id], 1, root.Id);
                child.FromOpen = true;
                Fill(child, e.Pattern.Id, e.Variant);
                root.Children.Add(child.Id);
            }
        }

        if (commit.Gap is { } gap)
        {
            if (!s.Settings.AllowNoFit)
            {
                s.Note($"compose: the gap \"{gap.NewId}\" is not used, because no-fit is switched off");
                return;
            }

            var asks = Asks(task, remainder);
            var node = s.Add(string.Join(" ", asks), s.TopIds, 1, root.Id);
            node.FromOpen = true;
            root.Children.Add(node.Id);
            root.Open.AddRange(asks);
            NoFit(node, gap, task, remainder, root.Pattern);
        }
    }

    /// <summary>The group pattern whose parts take the patterns of the set, in the counts of the parts. Null if none, or the set has one entry.</summary>
    public static Pattern? Group(PatternCatalog catalog, IReadOnlyList<CatalogEntry> set)
    {
        if (set.Count < 2)
        {
            return null;
        }

        var counts = set.GroupBy(e => e.Pattern.Id).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return catalog.Active.Where(p => p.Kind == PatternKind.Group).OrderBy(p => p.Level).FirstOrDefault(g =>
        {
            var parts = g.PartsFor(g.VariantIds.FirstOrDefault()).ToDictionary(pt => pt.Ref, pt => PartCount.Parse(pt.Count), StringComparer.Ordinal);
            return counts.All(c => parts.TryGetValue(c.Key, out var n) && c.Value <= n.Max)
                && parts.All(pt => counts.GetValueOrDefault(pt.Key) >= pt.Value.Min);
        });
    }

    private static void Fill(WorkNode n, string pattern, string variant)
    {
        n.Status = NodeStatus.Solved;
        n.Pattern = pattern;
        n.Variant = variant;
    }

    /// <summary>Makes a node a no-fit for a gap. With no nearest entry in the gap, the nearest pattern is the fallback.</summary>
    private static void NoFit(WorkNode n, Gap gap, EngineComposeTask task, IReadOnlyList<string> remainder, string? fallback)
    {
        n.Status = NodeStatus.None;
        n.NewId = DecideStep.CleanId(gap.NewId);
        n.Nearest = (gap.Nearest is { } near ? task.Entry(near)?.Pattern.Id : null) ?? fallback;
        n.Variant = "plain";
        n.Deltas.Clear();
        n.Deltas.AddRange(Asks(task, remainder));
        n.Reason = $"gap: no entry covers {string.Join(", ", remainder)}";
    }

    private static List<string> Asks(EngineComposeTask task, IReadOnlyList<string> remainder) =>
        remainder.Select(id => task.Specs[0].Rubrics.FirstOrDefault(r => r.Id == id)?.Ask ?? id).ToList();

    private static string PackFile(SolveContext s, params string[] parts)
    {
        var file = Path.Combine([s.Pack.Dir, .. parts]);
        return File.Exists(file) ? File.ReadAllText(file) : throw new FileNotFoundException($"the pack \"{s.Pack.Pack.Name}\" has no {string.Join('/', parts)} for the compose decider", file);
    }
}
