using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

public sealed record Composed(string Fragment, bool Ok, IReadOnlyList<string> Failures);

/// <summary>
/// The compose step, bottom-up. Code assembles a group from the fragments of its children; an agent call (composer) draws
/// each other node in its slot, and code checks the fragment, with retries.
/// </summary>
public static class ComposeStep
{
    public static async Task<Composed> RunAsync(SolveContext s, WorkNode n, Slot slot, CancellationToken ct)
    {
        if (n.Status is NodeStatus.Unresolved or NodeStatus.Asking or NodeStatus.Open)
        {
            return new Composed(string.Empty, false, [n.Reason ?? n.Status]);
        }

        var layout = s.Pack.Pack.Layout;
        var partKids = n.Children.Select(i => s.Nodes[i]).Where(k => !k.FromOpen).ToList();
        if (n.Pattern is not null && layout.IsAssembled(n.Pattern) && partKids.Count > 0)
        {
            var slots = layout.Slots(n.Pattern, partKids.Count);
            var done = await Task.WhenAll(partKids.Select((k, i) => RunAsync(s, k, slots[i], ct)));
            return new Composed(layout.Assemble(n.Pattern, n.Variant, done.Select(d => d.Fragment).ToList(), s.Catalog), done.All(d => d.Ok), done.SelectMany(d => d.Failures).ToList());
        }

        if (n.Status == NodeStatus.None)
        {
            await AuthorStep.RunAsync(s, n, ct);
            if (n.Status == NodeStatus.Unresolved)
            {
                return new Composed(string.Empty, false, [n.Reason!]);
            }
        }

        var result = await DrawAsync(s, n, slot, ct);
        if (result.Ok || !s.Settings.Triggers.RetriesUsedUp || n.Escalations >= s.Settings.Node.Escalations)
        {
            return result;
        }

        n.Reason = $"compose failed: {string.Join("; ", result.Failures.Take(3))}";
        if (await EscalateStep.RunAsync(s, n, EscalationReason.RetriesUsedUp, ct))
        {
            return await DrawAsync(s, n, slot, ct);
        }

        return result;
    }

    private static async Task<Composed> DrawAsync(SolveContext s, WorkNode n, Slot slot, CancellationToken ct)
    {
        var name = n.Status == NodeStatus.None ? n.NewId! : n.Pattern!;
        var layout = s.Pack.Pack.Layout;
        var adapter = s.Pack.Pack.Adapter;
        IReadOnlyList<string> failures = [];
        var fragment = string.Empty;
        for (var i = 0; i < s.Settings.Node.ComposeRetries; i++)
        {
            lock (s)
            {
                s.Attempts++;
            }

            var answer = await s.Agents.CallAsync("composer", Input(s, n, name, slot, failures, fragment), ct);
            if (s.Agents.Stopped && !answer.Ok)
            {
                return new Composed(fragment, false, [s.Agents.StopReason!]);
            }

            fragment = (answer.Text("fragment") ?? string.Empty).Trim();
            var parsed = adapter.Parse(layout.Wrap(fragment));
            var list = new List<string>();
            list.AddRange(StructureGrader.Check(parsed, s.Catalog, adapter, n.Status == NodeStatus.None ? name : null).Failures);
            list.AddRange(adapter.Lint(parsed));
            list.AddRange(layout.CheckSlot(parsed.Roots.FirstOrDefault(), slot));
            if (parsed.Roots.FirstOrDefault() is { } root && root.Pattern != name)
            {
                list.Add($"the top pattern must be \"{name}\", got \"{root.Pattern}\"");
            }

            failures = list;
            if (failures.Count == 0)
            {
                return new Composed(fragment, true, []);
            }
        }

        return new Composed(fragment, false, failures);
    }

    private static string Input(SolveContext s, WorkNode n, string name, Slot slot, IReadOnlyList<string> failures, string last)
    {
        var what = n.Status == NodeStatus.None
            ? $"No pattern fits. Make the new pattern \"{name}\" (variant \"{n.Variant}\"). Use only known leaf parts.\n\nNew pattern file:\n{s.PatternText(name)}\n\nNearest pattern file ({n.Nearest}):\n{s.PatternText(n.Nearest!)}"
            : $"Pattern: {n.Pattern}, variant: {n.Variant}, params: {System.Text.Json.JsonSerializer.Serialize(n.Params)}.\n\nPattern file:\n{s.PatternText(n.Pattern!)}";
        var retry = failures.Count > 0 ? $"\n\nThe last attempt failed these checks. Fix them:\n- {string.Join("\n- ", failures)}\n\nThe last attempt:\n{last}" : string.Empty;
        return $"# Subject rules\n{s.Pack.Instructions}\n\nRequest: {n.Text}\n\n{what}\n\n{s.Pack.Pack.Layout.SlotText(slot)}{retry}";
    }
}
