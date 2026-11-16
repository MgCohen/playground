using System.Text.Json;
using System.Text.Json.Nodes;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Blocks;

/// <summary>One block or leaf. Ports come from the pattern, or from the block itself for a new pattern.</summary>
public sealed class BlockNode(string pattern, string? variant, string? id) : OutputNode(pattern, variant)
{
    public string? Id { get; } = id;

    public List<(string From, string To)> Wires { get; } = [];

    public List<string> DeclaredInputs { get; } = [];

    public List<string> DeclaredOutputs { get; } = [];

    public IEnumerable<BlockNode> Parts => Children.OfType<BlockNode>();
}

/// <summary>
/// Reads JSON blocks: a pattern instance with parts and wires between typed ports. Code checks the wiring, so an agent cannot leave
/// a port open or drive a port two times (with merge set, an input can take more than one wire). A loop is allowed. With typed set, each wire must join two ports of the same type.
/// </summary>
public sealed class BlockAdapter(string primitive, bool typed, bool merge = false) : IOutputAdapter
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
        void Walk(BlockNode n, string where)
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

        foreach (var r in parsed.Roots.OfType<BlockNode>())
        {
            Walk(r, string.Empty);
        }

        return failures;
    }

    public IReadOnlyList<string> CheckLeaf(OutputNode node, Pattern leaf)
    {
        var n = (BlockNode)node;
        var messages = new List<string>();
        if (leaf.Primitive != primitive)
        {
            messages.Add($"a leaf of this domain is a {primitive}, got \"{leaf.Primitive}\"");
        }

        if (n.Wires.Count > 0 || n.Children.Count > 0)
        {
            messages.Add($"a {primitive} has no parts and no wires");
        }

        return messages;
    }

    /// <summary>The wiring check of one block, then the asserts of the pattern (none in this pack yet).</summary>
    public IReadOnlyList<AssertResult> Assert(OutputNode node, Pattern pattern, PatternCatalog catalog)
    {
        var problems = Wiring((BlockNode)node, catalog, typed, merge);
        return [new AssertResult("wiring", problems.Count == 0, string.Join("; ", problems.Take(3)))];
    }

    public string? ReadParam(OutputNode node, ParamDef param) => null;

    /// <summary>The type of each port that a wire can name in a block: its own ports and the ports of its parts. Unknown ports have no type.</summary>
    public static Dictionary<string, string> PortTypes(BlockNode n, PatternCatalog catalog)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        if (catalog.Get(n.Pattern) is { } own)
        {
            foreach (var port in own.Inputs.Concat(own.Outputs))
            {
                types[port.Name] = port.Type;
            }
        }

        foreach (var part in n.Parts.Where(p => p.Id is not null))
        {
            if (catalog.Get(part.Pattern) is { } p)
            {
                foreach (var port in p.Inputs.Concat(p.Outputs))
                {
                    types[$"{part.Id}.{port.Name}"] = port.Type;
                }
            }
        }

        return types;
    }

    public static IEnumerable<string> TypeErrors(BlockNode n, PatternCatalog catalog)
    {
        var types = PortTypes(n, catalog);
        foreach (var (from, to) in n.Wires)
        {
            if (types.TryGetValue(from, out var a) && types.TryGetValue(to, out var b) && a != b)
            {
                yield return $"wire {from} -> {to} joins a {a} to a {b}";
            }
        }
    }

    /// <summary>True if the wires between the parts of a block make a loop. A loop is allowed; this is information only.</summary>
    public static bool HasLoop(BlockNode n)
    {
        var edges = n.Wires.Where(w => w.From.Contains('.') && w.To.Contains('.'))
            .Select(w => (From: w.From.Split('.')[0], To: w.To.Split('.')[0])).ToList();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (state.GetValueOrDefault(id) == 1)
            {
                return true;
            }

            if (state.GetValueOrDefault(id) == 2)
            {
                return false;
            }

            state[id] = 1;
            if (edges.Where(e => e.From == id).Any(e => Visit(e.To)))
            {
                return true;
            }

            state[id] = 2;
            return false;
        }

        return n.Parts.Where(p => p.Id is not null).Any(p => Visit(p.Id!));
    }

    public static (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs) Ports(BlockNode n, PatternCatalog catalog)
    {
        var p = catalog.Get(n.Pattern);
        var inputs = p is { Inputs.Count: > 0 } ? p.Inputs.Select(x => x.Name).ToList() : n.DeclaredInputs;
        var outputs = p is { Outputs.Count: > 0 } ? p.Outputs.Select(x => x.Name).ToList() : n.DeclaredOutputs;
        return (inputs, outputs);
    }

    /// <summary>
    /// The typed outer ports of a block: from the catalog, or, for a block with no catalog ports (a solution, an assembled system),
    /// from its wires: an input has the type of the part port that it drives; an output has the type of its source.
    /// </summary>
    public static (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs) TypedPorts(BlockNode n, PatternCatalog catalog)
    {
        if (catalog.Get(n.Pattern) is { } p && p.Inputs.Count + p.Outputs.Count > 0)
        {
            return (p.Inputs, p.Outputs);
        }

        var types = PortTypes(n, catalog);
        var inputs = n.DeclaredInputs.Select(x => (Name: x, Type: n.Wires.Where(w => w.From == x).Select(w => types.GetValueOrDefault(w.To)).FirstOrDefault(t => t is not null)))
            .Where(x => x.Type is not null).Select(x => new Port(x.Name, x.Type!)).ToList();
        var outputs = n.DeclaredOutputs.Select(x => (Name: x, Type: n.Wires.Where(w => w.To == x).Select(w => types.GetValueOrDefault(w.From)).FirstOrDefault(t => t is not null)))
            .Where(x => x.Type is not null).Select(x => new Port(x.Name, x.Type!)).ToList();
        return (inputs, outputs);
    }

    public static IReadOnlyList<string> Wiring(BlockNode n, PatternCatalog catalog, bool typed = false, bool merge = false)
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
            if (count == 0 || (count > 1 && !(merge && !outputs.Contains(sink))))
            {
                problems.Add(count == 0 ? $"\"{sink}\" is not driven" : $"\"{sink}\" has {count} drivers");
            }
        }

        if (typed)
        {
            problems.AddRange(TypeErrors(n, catalog));
        }

        return problems;
    }

    private static BlockNode Read(JsonObject o, List<string> errors)
    {
        var p = o["p"]?.GetValue<string>() ?? string.Empty;
        if (p.Length == 0)
        {
            errors.Add("a block has no \"p\"");
        }

        var node = new BlockNode(p, o["v"]?.GetValue<string>(), o["id"]?.GetValue<string>());
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
