using System.Text;
using System.Text.Json;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Features.Engine.Domain;

/// <summary>
/// The join step: makes one solution from the main pattern and the remainder items. Code makes the wires from the port types
/// in the catalog. When an input can take more than one output, the joiner agent chooses, and code checks each choice.
/// The pack builds the output (<see cref="ILayout.Join"/>).
/// </summary>
public static class JoinStep
{
    public sealed record Result(string? Output, JoinPlan? Plan, IReadOnlyList<string> Failures);

    public static async Task<Result> RunAsync(SolveContext s, IReadOnlyList<SolutionItem> items, CancellationToken ct)
    {
        var (plan, choices) = Plan(s.Catalog, items, s.Pack.Pack.Layout);
        if (choices.Count > 0)
        {
            var answer = await s.Agents.CallAsync("joiner", Input(s, items, choices), ct);
            var chosen = answer.Ok ? Chosen(answer.Field("wires")) : [];
            var wires = plan.Wires.ToList();
            var inputs = plan.Inputs.ToList();
            foreach (var c in choices)
            {
                var pick = chosen.FirstOrDefault(w => w.To == c.Input);
                if (pick is not null && c.Options.Contains(pick.From))
                {
                    wires.Add(pick);
                }
                else
                {
                    s.Note($"join: no valid wire for {c.Input} (options {string.Join(", ", c.Options)}); it becomes an input of the solution");
                    inputs.Add(c.Input.Replace('.', '_'));
                }
            }

            plan = PortJoin.Finish(s.Catalog, items, wires, inputs, s.Pack.Pack.Layout);
        }

        foreach (var lone in items.Where(i => !plan.Wires.Any(w => w.From.StartsWith($"{i.Id}.", StringComparison.Ordinal) || w.To.StartsWith($"{i.Id}.", StringComparison.Ordinal))))
        {
            s.Note($"join: {lone.Id} ({lone.Pattern}) has no wire to another pattern of the solution");
        }

        if (PortJoin.Loop(plan.Wires))
        {
            return new Result(null, plan, ["join: the wires between the patterns make a loop"]);
        }

        var output = s.Pack.Pack.Layout.Join(items, plan);
        return output is null
            ? new Result(null, plan, [$"join: the pack \"{s.Pack.Pack.Name}\" cannot join a solution yet"])
            : new Result(output, plan, []);
    }

    /// <summary>The code part of the join: see <see cref="PortJoin.Plan"/>.</summary>
    public static (JoinPlan Plan, IReadOnlyList<PortJoin.Choice> Choices) Plan(PatternCatalog catalog, IReadOnlyList<SolutionItem> items, ILayout? layout = null) =>
        PortJoin.Plan(catalog, items, layout);

    private static List<JoinWire> Chosen(JsonElement? field) =>
        field is { ValueKind: JsonValueKind.Array } arr
            ? arr.EnumerateArray()
                .Where(w => w.ValueKind == JsonValueKind.Object && w.TryGetProperty("from", out _) && w.TryGetProperty("to", out _))
                .Select(w => new JoinWire(w.GetProperty("from").ToString(), w.GetProperty("to").ToString())).ToList()
            : [];

    private static string Input(SolveContext s, IReadOnlyList<SolutionItem> items, IReadOnlyList<PortJoin.Choice> choices)
    {
        var sb = new StringBuilder();
        sb.Append($"Request: {s.Nodes[0].Text}\n\nPatterns of the solution:\n");
        foreach (var item in items)
        {
            var p = s.Catalog.Get(item.Pattern);
            var text = s.Nodes.FirstOrDefault(n => (n.Pattern ?? n.NewId) == item.Pattern)?.Text;
            sb.Append($"- {item.Id}: {item.Pattern} ({p?.Phrase}), for \"{text}\"; inputs: {Names(p?.Inputs)}; outputs: {Names(p?.Outputs)}\n");
        }

        sb.Append("\nChoose one output for each input:\n");
        foreach (var c in choices)
        {
            sb.Append($"- {c.Input} ({c.Type}): one of {string.Join(", ", c.Options)}\n");
        }

        return sb.ToString();
    }

    private static string Names(IReadOnlyList<Port>? ports) => ports is { Count: > 0 } ? string.Join(", ", ports.Select(x => $"{x.Name} {x.Type}")) : "none";
}
