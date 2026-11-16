using System.Text.Json.Nodes;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Blocks;

/// <summary>Builds a solution block: each item is a part with its item id, and the wires of the plan join their ports.</summary>
public static class BlockJoin
{
    public static string? Build(IReadOnlyList<SolutionItem> items, JoinPlan plan)
    {
        var parts = new JsonArray();
        foreach (var item in items)
        {
            if (JsonNode.Parse(item.Fragment) is not JsonObject block)
            {
                return null;
            }

            block["id"] = item.Id;
            parts.Add(block);
        }

        var wires = new JsonArray();
        foreach (var input in plan.Inputs)
        {
            wires.Add(new JsonArray(input, Dotted(input)));
        }

        foreach (var w in plan.Wires)
        {
            wires.Add(new JsonArray(w.From, w.To));
        }

        foreach (var output in plan.Outputs)
        {
            wires.Add(new JsonArray(Dotted(output), output));
        }

        return new JsonObject
        {
            ["p"] = Solution.Id,
            ["v"] = "plain",
            ["inputs"] = new JsonArray(plan.Inputs.Select(x => (JsonNode)x).ToArray()),
            ["outputs"] = new JsonArray(plan.Outputs.Select(x => (JsonNode)x).ToArray()),
            ["parts"] = parts,
            ["wires"] = wires,
        }.ToJsonString();
    }

    /// <summary>The typed ports of a block from its wires: an input has the type of the part port that it drives; an output has the type of its source.</summary>
    public static (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs)? Ports(BlockAdapter adapter, string fragment, PatternCatalog catalog)
    {
        if (adapter.Parse(fragment).Roots.FirstOrDefault() is not BlockNode n)
        {
            return null;
        }

        var types = BlockAdapter.PortTypes(n, catalog);
        string? Type(string port) => types.GetValueOrDefault(port);
        var inputs = n.DeclaredInputs.Select(x => (x, n.Wires.Where(w => w.From == x).Select(w => Type(w.To)).FirstOrDefault(t => t is not null)))
            .Where(p => p.Item2 is not null).Select(p => new Port(p.x, p.Item2!)).ToList();
        var outputs = n.DeclaredOutputs.Select(x => (x, n.Wires.Where(w => w.To == x).Select(w => Type(w.From)).FirstOrDefault(t => t is not null)))
            .Where(p => p.Item2 is not null).Select(p => new Port(p.x, p.Item2!)).ToList();
        return (inputs, outputs);
    }

    /// <summary>s1_request → s1.request: the outer port name back to the item port.</summary>
    private static string Dotted(string outer)
    {
        var i = outer.IndexOf('_', StringComparison.Ordinal);
        return i < 0 ? outer : $"{outer[..i]}.{outer[(i + 1)..]}";
    }
}
