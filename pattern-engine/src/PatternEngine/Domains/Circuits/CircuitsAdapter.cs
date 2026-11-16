using System.Text.Json;
using System.Text.Json.Nodes;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Circuits;

/// <summary>One block or gate of a circuit. Ports come from the pattern, or from the block itself for a new pattern.</summary>
public sealed class CircuitNode(string pattern, string? variant, string? id) : OutputNode(pattern, variant)
{
    public string? Id { get; } = id;

    public List<(string From, string To)> Wires { get; } = [];

    public List<string> DeclaredInputs { get; } = [];

    public List<string> DeclaredOutputs { get; } = [];

    public IEnumerable<CircuitNode> Parts => Children.OfType<CircuitNode>();
}

/// <summary>Reads circuits written as JSON blocks. Code checks the wiring, so an agent cannot leave a port open or make a loop.</summary>
public sealed class CircuitsAdapter : IOutputAdapter
{
    public ParsedOutput Parse(string text)
    {
        var errors = new List<string>();
        try
        {
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is not JsonObject root)
            {
                return new ParsedOutput([], ["the circuit must be one JSON object"]);
            }

            return new ParsedOutput([Read(root, errors)], errors);
        }
        catch (JsonException e)
        {
            return new ParsedOutput([], [$"not valid JSON: {e.Message}"]);
        }
    }

    public IReadOnlyList<string> Lint(ParsedOutput parsed)
    {
        var failures = new List<string>();
        void Walk(CircuitNode n, string where)
        {
            var here = where.Length > 0 ? $"{where} > {n.Id ?? n.Pattern}" : n.Pattern;
            foreach (var dup in n.Parts.GroupBy(p => p.Id).Where(g => g.Key is null || g.Count() > 1))
            {
                failures.Add(dup.Key is null ? $"{here}: a part has no id" : $"{here}: the id \"{dup.Key}\" is used two times");
            }

            foreach (var p in n.Parts)
            {
                Walk(p, here);
            }
        }

        foreach (var r in parsed.Roots.OfType<CircuitNode>())
        {
            Walk(r, string.Empty);
        }

        return failures;
    }

    public IReadOnlyList<string> CheckLeaf(OutputNode node, Pattern leaf)
    {
        var n = (CircuitNode)node;
        var messages = new List<string>();
        if (leaf.Primitive != "gate")
        {
            messages.Add($"a leaf of this domain is a gate, got \"{leaf.Primitive}\"");
        }

        if (n.Wires.Count > 0 || n.Children.Count > 0)
        {
            messages.Add("a gate has no parts and no wires");
        }

        return messages;
    }

    /// <summary>The wiring check of one block, then the asserts of the pattern (none in this pack yet).</summary>
    public IReadOnlyList<AssertResult> Assert(OutputNode node, Pattern pattern, PatternCatalog catalog)
    {
        var problems = Wiring((CircuitNode)node, catalog);
        return [new AssertResult("wiring", problems.Count == 0, string.Join("; ", problems.Take(3)))];
    }

    public string? ReadParam(OutputNode node, ParamDef param) => null;

    public static (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs) Ports(CircuitNode n, PatternCatalog catalog)
    {
        var p = catalog.Get(n.Pattern);
        var inputs = p is { Inputs.Count: > 0 } ? p.Inputs.Select(x => x.Name).ToList() : n.DeclaredInputs;
        var outputs = p is { Outputs.Count: > 0 } ? p.Outputs.Select(x => x.Name).ToList() : n.DeclaredOutputs;
        return (inputs, outputs);
    }

    public static IReadOnlyList<string> Wiring(CircuitNode n, PatternCatalog catalog)
    {
        var problems = new List<string>();
        var (inputs, outputs) = Ports(n, catalog);
        var parts = n.Parts.Where(p => p.Id is not null).GroupBy(p => p.Id!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sources = new HashSet<string>(inputs, StringComparer.Ordinal);
        var sinks = new HashSet<string>(outputs, StringComparer.Ordinal);
        foreach (var (id, part) in parts)
        {
            var (pi, po) = Ports(part, catalog);
            foreach (var x in po)
            {
                sources.Add($"{id}.{x}");
            }

            foreach (var x in pi)
            {
                sinks.Add($"{id}.{x}");
            }
        }

        var driven = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (from, to) in n.Wires)
        {
            if (!sources.Contains(from))
            {
                problems.Add($"wire from \"{from}\": no such input or part output");
            }

            if (!sinks.Contains(to))
            {
                problems.Add($"wire to \"{to}\": no such part input or output");
            }

            driven[to] = driven.GetValueOrDefault(to) + 1;
        }

        foreach (var sink in sinks)
        {
            var count = driven.GetValueOrDefault(sink);
            if (count != 1)
            {
                problems.Add(count == 0 ? $"\"{sink}\" is not driven" : $"\"{sink}\" has {count} drivers");
            }
        }

        if (problems.Count == 0 && Simulator.HasLoop(n))
        {
            problems.Add("the wires make a loop");
        }

        return problems;
    }

    private static CircuitNode Read(JsonObject o, List<string> errors)
    {
        var p = o["p"]?.GetValue<string>() ?? string.Empty;
        if (p.Length == 0)
        {
            errors.Add("a block has no \"p\"");
        }

        var node = new CircuitNode(p, o["v"]?.GetValue<string>(), o["id"]?.GetValue<string>());
        node.DeclaredInputs.AddRange(Names(o["inputs"]));
        node.DeclaredOutputs.AddRange(Names(o["outputs"]));
        foreach (var part in (o["parts"] as JsonArray ?? []).OfType<JsonObject>())
        {
            node.Children.Add(Read(part, errors));
        }

        foreach (var wire in (o["wires"] as JsonArray ?? []).OfType<JsonArray>())
        {
            if (wire.Count == 2 && wire[0] is JsonValue a && wire[1] is JsonValue b)
            {
                node.Wires.Add((a.ToString(), b.ToString()));
            }
            else
            {
                errors.Add($"{p}: a wire must be [from, to]");
            }
        }

        return node;
    }

    private static IEnumerable<string> Names(JsonNode? node) => (node as JsonArray ?? []).Select(x => x is JsonObject obj ? obj["name"]?.ToString() : x?.ToString()).OfType<string>();
}
