using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Compose.Domain;

/// <summary>The verdict of the grader for one rubric. Ok null means that the grader gave no verdict.</summary>
public sealed record RubricVerdict(
    [property: JsonPropertyName("rubric")] string Rubric,
    [property: JsonPropertyName("ok")] bool? Ok,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>One set for one spec, inside an assignment. A null set means "none" for that spec.</summary>
public sealed record SpecSet(
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("set")] IReadOnlyList<string>? Set);

/// <summary>
/// One graded set. The remainder is each rubric that is not true.
/// An assignment grade has Parts (one set for each spec) and grades the rubrics of the whole anchor; Set is then all entries of the parts.
/// </summary>
public sealed record GradeRecord(
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("set")] IReadOnlyList<string> Set,
    [property: JsonPropertyName("verdicts")] IReadOnlyList<RubricVerdict> Verdicts,
    [property: JsonPropertyName("remainder")] IReadOnlyList<string> Remainder,
    [property: JsonPropertyName("cached")] bool Cached,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("parts")] IReadOnlyList<SpecSet>? Parts = null);

/// <summary>The final answer for one spec. A null set means "none".</summary>
public sealed record SetCommit(
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("set")] IReadOnlyList<string>? Set,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record TurnRecord(
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("refused")] IReadOnlyList<string> Refused,
    [property: JsonPropertyName("grades")] IReadOnlyList<GradeRecord> Grades,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>The trace of one compose run. Outcome: committed, no-commit or stopped.</summary>
public sealed record ComposeTrace(
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("commits")] IReadOnlyList<SetCommit> Commits,
    [property: JsonPropertyName("turns")] IReadOnlyList<TurnRecord> Turns,
    [property: JsonPropertyName("grades")] int Grades,
    [property: JsonPropertyName("cache_hits")] int CacheHits,
    [property: JsonPropertyName("calls")] int Calls,
    [property: JsonPropertyName("cost")] double Cost,
    [property: JsonPropertyName("ms")] long Ms)
{
    public IReadOnlyList<string>? CommitOf(string spec) => Commits.FirstOrDefault(c => c.Spec == spec)?.Set;
}

/// <summary>
/// The compose loop. The decider agent asks for grades of sets, then commits a set (or none) for each spec.
/// The grader agent grades each set in parallel. Code counts the limits, checks the ids, keeps the cache and makes the remainder.
/// Code never decides a rubric: a set over a budget or with an unavailable entry is the work of the agents.
/// </summary>
public sealed class ComposeLoop(IComposeTask task, ComposeSettings settings, AgentGateway gateway)
{
    public const string DeciderAgent = "composer-decider";

    public const string GraderAgent = "grader";

    private readonly Dictionary<string, GradeRecord> cache = new(StringComparer.Ordinal);
    private readonly List<GradeRecord> graded = [];
    private readonly List<TurnRecord> turns = [];
    private readonly List<string> notes = [];
    private int grades;
    private int cacheHits;

    public async Task<ComposeTrace> RunAsync(CancellationToken ct)
    {
        var limits = settings.Limits;
        for (var turn = 1; turn <= limits.Turns; turn++)
        {
            var answer = await gateway.CallAsync(DeciderAgent, DeciderInput(turn), ct);
            if (gateway.Stopped && !answer.Ok)
            {
                return Finish("stopped", gateway.StopReason, []);
            }

            if (!answer.Ok)
            {
                turns.Add(new TurnRecord(turn, "error", null, [], [], answer.Error));
                notes.Add($"Turn {turn}: your answer was not valid JSON for the schema.");
                continue;
            }

            var action = answer.Text("action") ?? string.Empty;
            var note = answer.Text("note");
            if (action == "commit")
            {
                var (commits, refused) = ReadCommits(answer);
                if (refused.Count == 0)
                {
                    turns.Add(new TurnRecord(turn, "commit", note, [], [], null));
                    return Finish("committed", null, commits);
                }

                turns.Add(new TurnRecord(turn, "commit", note, refused, [], null));
                notes.AddRange(refused.Select(r => $"Turn {turn}: commit refused: {r}"));
                continue;
            }

            if (action != "grade")
            {
                turns.Add(new TurnRecord(turn, action, note, [$"unknown action \"{action}\""], [], null));
                notes.Add($"Turn {turn}: the action must be grade or commit.");
                continue;
            }

            var (records, refusedGrades) = await GradeAsync(answer, ct);
            turns.Add(new TurnRecord(turn, "grade", note, refusedGrades, records, null));
            notes.AddRange(refusedGrades.Select(r => $"Turn {turn}: grade refused: {r}"));
            if (gateway.Stopped)
            {
                return Finish("stopped", gateway.StopReason, []);
            }
        }

        return Finish("no-commit", $"turn limit: {limits.Turns} turns with no valid commit", []);
    }

    /// <summary>The cache key: the spec and the sorted set.</summary>
    public static string Key(string spec, IEnumerable<string> set) => $"{spec}|{string.Join(",", set.Order(StringComparer.Ordinal))}";

    private ComposeTrace Finish(string outcome, string? stop, IReadOnlyList<SetCommit> commits)
    {
        var all = task.Specs.Select(s => commits.FirstOrDefault(c => c.Spec == s.Id) ?? new SetCommit(s.Id, null, "no commit")).ToList();
        return new ComposeTrace(task.Id, settings.Strategy, outcome, stop, all, turns, grades, cacheHits, gateway.Calls, gateway.Cost, gateway.ElapsedMs);
    }

    private (IReadOnlyList<SetCommit> Commits, List<string> Refused) ReadCommits(AgentAnswer answer)
    {
        var commits = new List<SetCommit>();
        var refused = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items(answer, "commit"))
        {
            var spec = Text(item, "spec");
            named.Add(spec);
            var set = item.TryGetProperty("set", out var s) && s.ValueKind == JsonValueKind.Array ? Ids(s) : null;
            var problem = SpecProblem(spec) ?? (set is null ? null : SetProblem(set));
            if (problem is not null)
            {
                refused.Add(problem);
                continue;
            }

            if (commits.Any(c => c.Spec == spec))
            {
                refused.Add($"spec \"{spec}\" has more than one commit");
                continue;
            }

            commits.Add(new SetCommit(spec, set, Text(item, "reason")));
        }

        refused.AddRange(task.Specs.Where(s => !named.Contains(s.Id)).Select(s => $"no commit for spec \"{s.Id}\"; commit a set or null for each spec"));
        return (commits, refused);
    }

    private async Task<(IReadOnlyList<GradeRecord> Records, List<string> Refused)> GradeAsync(AgentAnswer answer, CancellationToken ct)
    {
        var limits = settings.Limits;
        var refused = new List<string>();
        var accepted = new List<(string Key, Anchor Spec, IReadOnlyList<string> Set, IReadOnlyList<SpecSet>? Parts)>();
        var thisTurn = 0;

        void Accept(string key, Anchor spec, IReadOnlyList<string> set, IReadOnlyList<SpecSet>? parts, string label)
        {
            if (accepted.Any(a => a.Key == key))
            {
                return;
            }

            if (!(settings.Cache && cache.ContainsKey(key)))
            {
                if (thisTurn >= limits.GradesPerTurn)
                {
                    refused.Add($"{label}: limit of {limits.GradesPerTurn} grades in one turn");
                    return;
                }

                if (grades + thisTurn >= limits.Grades)
                {
                    refused.Add($"{label}: limit of {limits.Grades} grades in the run");
                    return;
                }

                thisTurn++;
            }

            accepted.Add((key, spec, set, parts));
        }

        foreach (var item in Items(answer, "grade"))
        {
            var spec = Text(item, "spec");
            var set = item.TryGetProperty("set", out var s) && s.ValueKind == JsonValueKind.Array ? Ids(s) : [];
            var problem = SpecProblem(spec) ?? SetProblem(set);
            if (problem is not null)
            {
                refused.Add(problem);
                continue;
            }

            Accept(Key(spec, set), task.Specs.First(x => x.Id == spec), set, null, $"[{string.Join(", ", set)}] for \"{spec}\"");
        }

        if (answer.Field("assignment") is { ValueKind: JsonValueKind.Array })
        {
            var (parts, problem) = ReadAssignment(answer);
            if (problem is not null)
            {
                refused.Add($"assignment: {problem}");
            }
            else
            {
                Accept(AssignmentKey(parts), task.Whole!, parts.SelectMany(p => p.Set ?? []).ToList(), parts, "assignment");
            }
        }

        if (accepted.Count == 0 && refused.Count == 0)
        {
            refused.Add("the grade action has no sets");
        }

        using var gate = new SemaphoreSlim(Math.Max(1, settings.Concurrency));
        var records = await Task.WhenAll(accepted.Select(async a =>
        {
            if (settings.Cache && cache.TryGetValue(a.Key, out var hit))
            {
                Interlocked.Increment(ref cacheHits);
                return hit with { Cached = true };
            }

            await gate.WaitAsync(ct);
            try
            {
                Interlocked.Increment(ref grades);
                return await GradeOneAsync(a.Spec, a.Set, a.Parts, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var (r, a) in records.Zip(accepted))
        {
            graded.Add(r);
            if (settings.Cache && !r.Cached && r.Error is null)
            {
                cache[a.Key] = r;
            }
        }

        return (records, refused);
    }

    /// <summary>The cache key of an assignment: each spec with its sorted set, in the order of the specs.</summary>
    public static string AssignmentKey(IEnumerable<SpecSet> parts) =>
        "assignment|" + string.Join(";", parts.Select(p => Key(p.Spec, p.Set ?? ["none"])));

    private (IReadOnlyList<SpecSet> Parts, string? Problem) ReadAssignment(AgentAnswer answer)
    {
        if (task.Whole is null)
        {
            return ([], "this task has no rubrics over all sets");
        }

        var given = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
        foreach (var item in Items(answer, "assignment"))
        {
            var spec = Text(item, "spec");
            var set = item.TryGetProperty("set", out var s) && s.ValueKind == JsonValueKind.Array ? Ids(s) : null;
            var problem = SpecProblem(spec) ?? (set is null ? null : SetProblem(set));
            if (problem is not null)
            {
                return ([], problem);
            }

            if (!given.TryAdd(spec, set))
            {
                return ([], $"spec \"{spec}\" is in the assignment more than once");
            }
        }

        var missing = task.Specs.Where(s => !given.ContainsKey(s.Id)).Select(s => s.Id).ToList();
        return missing.Count > 0
            ? ([], $"give one set (or null) for each spec; missing {string.Join(", ", missing)}")
            : (task.Specs.Select(s => new SpecSet(s.Id, given[s.Id])).ToList(), null);
    }

    private async Task<GradeRecord> GradeOneAsync(Anchor spec, IReadOnlyList<string> set, IReadOnlyList<SpecSet>? parts, CancellationToken ct)
    {
        var input = parts is null ? GraderInput(task, spec, set) : AssignmentInput(task, parts);
        var answer = await gateway.CallAsync(GraderAgent, input, ct);
        var verdicts = ReadVerdicts(answer, spec.Visible);
        return new GradeRecord(spec.Id, set, verdicts, verdicts.Where(v => v.Ok != true).Select(v => v.Rubric).ToList(), false, answer.Ok ? null : answer.Error, parts);
    }

    /// <summary>The input of one assignment grade: the pack rules, the whole anchor, each spec with its set, and the rubrics over all sets.</summary>
    public static string AssignmentInput(IComposeTask task, IReadOnlyList<SpecSet> parts)
    {
        var whole = task.Whole ?? throw new InvalidOperationException("the task has no whole anchor");
        var b = new StringBuilder();
        b.Append("# Rules\n\n").Append(task.GraderRules.Trim()).Append("\n\n# Spec\n\n").Append(whole.Context).Append("\n\n# Sets\n");
        foreach (var p in parts)
        {
            b.Append("\n## ").Append(p.Spec).Append("\n\n").Append(p.Set is null ? "none" : task.SetText(p.Set).Trim()).Append('\n');
        }

        b.Append("\n# Rubrics\n\n");
        foreach (var r in whole.Visible)
        {
            b.Append("- ").Append(r.Id).Append(": ").Append(r.Ask).Append('\n');
        }

        return b.ToString();
    }

    /// <summary>One verdict for each rubric, in the order of the rubrics. A rubric with no verdict in the answer gets Ok null.</summary>
    public static IReadOnlyList<RubricVerdict> ReadVerdicts(AgentAnswer answer, IReadOnlyList<Rubric> rubrics)
    {
        var given = new Dictionary<string, (bool Ok, string Reason)>(StringComparer.Ordinal);
        if (answer.Ok && answer.Field("verdicts") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var v in list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object))
            {
                var id = Text(v, "rubric").Trim();
                if (!rubrics.Any(r => r.Id == id) && id.Contains(':', StringComparison.Ordinal))
                {
                    id = id[..id.IndexOf(':', StringComparison.Ordinal)].Trim();
                }

                if (v.TryGetProperty("verdict", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    given.TryAdd(id, (ok.GetBoolean(), Text(v, "reason")));
                }
            }
        }

        return rubrics.Select(r => given.TryGetValue(r.Id, out var g) ? new RubricVerdict(r.Id, g.Ok, g.Reason) : new RubricVerdict(r.Id, null, "no verdict")).ToList();
    }

    private string? SpecProblem(string spec) =>
        task.Specs.Any(s => s.Id == spec) ? null : $"unknown spec \"{spec}\"";

    private string? SetProblem(IReadOnlyList<string> set)
    {
        if (set.Count == 0)
        {
            return "an empty set; use null in a commit for \"none\"";
        }

        var unknown = set.Where(id => !task.HasEntry(id)).ToList();
        if (unknown.Count > 0)
        {
            return $"unknown entry {string.Join(", ", unknown.Select(u => $"\"{u}\""))} in [{string.Join(", ", set)}]";
        }

        if (set.Distinct().Count() != set.Count)
        {
            return $"an entry is in [{string.Join(", ", set)}] more than once";
        }

        return set.Count > settings.Limits.SetSize ? $"[{string.Join(", ", set)}] has more than {settings.Limits.SetSize} entries" : null;
    }

    private string DeciderInput(int turn)
    {
        var limits = settings.Limits;
        var b = new StringBuilder();
        b.Append("# Strategy\n\n").Append(task.Strategy.Trim()).Append("\n\n# Specs\n");
        foreach (var spec in task.Specs)
        {
            b.Append("\n## ").Append(spec.Id).Append("\n\n").Append(spec.Context).Append('\n');
            foreach (var r in spec.Visible)
            {
                b.Append("- ").Append(r.Id).Append(": ").Append(r.Ask).Append('\n');
            }
        }

        if (task.Whole is { } whole)
        {
            b.Append("\n# Rubrics over all sets\n\n").Append(whole.Context).Append('\n');
            foreach (var r in whole.Visible)
            {
                b.Append("- ").Append(r.Id).Append(": ").Append(r.Ask).Append('\n');
            }

            b.Append("\nTo grade these rubrics, put one set (or null) for each spec in `assignment` in a grade action.\n");
        }

        b.Append("\n# Catalog\n\n").Append(task.Catalog.Trim()).Append("\n\n# Graded sets\n\n");
        if (graded.Count == 0)
        {
            b.Append("None yet.\n");
        }

        foreach (var g in graded.DistinctBy(g => g.Parts is null ? Key(g.Spec, g.Set) : AssignmentKey(g.Parts)))
        {
            var what = g.Parts is null
                ? $"- spec {g.Spec}, set [{string.Join(", ", g.Set)}]: "
                : $"- assignment [{string.Join(" | ", g.Parts.Select(p => $"{p.Spec}: {(p.Set is null ? "none" : string.Join(", ", p.Set))}"))}]: ";
            b.Append(what)
                .Append(string.Join("; ", g.Verdicts.Select(v => $"{v.Rubric} {(v.Ok is { } ok ? (ok ? "yes" : "no") : "no verdict")}")))
                .Append(". Remainder: ").Append(g.Remainder.Count == 0 ? "none" : string.Join(", ", g.Remainder)).Append('\n');
        }

        if (notes.Count > 0)
        {
            b.Append("\n# Notes\n\n");
            foreach (var n in notes)
            {
                b.Append("- ").Append(n).Append('\n');
            }
        }

        var left = limits.Turns - turn + 1;
        b.Append("\n# Limits\n\n")
            .Append($"- Turn {turn} of {limits.Turns}. {(left == 1 ? "This is the last turn: you must commit." : $"{left} turns left, this turn included.")}\n")
            .Append($"- Grades left in the run: {limits.Grades - grades}. Grades in one turn: {limits.GradesPerTurn}.\n")
            .Append($"- Entries in one set: {limits.SetSize} at most.\n");
        return b.ToString();
    }

    /// <summary>The input of one grader call: the pack rules, the spec context, the set and the visible rubrics.</summary>
    public static string GraderInput(IComposeTask task, Anchor spec, IReadOnlyList<string> set)
    {
        var b = new StringBuilder();
        b.Append("# Rules\n\n").Append(task.GraderRules.Trim()).Append("\n\n# Spec\n\n").Append(spec.Context).Append("\n\n# Set\n\n")
            .Append(task.SetText(set).Trim()).Append("\n\n# Rubrics\n\n");
        foreach (var r in spec.Visible)
        {
            b.Append("- ").Append(r.Id).Append(": ").Append(r.Ask).Append('\n');
        }

        return b.ToString();
    }

    private static IEnumerable<JsonElement> Items(AgentAnswer answer, string field) =>
        answer.Field(field) is { ValueKind: JsonValueKind.Array } list ? list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object) : [];

    private static string Text(JsonElement item, string field) =>
        item.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

    private static List<string> Ids(JsonElement list) =>
        list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).ToList();
}
