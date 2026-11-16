using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Domains.Circuits;

/// <summary>Runs a circuit on input bits. A block is evaluated from its wires; a gate computes its output.</summary>
public static class Simulator
{
    public static IReadOnlyDictionary<string, bool> Run(CircuitNode n, PatternCatalog catalog, IReadOnlyDictionary<string, bool> inputs)
    {
        if (n.Children.Count == 0 && n.Wires.Count == 0)
        {
            return Gate(n.Pattern, inputs);
        }

        var values = new Dictionary<string, bool>(inputs, StringComparer.Ordinal);
        var parts = n.Parts.Where(p => p.Id is not null).ToList();
        var done = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round <= parts.Count; round++)
        {
            Propagate(n, values);
            foreach (var part in parts.Where(p => !done.Contains(p.Id!)))
            {
                var (pi, _) = CircuitsAdapter.Ports(part, catalog);
                if (pi.All(x => values.ContainsKey($"{part.Id}.{x}")))
                {
                    var outputs = Run(part, catalog, pi.ToDictionary(x => x, x => values[$"{part.Id}.{x}"], StringComparer.Ordinal));
                    foreach (var (k, v) in outputs)
                    {
                        values[$"{part.Id}.{k}"] = v;
                    }

                    done.Add(part.Id!);
                }
            }
        }

        Propagate(n, values);
        var (_, outs) = CircuitsAdapter.Ports(n, catalog);
        var missing = outs.Where(o => !values.ContainsKey(o)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"{n.Pattern}: the output {string.Join(", ", missing)} has no value");
        }

        return outs.ToDictionary(o => o, o => values[o], StringComparer.Ordinal);
    }

    public static bool HasLoop(CircuitNode n)
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

    private static void Propagate(CircuitNode n, Dictionary<string, bool> values)
    {
        foreach (var (from, to) in n.Wires)
        {
            if (values.TryGetValue(from, out var v))
            {
                values[to] = v;
            }
        }
    }

    private static Dictionary<string, bool> Gate(string gate, IReadOnlyDictionary<string, bool> i)
    {
        bool A() => i["a"];
        bool B() => i["b"];
        var y = gate switch
        {
            "and" => A() && B(),
            "or" => A() || B(),
            "xor" => A() ^ B(),
            "not" => !A(),
            _ => throw new InvalidOperationException($"unknown gate \"{gate}\""),
        };
        return new Dictionary<string, bool> { ["y"] = y };
    }
}

/// <summary>A behavior: input and output names and the function. A case names one, or the pack takes it from the pattern id.</summary>
public sealed record BehaviorSpec(string Id, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, Func<IReadOnlyDictionary<string, bool>, IReadOnlyDictionary<string, bool>> Function)
{
    public static IReadOnlyDictionary<string, BehaviorSpec> All { get; } = new[]
    {
        new BehaviorSpec("half_adder", ["a", "b"], ["sum", "carry"], i => new Dictionary<string, bool> { ["sum"] = i["a"] ^ i["b"], ["carry"] = i["a"] && i["b"] }),
        new BehaviorSpec("full_adder", ["a", "b", "cin"], ["sum", "cout"], i =>
        {
            var n = (i["a"] ? 1 : 0) + (i["b"] ? 1 : 0) + (i["cin"] ? 1 : 0);
            return new Dictionary<string, bool> { ["sum"] = n % 2 == 1, ["cout"] = n >= 2 };
        }),
        new BehaviorSpec("mux2", ["a", "b", "sel"], ["y"], i => new Dictionary<string, bool> { ["y"] = i["sel"] ? i["b"] : i["a"] }),
        new BehaviorSpec("equal", ["a", "b"], ["y"], i => new Dictionary<string, bool> { ["y"] = i["a"] == i["b"] }),
        new BehaviorSpec("majority", ["a", "b", "c"], ["y"], i => new Dictionary<string, bool> { ["y"] = (i["a"] ? 1 : 0) + (i["b"] ? 1 : 0) + (i["c"] ? 1 : 0) >= 2 }),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>The failures of a circuit against this behavior, for each row of the truth table.</summary>
    public IReadOnlyList<string> Check(CircuitNode top, PatternCatalog catalog)
    {
        var (inputs, outputs) = CircuitsAdapter.Ports(top, catalog);
        var missing = Inputs.Except(inputs).Concat(Outputs.Except(outputs)).ToList();
        if (missing.Count > 0)
        {
            return [$"the circuit has no port {string.Join(", ", missing)}"];
        }

        var failures = new List<string>();
        for (var row = 0; row < 1 << Inputs.Count; row++)
        {
            var bits = Inputs.Select((name, i) => (name, value: (row >> (Inputs.Count - 1 - i) & 1) == 1)).ToDictionary(x => x.name, x => x.value, StringComparer.Ordinal);
            IReadOnlyDictionary<string, bool> got;
            try
            {
                got = Simulator.Run(top, catalog, bits);
            }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException)
            {
                return [$"cannot simulate: {e.Message}"];
            }

            var want = Function(bits);
            foreach (var o in Outputs.Where(o => got[o] != want[o]))
            {
                failures.Add($"for {string.Join(" ", bits.Select(b => $"{b.Key}={(b.Value ? 1 : 0)}"))}: {o} is {(got[o] ? 1 : 0)}, expected {(want[o] ? 1 : 0)}");
            }
        }

        return failures;
    }
}
