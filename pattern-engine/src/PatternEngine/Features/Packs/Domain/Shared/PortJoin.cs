using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Features.Packs.Domain.Shared;

/// <summary>
/// The port rule that joins complete patterns: the join step of the engine uses it for a solution, and a pack can use it to assemble
/// a group pattern. Only code; no agent call.
/// </summary>
public static class PortJoin
{
    /// <summary>An input of one item, and the outputs of other items with the same type.</summary>
    public sealed record Choice(string Input, string Type, IReadOnlyList<string> Options);

    /// <summary>
    /// The port rule of a join. For each input: one output of another item with the same type gives a wire; none gives an outer input;
    /// more than one gives a choice. Each output is also an outer output.
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

    public static JoinPlan Finish(PatternCatalog catalog, IReadOnlyList<SolutionItem> items, List<JoinWire> wires, List<string> inputs, ILayout? layout)
    {
        // Each output is also an output of the whole: an event can go to a part inside and to a pattern outside (fan-out).
        var outputs = items.SelectMany(i => Ports(catalog, i, layout).Outputs.Select(x => $"{i.Id}_{x.Name}")).ToList();
        return new JoinPlan(wires, inputs, outputs);
    }

    /// <summary>The ports of an item: from the catalog, or from the pack when the pattern has no ports in the catalog.</summary>
    public static (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs) Ports(PatternCatalog catalog, SolutionItem item, ILayout? layout) =>
        catalog.Get(item.Pattern) is { } p && p.Inputs.Count + p.Outputs.Count > 0 ? (p.Inputs, p.Outputs)
        : layout?.Ports(item.Fragment, catalog) ?? ([], []);

}
