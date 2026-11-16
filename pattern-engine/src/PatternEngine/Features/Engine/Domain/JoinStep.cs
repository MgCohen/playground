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
    /// <summary>An input of one item, and the outputs of other items with the same type.</summary>
    public sealed record Choice(string Input, string Type, IReadOnlyList<string> Options);

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

            plan = Finish(s.Catalog, items, wires, inputs, s.Pack.Pack.Layout);
        }

        foreach (var lone in items.Where(i => !plan.Wires.Any(w => w.From.StartsWith($"{i.Id}.", StringComparison.Ordinal) || w.To.StartsWith($"{i.Id}.", StringComparison.Ordinal))))
        {
            s.Note($"join: {lone.Id} ({lone.Pattern}) has no wire to another pattern of the solution");
        }

        if (Loop(plan.Wires))
        {
            return new Result(null, plan, ["join: the wires between the patterns make a loop"]);
        }

        var output = s.Pack.Pack.Layout.Join(items, plan);
        return output is null
            ? new Result(null, plan, [$"join: the pack \"{s.Pack.Pack.Name}\" cannot join a solution yet"])
            : new Result(output, plan, []);
    }

    /// <summary>
    /// The code part of the join. For each input: one output of another item with the same type gives a wire; none gives an outer input;
    /// more than one gives a choice. Each output that no wire uses is an outer output.
    /// </summary>
    public static (JoinPlan Plan, IReadOnlyList<Choice> Choices) Plan(PatternCatalog catalog, IReadOnlyList<SolutionItem> items, ILayout? layout = null)
    {
        var wires = new List<JoinWire>();
        var inputs = new List<string>();
        var choices = new List<Choice>();
        foreach (var item in items)
        {
            foreach (var port in Ports(catalog, item, layout).Inputs)
            {
                var options = items.Where(o => o.Id != item.Id)
                    .SelectMany(o => Ports(catalog, o, layout).Outputs.Where(x => x.Type == port.Type).Select(x => $"{o.Id}.{x.Name}")).ToList();
                var input = $"{item.Id}.{port.Name}";
                switch (options.Count)
                {
                    case 0:
                        inputs.Add($"{item.Id}_{port.Name}");
                        break;
                    case 1:
                        wires.Add(new JoinWire(options[0], input));
                        break;
                    default:
                        choices.Add(new Choice(input, port.Type, options));
                        break;
                }
            }
        }

        return (Finish(catalog, items, wires, inputs, layout), choices);
    }

    private static JoinPlan Finish(PatternCatalog catalog, IReadOnlyList<SolutionItem> items, List<JoinWire> wires, List<string> inputs, ILayout? layout)
    {
        var used = wires.Select(w => w.From).ToHashSet(StringComparer.Ordinal);
        var outputs = items.SelectMany(i => Ports(catalog, i, layout).Outputs.Select(x => $"{i.Id}.{x.Name}"))
            .Where(o => !used.Contains(o)).Select(o => o.Replace('.', '_')).ToList();
        return new JoinPlan(wires, inputs, outputs);
    }

    /// <summary>The ports of an item: from the catalog, or from the pack when the pattern has no ports in the catalog.</summary>
    private static (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs) Ports(PatternCatalog catalog, SolutionItem item, ILayout? layout) =>
        catalog.Get(item.Pattern) is { } p && p.Inputs.Count + p.Outputs.Count > 0 ? (p.Inputs, p.Outputs)
        : layout?.Ports(item.Fragment, catalog) ?? ([], []);

    private static bool Loop(IReadOnlyList<JoinWire> wires)
    {
        var edges = wires.Select(w => (From: w.From.Split('.')[0], To: w.To.Split('.')[0])).ToList();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            switch (state.GetValueOrDefault(id))
            {
                case 1:
                    return true;
                case 2:
                    return false;
            }

            state[id] = 1;
            var loop = edges.Where(e => e.From == id).Any(e => Visit(e.To));
            state[id] = 2;
            return loop;
        }

        return edges.Select(e => e.From).Distinct().Any(Visit);
    }

    private static List<JoinWire> Chosen(JsonElement? field) =>
        field is { ValueKind: JsonValueKind.Array } arr
            ? arr.EnumerateArray()
                .Where(w => w.ValueKind == JsonValueKind.Object && w.TryGetProperty("from", out _) && w.TryGetProperty("to", out _))
                .Select(w => new JoinWire(w.GetProperty("from").ToString(), w.GetProperty("to").ToString())).ToList()
            : [];

    private static string Input(SolveContext s, IReadOnlyList<SolutionItem> items, IReadOnlyList<Choice> choices)
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
