using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PatternEngine.Domains.Blocks;
using PatternEngine.Domains.Shapes;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Topology;

/// <summary>
/// The software topology pack: design patterns as blocks with typed ports (Request, Command, Event, Query, View, Effect).
/// The flow grader sends a token into each input of the top block and checks that each part runs and each output gets a value.
/// </summary>
public sealed class TopologyPack : IDomainPack
{
    public string Name => "topology";

    public string Folder => "Topology";

    public string OutputFile => "output.json";

    public IOutputAdapter Adapter { get; } = new BlockAdapter("component", true);

    public ILayout Layout { get; } = new TopologyLayout();

    public IReferenceRenderer? Reference { get; } = new TopologyReference();

    public IReadOnlyList<IMutationOperator> Mutations => TopologyMutations.All;

    public IReviewRenderer? Review { get; } = new TopologyReview();

    public IReadOnlyList<IGrader> Graders { get; } = [new FlowGrader()];
}

/// <summary>
/// Runs the data flow of the top block: a token goes into each input; a part runs when each of its inputs has a token.
/// Each part must run and each output must get a token. It also checks the wiring and the port types of the top block,
/// so a new block of a no-fit run, which the structure grader does not know, is checked too.
/// </summary>
public sealed class FlowGrader : IGrader
{
    public string Name => "flow";

    public Verdict Grade(GradeInput input)
    {
        if (input.Case.Expect.Outcome == Outcomes.Ask && input.Parsed is null)
        {
            return Verdict.Skip;
        }

        if (input.Parsed?.Roots.FirstOrDefault() is not BlockNode top)
        {
            return Verdict.From(["no topology"]);
        }

        var failures = new List<string>(BlockAdapter.Wiring(top, input.Catalog, true));
        if (failures.Count == 0)
        {
            failures.AddRange(Flow(top, input.Catalog));
        }

        return Verdict.From(failures);
    }

    public static IReadOnlyList<string> Flow(BlockNode n, PatternCatalog catalog)
    {
        var (inputs, outputs) = BlockAdapter.Ports(n, catalog);
        var tokens = new HashSet<string>(inputs, StringComparer.Ordinal);
        var ran = new HashSet<string>(StringComparer.Ordinal);
        var parts = n.Parts.Where(p => p.Id is not null).ToList();
        for (var round = 0; round <= parts.Count + 1; round++)
        {
            foreach (var (from, to) in n.Wires.Where(w => tokens.Contains(w.From)))
            {
                tokens.Add(to);
            }

            foreach (var part in parts.Where(p => !ran.Contains(p.Id!)))
            {
                var (pi, po) = BlockAdapter.Ports(part, catalog);
                if (pi.All(x => tokens.Contains($"{part.Id}.{x}")) && (part.Children.Count == 0 || Flow(part, catalog).Count == 0))
                {
                    ran.Add(part.Id!);
                    foreach (var x in po)
                    {
                        tokens.Add($"{part.Id}.{x}");
                    }
                }
            }
        }

        return parts.Where(p => !ran.Contains(p.Id!)).Select(p => $"{n.Pattern}: the part \"{p.Id}\" never runs")
            .Concat(outputs.Where(o => !tokens.Contains(o)).Select(o => $"{n.Pattern}: the output \"{o}\" gets no value")).ToList();
    }
}

/// <summary>Code assembles a system from its parts: each open port of a part becomes a port of the system, and the events of the command pipeline go to each observer.</summary>
public sealed class TopologyLayout : ILayout
{
    private static readonly Slot Empty = new(new Dictionary<string, double>());

    public bool IsAssembled(string pattern) => pattern == "cqrs_system";

    public IReadOnlyList<Slot> Slots(string? pattern, int children) => Enumerable.Repeat(Empty, Math.Max(children, 1)).ToList();

    public string SlotText(Slot slot) => "Give the full block as one JSON object, with its parts and wires.";

    public string Wrap(string fragment) => fragment;

    public IReadOnlyList<string> CheckSlot(OutputNode? root, Slot slot) => root is null ? ["the fragment has no block"] : [];

    public string? Join(IReadOnlyList<SolutionItem> items, JoinPlan plan) => BlockJoin.Build(items, plan);

    public (IReadOnlyList<Port> Inputs, IReadOnlyList<Port> Outputs)? Ports(string fragment, PatternCatalog catalog) => BlockJoin.Ports(Adapter, fragment, catalog);

    private static readonly BlockAdapter Adapter = new("component", true);

    public string Assemble(string pattern, string? variant, IReadOnlyList<string> fragments)
    {
        var parts = new JsonArray();
        var wires = new JsonArray();
        var inputs = new JsonArray();
        var outputs = new JsonArray();
        var blocks = fragments.Select((f, i) => (Node: JsonNode.Parse(f) as JsonObject, Id: $"c{i + 1}")).Where(x => x.Node is not null).ToList();
        var pipeline = blocks.FirstOrDefault(b => b.Node!["p"]?.ToString() == "command_pipeline");
        foreach (var (node, id) in blocks)
        {
            node!["id"] = id;
            parts.Add(node);
            var p = node["p"]?.ToString();
            var (ins, outs) = p switch
            {
                "command_pipeline" => (["request"], ["event"]),
                "query_handler" => (new[] { "request" }, new[] { "view" }),
                "observer" => (["event"], ["effect"]),
                _ => (Names(node["inputs"]), Names(node["outputs"])),
            };
            foreach (var x in ins)
            {
                if (p == "observer" && x == "event" && pipeline.Node is not null)
                {
                    wires.Add(new JsonArray($"{pipeline.Id}.event", $"{id}.event"));
                    continue;
                }

                inputs.Add($"{id}_{x}");
                wires.Add(new JsonArray($"{id}_{x}", $"{id}.{x}"));
            }

            foreach (var x in outs)
            {
                outputs.Add($"{id}_{x}");
                wires.Add(new JsonArray($"{id}.{x}", $"{id}_{x}"));
            }
        }

        return new JsonObject { ["p"] = pattern, ["v"] = variant ?? "plain", ["inputs"] = inputs, ["outputs"] = outputs, ["parts"] = parts, ["wires"] = wires }.ToJsonString();
    }

    private static string[] Names(JsonNode? node) => (node as JsonArray ?? []).Select(x => x is JsonObject o ? o["name"]?.ToString() : x?.ToString()).OfType<string>().ToArray();
}

/// <summary>The known-good topology of each pattern.</summary>
public sealed class TopologyReference : IReferenceRenderer
{
    public string? Render(TreeNode tree, PatternCatalog catalog)
    {
        if (tree.P == "cqrs_system")
        {
            var kids = (tree.Parts ?? []).Select(k => Block(k.P, k.V)?.ToJsonString()).OfType<string>().ToList();
            return new TopologyLayout().Assemble("cqrs_system", tree.V, kids);
        }

        return Block(tree.P, tree.V)?.ToJsonString();
    }

    public static JsonObject? Block(string p, string? v)
    {
        JsonObject Leaf(string id, string pattern) => new() { ["id"] = id, ["p"] = pattern };
        JsonObject Make(string variant, JsonNode[] parts, params string[][] wires) => new()
        {
            ["p"] = p,
            ["v"] = variant,
            ["parts"] = new JsonArray(parts),
            ["wires"] = new JsonArray(wires.Select(w => (JsonNode)new JsonArray(w[0], w[1])).ToArray()),
        };

        return p switch
        {
            "command_pipeline" when v == "validated" => Make("validated",
                [Leaf("in", "endpoint"), Leaf("check", "validator"), Leaf("run", "handler"), Leaf("save", "event_store")],
                ["request", "in.request"], ["in.command", "check.command"], ["check.command", "run.command"], ["run.event", "save.event"], ["save.event", "event"]),
            "command_pipeline" => Make("plain",
                [Leaf("in", "endpoint"), Leaf("run", "handler"), Leaf("save", "event_store")],
                ["request", "in.request"], ["in.command", "run.command"], ["run.event", "save.event"], ["save.event", "event"]),
            "query_handler" => Make("plain", [Leaf("in", "query_endpoint"), Leaf("read", "reader")], ["request", "in.request"], ["in.query", "read.query"], ["read.view", "view"]),
            "observer" => Make("plain", [Leaf("s1", "subscriber")], ["event", "s1.event"], ["s1.effect", "effect"]),
            _ => null,
        };
    }
}

/// <summary>Operators that break a known-good topology in one way.</summary>
public static class TopologyMutations
{
    public static IReadOnlyList<IMutationOperator> All { get; } =
    [
        new MutationOperator("drop-wire", ["structure"], "Remove the last wire of the first block", i => Edit(i.Output, b =>
        {
            var wires = b["wires"] as JsonArray;
            if (wires is not { Count: > 0 })
            {
                return false;
            }

            wires.RemoveAt(wires.Count - 1);
            return true;
        })),
        new MutationOperator("type-mismatch", ["structure"], "Drive the output of the first block from its input", i => Edit(i.Output, b =>
        {
            var wires = (b["wires"] as JsonArray ?? []).OfType<JsonArray>().ToList();
            var into = wires.FirstOrDefault(w => !w[1]!.ToString().Contains('.'));
            var from = wires.FirstOrDefault(w => !w[0]!.ToString().Contains('.'));
            if (into is null || from is null)
            {
                return false;
            }

            into[0] = from[0]!.ToString();
            return true;
        })),
        new MutationOperator("remove-part", ["structure"], "Remove the last part of the first block and its wires", i => Edit(i.Output, b =>
        {
            var parts = b["parts"] as JsonArray;
            if (parts is not { Count: > 1 })
            {
                return false;
            }

            var id = parts[^1]!["id"]!.ToString();
            parts.RemoveAt(parts.Count - 1);
            var wires = (JsonArray)b["wires"]!;
            foreach (var w in wires.OfType<JsonArray>().Where(w => w[0]!.ToString().StartsWith(id + ".", StringComparison.Ordinal) || w[1]!.ToString().StartsWith(id + ".", StringComparison.Ordinal)).ToList())
            {
                wires.Remove(w);
            }

            return true;
        })),
        new MutationOperator("wrong-component", ["structure"], "Replace the first handler with a reader", i => Edit(i.Output, b =>
        {
            var part = (b["parts"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(p => p["p"]?.ToString() == "handler");
            if (part is null)
            {
                return false;
            }

            part["p"] = "reader";
            return true;
        })),
    ];

    /// <summary>Changes the first block: the top block, or the first part of a system.</summary>
    private static string? Edit(string output, Func<JsonObject, bool> change)
    {
        if (JsonNode.Parse(output) is not JsonObject root)
        {
            return null;
        }

        var target = root["p"]?.ToString() == "cqrs_system" ? (root["parts"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() : root;
        return target is not null && change(target) ? root.ToJsonString() : null;
    }
}

public sealed class TopologyReview : IReviewRenderer
{
    public string RenderOutput(string output)
    {
        string pretty;
        try
        {
            pretty = JsonNode.Parse(output)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            pretty = output;
        }

        return $"<pre class=\"code\">{WebUtility.HtmlEncode(pretty)}</pre>";
    }

    public string? RenderExpected(Case c, PatternCatalog catalog) => c.Expect.Outcome == Outcomes.NoFit
        ? $"<p class=\"caption\">No pattern fits. The engine must start from the nearest pattern ({WebUtility.HtmlEncode(string.Join(" or ", c.Expect.Nearest ?? []))}) and propose a new one.</p>"
        : c.Expect.Tree is null ? null : new TopologyReview().RenderOutput(new TopologyReference().Render(c.Expect.Tree, catalog) ?? "{}");
}
