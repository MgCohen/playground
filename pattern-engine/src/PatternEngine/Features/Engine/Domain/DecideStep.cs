using System.Text.Json;
using System.Text.RegularExpressions;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>The answer of a decide or an escalation agent call, read into plain values. Code checks each value.</summary>
public sealed record Decision(
    string Status,
    string? Pattern,
    string? Variant,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<string> Children,
    IReadOnlyList<string> Open,
    string? Nearest,
    string? NewId,
    IReadOnlyList<string> Deltas,
    double? Margin,
    string? Reason)
{
    public const string Solved = "solved";
    public const string Partial = "partial";
    public const string None = "none";
    public const string Unclear = "unclear";
    public const string Outside = "outside";

    public static Decision From(AgentAnswer a) => new(
        a.Text("status") ?? string.Empty,
        a.Text("pattern"),
        a.Text("variant"),
        a.Map("params"),
        a.Texts("children"),
        a.Texts("open"),
        a.Text("nearest"),
        a.Text("new_id"),
        a.Texts("deltas"),
        a.Field("margin") is { ValueKind: JsonValueKind.Number } m ? m.GetDouble() : null,
        a.Text("reason"));
}

/// <summary>Why a node goes to the escalation agent. Each reason is a trigger in the settings.</summary>
public enum EscalationReason
{
    Unclear,
    NearTie,
    NotAllowed,
    RetriesUsedUp,
    OutOfDomain,
}

/// <summary>
/// The decide step: one agent call (decider) for one node, then code checks and applies the answer.
/// It returns the escalation reason when a trigger fires, else null.
/// </summary>
public static partial class DecideStep
{
    public static async Task<EscalationReason?> RunAsync(SolveContext s, WorkNode n, CancellationToken ct)
    {
        if (n.Depth > s.Settings.Node.MaxDepth)
        {
            n.Status = NodeStatus.Unresolved;
            n.Reason = "too deep";
            return s.Settings.Triggers.NotAllowed ? EscalationReason.NotAllowed : null;
        }

        var answer = await s.Agents.CallAsync("decider", $"Request: {n.Text}\n\nCandidates:\n{s.Candidates(n.Allowed)}", ct);
        if (!answer.Ok)
        {
            n.Status = NodeStatus.Unresolved;
            n.Reason = $"no answer: {answer.Error}";
            return null;
        }

        var d = Decision.From(answer);
        n.Reason = d.Reason;
        switch (d.Status)
        {
            case Decision.Unclear:
                n.Status = NodeStatus.Unresolved;
                n.Reason = $"unclear: {d.Reason}";
                return s.Settings.Triggers.Unclear ? EscalationReason.Unclear : null;
            case Decision.Outside:
                n.Status = NodeStatus.Unresolved;
                n.Reason = $"outside the domain: {d.Reason}";
                return s.Settings.Triggers.OutOfDomain ? EscalationReason.OutOfDomain : null;
        }

        if (s.Settings.Triggers.NearTie && d.Margin is { } margin && margin < s.Settings.Triggers.NearTieMargin && n.Escalations == 0)
        {
            n.Reason = $"near tie (margin {margin}): {d.Reason}";
            return EscalationReason.NearTie;
        }

        var problem = Apply(s, n, d);
        if (problem is null)
        {
            return null;
        }

        n.Status = NodeStatus.Unresolved;
        n.Reason = problem;
        return s.Settings.Triggers.NotAllowed ? EscalationReason.NotAllowed : null;
    }

    /// <summary>Applies a decision to a node. Code checks the pattern, the variant, the params and the parts. Returns a problem, or null.</summary>
    public static string? Apply(SolveContext s, WorkNode n, Decision d)
    {
        if (d.Status == Decision.None)
        {
            if (!s.Settings.AllowNoFit)
            {
                return "no-fit is switched off";
            }

            n.Status = NodeStatus.None;
            n.Nearest = n.Allowed.Contains(d.Nearest ?? string.Empty) ? d.Nearest
                : n.Allowed.Contains(d.Pattern ?? string.Empty) ? d.Pattern
                : n.Allowed.FirstOrDefault();
            n.Deltas.Clear();
            n.Deltas.AddRange(d.Deltas);
            n.NewId = CleanId(d.NewId);
            return null;
        }

        if (d.Status is not (Decision.Solved or Decision.Partial))
        {
            return $"unknown status \"{d.Status}\"";
        }

        var p = s.Catalog.Get(d.Pattern ?? string.Empty);
        if (p is null || !n.Allowed.Contains(p.Id))
        {
            return $"pattern \"{d.Pattern}\" is not allowed here";
        }

        if (OverCount(s, n, p.Id) is { } limit)
        {
            return limit;
        }

        n.Status = d.Status == Decision.Partial && d.Open.Count > 0 ? NodeStatus.Partial : NodeStatus.Solved;
        n.Pattern = p.Id;
        var variants = p.VariantIds.ToList();
        n.Variant = variants.Contains(d.Variant ?? string.Empty) ? d.Variant : variants.FirstOrDefault();
        n.Params.Clear();
        foreach (var (key, value) in d.Params)
        {
            var def = p.Param(key);
            var v = value.ToLowerInvariant();
            if (def is not null && (def.Values.Count == 0 || def.Values.Contains(v)))
            {
                n.Params[key] = v;
            }
        }

        var childIds = s.Catalog.ChildPatterns(p);
        if (childIds.Count > 0)
        {
            foreach (var text in d.Children)
            {
                n.Children.Add(s.Add(text, childIds, n.Depth + 1, n.Id).Id);
            }
        }

        if (n.Status == NodeStatus.Partial)
        {
            n.Open.Clear();
            n.Open.AddRange(d.Open);
            foreach (var text in d.Open)
            {
                var child = s.Add(text, s.TopIds, n.Depth + 1, n.Id);
                child.FromOpen = true;
                n.Children.Add(child.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// The part count of the parent pattern: a child decision that makes more parts of one pattern than the parent allows is not allowed.
    /// The check and the claim are one step, so two children decided at the same time cannot both take the last place.
    /// </summary>
    private static string? OverCount(SolveContext s, WorkNode n, string pattern)
    {
        if (n.FromOpen || n.Parent is not { } parentId)
        {
            return null;
        }

        var parent = s.Nodes[parentId];
        var spec = s.Catalog.Get(parent.Pattern ?? string.Empty)?.PartsFor(parent.Variant).FirstOrDefault(pt => pt.Ref == pattern);
        if (spec is null)
        {
            return null;
        }

        var max = PartCount.Parse(spec.Count).Max;
        lock (parent)
        {
            var taken = parent.Children.Select(i => s.Nodes[i]).Count(k => k != n && !k.FromOpen && k.Pattern == pattern && k.Status is NodeStatus.Solved or NodeStatus.Partial or NodeStatus.Asking);
            if (taken >= max)
            {
                return $"\"{parent.Pattern}\" allows at most {spec.Count} \"{pattern}\" parts, and {taken} are already decided";
            }

            n.Pattern = pattern;
            n.Status = NodeStatus.Solved;
            return null;
        }
    }

    public static string CleanId(string? id)
    {
        var clean = Edges().Replace(NotId().Replace((id ?? string.Empty).ToLowerInvariant(), "-"), string.Empty);
        return clean.Length > 0 ? clean : "new-pattern";
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NotId();

    [GeneratedRegex("^-+|-+$")]
    private static partial Regex Edges();
}
