using PatternEngine.Domains.Blocks;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PatternEngine.Domains.Shapes;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;

namespace PatternEngine.Domains.Circuits;

/// <summary>The circuits pack: small logic circuits as JSON blocks with typed ports. A behavior grader checks the truth table.</summary>
public sealed class CircuitsPack : IDomainPack
{
    public string Name => "circuits";

    public string Folder => "Circuits";

    public string OutputFile => "output.json";

    public IOutputAdapter Adapter { get; } = new BlockAdapter("gate", false);

    public ILayout Layout { get; } = new CircuitsLayout();

    public IReferenceRenderer? Reference { get; } = new CircuitsReference();

    public IReadOnlyList<IMutationOperator> Mutations => CircuitsMutations.All;

    public IReviewRenderer? Review { get; } = new CircuitsReview();

    public IReadOnlyList<IGrader> Graders { get; } = [new BehaviorGrader()];
}

/// <summary>
/// Simulates the top block on each row of the truth table. The behavior comes from the case (expect.spec) or from the expected
/// pattern id. Any inner circuit with the right behavior passes.
/// </summary>
public sealed class BehaviorGrader : IGrader
{
    public string Name => "behavior";

    public Verdict Grade(GradeInput input)
    {
        var id = input.Case.Expect.Spec ?? (input.Case.Expect.Outcome == Outcomes.Match ? input.Case.Expect.Tree?.P : null);
        if (input.Case.Expect.Outcome == Outcomes.Ask)
        {
            return Verdict.Skip;
        }

        if (id is null || !BehaviorSpec.All.TryGetValue(id, out var spec))
        {
            return Verdict.From([$"the case has no expected behavior: give spec (one of {string.Join(", ", BehaviorSpec.All.Keys)})"]);
        }

        if (input.Parsed?.Roots.FirstOrDefault() is not BlockNode top)
        {
            return Verdict.From(["no circuit"]);
        }

        var failures = spec.Check(top, input.Catalog);
        return failures.Count <= 2 ? Verdict.From(failures) : Verdict.From([$"{failures.Count} rows of the truth table are wrong, e.g. {failures[0]}"]);
    }
}

/// <summary>Circuits have no geometry: the slot is empty and code does not assemble blocks.</summary>
public sealed class CircuitsLayout : ILayout
{
    private static readonly Slot Empty = new(new Dictionary<string, double>());

    public bool IsAssembled(string pattern) => false;

    public IReadOnlyList<Slot> Slots(string? pattern, int children) => Enumerable.Repeat(Empty, Math.Max(children, 1)).ToList();

    public string SlotText(Slot slot) => "Give the full block as one JSON object, with its parts and wires.";

    public string Assemble(string pattern, string? variant, IReadOnlyList<string> fragments, PatternCatalog catalog) => throw new InvalidOperationException("circuits are not assembled by code");

    public string Wrap(string fragment) => fragment;

    public string? Join(IReadOnlyList<SolutionItem> items, JoinPlan plan) => BlockJoin.Build(items, plan);

    public IReadOnlyList<string> CheckSlot(OutputNode? root, Slot slot) => root is null ? ["the fragment has no block"] : [];
}

/// <summary>The known-good circuit of each block: the textbook form.</summary>
public sealed class CircuitsReference : IReferenceRenderer
{
    public string? Render(TreeNode tree, PatternCatalog catalog) => Block(tree.P, null)?.ToJsonString();

    public static JsonObject? Block(string p, string? id)
    {
        JsonObject Gate(string i, string g) => new() { ["id"] = i, ["p"] = g };
        JsonArray Wires(params string[][] w) => new(w.Select(x => (JsonNode)new JsonArray(x[0], x[1])).ToArray());
        JsonObject Make(JsonNode[] parts, JsonArray wires)
        {
            var o = new JsonObject();
            if (id is not null)
            {
                o["id"] = id;
            }

            o["p"] = p;
            o["v"] = "plain";
            o["parts"] = new JsonArray(parts);
            o["wires"] = wires;
            return o;
        }

        return p switch
        {
            "half_adder" => Make([Gate("x1", "xor"), Gate("a1", "and")], Wires(["a", "x1.a"], ["b", "x1.b"], ["a", "a1.a"], ["b", "a1.b"], ["x1.y", "sum"], ["a1.y", "carry"])),
            "full_adder" => Make(
                [Block("half_adder", "h1")!, Block("half_adder", "h2")!, Gate("o1", "or")],
                Wires(["a", "h1.a"], ["b", "h1.b"], ["h1.sum", "h2.a"], ["cin", "h2.b"], ["h1.carry", "o1.a"], ["h2.carry", "o1.b"], ["h2.sum", "sum"], ["o1.y", "cout"])),
            "mux2" => Make(
                [Gate("n1", "not"), Gate("a1", "and"), Gate("a2", "and"), Gate("o1", "or")],
                Wires(["sel", "n1.a"], ["a", "a1.a"], ["n1.y", "a1.b"], ["b", "a2.a"], ["sel", "a2.b"], ["a1.y", "o1.a"], ["a2.y", "o1.b"], ["o1.y", "y"])),
            "equal" => Make([Gate("x1", "xor"), Gate("n1", "not")], Wires(["a", "x1.a"], ["b", "x1.b"], ["x1.y", "n1.a"], ["n1.y", "y"])),
            _ => null,
        };
    }
}

/// <summary>Operators that break a known-good circuit in one way.</summary>
public static class CircuitsMutations
{
    public static IReadOnlyList<IMutationOperator> All { get; } =
    [
        new MutationOperator("wrong-gate", ["behavior"], "Replace the first XOR or AND gate with an OR gate", i => Edit(i.Output, root =>
        {
            var gate = Gates(root).FirstOrDefault(g => g["p"]?.ToString() is "xor" or "and");
            if (gate is null)
            {
                return false;
            }

            gate["p"] = "or";
            return true;
        })),
        new MutationOperator("drop-wire", ["structure"], "Remove the last wire of the top block", i => Edit(i.Output, root =>
        {
            var wires = root["wires"] as JsonArray;
            if (wires is null || wires.Count == 0)
            {
                return false;
            }

            wires.RemoveAt(wires.Count - 1);
            return true;
        })),
        new MutationOperator("swap-outputs", ["behavior"], "Swap the two outputs of the top block", i => Edit(i.Output, root =>
        {
            var wires = (root["wires"] as JsonArray ?? []).OfType<JsonArray>().Where(w => !w[1]!.ToString().Contains('.')).ToList();
            if (wires.Count < 2)
            {
                return false;
            }

            var (a, b) = (wires[0][1]!.ToString(), wires[1][1]!.ToString());
            wires[0][1] = b;
            wires[1][1] = a;
            return true;
        })),
        new MutationOperator("extra-gate", ["structure"], "Add an AND gate with no wires", i => Edit(i.Output, root =>
        {
            (root["parts"] as JsonArray)?.Add(new JsonObject { ["id"] = "extra", ["p"] = "and" });
            return root["parts"] is JsonArray;
        })),
        new MutationOperator("unknown-gate", ["structure"], "Use a gate that the catalog does not have", i => Edit(i.Output, root =>
        {
            var gate = Gates(root).FirstOrDefault();
            if (gate is null)
            {
                return false;
            }

            gate["p"] = "nand";
            return true;
        })),
        new MutationOperator("loop", ["behavior"], "Wire the output of a gate back into its own input: a loop is allowed, but the truth table is wrong", i => Edit(i.Output, root =>
        {
            var wires = root["wires"] as JsonArray;
            var target = wires?.OfType<JsonArray>().FirstOrDefault(w => w[0]!.ToString().Contains('.') && w[1]!.ToString().Contains('.'));
            if (target is null)
            {
                return false;
            }

            var part = target[1]!.ToString().Split('.')[0];
            var output = Gates(root).FirstOrDefault(g => g["id"]?.ToString() == part) is null ? null : $"{part}.y";
            if (output is null)
            {
                return false;
            }

            target[0] = output;
            return true;
        })),
    ];

    private static IEnumerable<JsonObject> Gates(JsonObject root) =>
        (root["parts"] as JsonArray ?? []).OfType<JsonObject>().Where(p => p["parts"] is null);

    private static string? Edit(string output, Func<JsonObject, bool> change)
    {
        if (JsonNode.Parse(output) is not JsonObject root || !change(root))
        {
            return null;
        }

        return root.ToJsonString();
    }
}

public sealed class CircuitsReview : IReviewRenderer
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

    public string? RenderExpected(Case c, PatternCatalog catalog)
    {
        var id = c.Expect.Spec ?? c.Expect.Tree?.P;
        if (c.Expect.Outcome == Outcomes.NoFit)
        {
            return $"<p class=\"caption\">No block fits. The engine must start from the nearest block ({WebUtility.HtmlEncode(string.Join(" or ", c.Expect.Nearest ?? []))}) and propose a new one.</p>";
        }

        if (id is null || !BehaviorSpec.All.TryGetValue(id, out var spec))
        {
            return null;
        }

        var rows = Enumerable.Range(0, 1 << spec.Inputs.Count).Select(row =>
        {
            var bits = spec.Inputs.Select((n, i) => (n, v: (row >> (spec.Inputs.Count - 1 - i) & 1) == 1)).ToDictionary(x => x.n, x => x.v);
            var outs = spec.Function(bits);
            return "<tr>" + string.Concat(spec.Inputs.Select(n => $"<td>{(bits[n] ? 1 : 0)}</td>")) + string.Concat(spec.Outputs.Select(n => $"<td><b>{(outs[n] ? 1 : 0)}</b></td>")) + "</tr>";
        });
        return $"<table class=\"truth\"><tr>{string.Concat(spec.Inputs.Select(n => $"<th>{n}</th>"))}{string.Concat(spec.Outputs.Select(n => $"<th>{n}</th>"))}</tr>{string.Concat(rows)}</table>";
    }
}
