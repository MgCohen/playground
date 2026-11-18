using System.Text.Json;
using PatternEngine.Features.Compose.Domain;
using PatternEngine.Features.Compose.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Compose;

/// <summary>
/// The rules of the compose loop with a small task of three entries and no domain: code checks (R1), the loop stop (R3),
/// the cache key (G1), the anchor score (G3) and gaps (B4). No model.
/// </summary>
public sealed class ComposeRulesTests
{
    private const string SpecText = """
        ---
        id: s1
        kind: success
        level: case
        score: { rule: all }
        ---
        A set with a and b.

        - id: has-a
          ask: Does the set have a?
          check: grader
        - id: has-b
          ask: Does the set have b?
          check: code
          args: { entry: b }
        - id: secret
          ask: Is this hidden?
          check: grader
          visibility: eval
        """;

    private static readonly ComposeSettings Settings = new("test", new ComposeLimits(6, 4, 20, 3), true, 2, CodeChecks: true);

    [Test]
    public async Task Code_answers_the_code_rubrics_and_the_grader_gets_only_the_others()
    {
        var (trace, judge) = await RunAsync(Settings, Grade("a", "b"), Commit(["a", "b"]));
        var grade = trace.Turns[0].Grades.Single();
        Assert.That(grade.Verdicts.Select(v => v.Rubric), Is.EqualTo(new[] { "has-a", "has-b", "set:size" }));
        Assert.That(grade.Remainder, Is.Empty);
        var input = judge.Calls.Single(c => c.Agent == ComposeLoop.GraderAgent).Input;
        Assert.That(input, Does.Contain("has-a"));
        Assert.That(input, Does.Not.Contain("has-b"));
    }

    [Test]
    public async Task Eval_rubrics_never_go_to_the_run()
    {
        var (_, judge) = await RunAsync(Settings, Grade("a"), Commit(["a"]));
        Assert.That(judge.Calls.Select(c => c.Input), Has.All.Not.Contain("secret"));
    }

    [Test]
    public async Task With_code_checks_off_the_grader_answers_each_visible_rubric()
    {
        var (trace, judge) = await RunAsync(Settings with { CodeChecks = false }, Grade("a", "b"), Commit(["a", "b"]));
        Assert.That(judge.Calls.Single(c => c.Agent == ComposeLoop.GraderAgent).Input, Does.Contain("has-b"));
        Assert.That(trace.Turns[0].Grades.Single().Verdicts.Select(v => v.Rubric), Is.EqualTo(new[] { "has-a", "has-b" }));
    }

    [Test]
    public async Task A_set_check_that_fails_is_in_the_remainder()
    {
        var (trace, _) = await RunAsync(Settings, Grade("a", "b", "c"), Commit(["a", "b", "c"]));
        Assert.That(trace.Turns[0].Grades.Single().Remainder, Is.EqualTo(new[] { "set:size" }));
    }

    [Test]
    public async Task A_commit_of_a_set_that_fails_a_set_check_is_refused_and_gets_one_more_turn()
    {
        var (trace, _) = await RunAsync(Settings, Grade("a", "c"), Commit(["a", "b", "c"]), Commit(["a", "b"]));
        Assert.That(trace.Turns[1].Refused.Single(), Does.Contain("fails the set check set:size"));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(trace.CommitOf("s1"), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public async Task An_empty_remainder_stops_the_loop_and_the_next_turn_is_the_last()
    {
        var (trace, judge) = await RunAsync(Settings, Grade("a", "b"), Grade("c"));
        Assert.That(trace.Outcome, Is.EqualTo("no-commit"));
        Assert.That(trace.StopReason, Does.Contain("the remainder is empty"));
        Assert.That(trace.Turns, Has.Count.EqualTo(2));
        Assert.That(judge.Calls.Where(c => c.Agent == ComposeLoop.DeciderAgent).Last().Input, Does.Contain("This is the last turn: you must commit."));
    }

    [Test]
    public async Task A_remainder_that_does_not_become_smaller_stops_the_loop()
    {
        var (trace, _) = await RunAsync(Settings, Grade("a"), Grade("c"), Commit(["a"]));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(trace.Turns, Has.Count.EqualTo(3));
        Assert.That(trace.Turns[1].Grades.Single().Remainder, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_smaller_remainder_lets_the_loop_go_on()
    {
        var (trace, _) = await RunAsync(Settings, Grade("c"), Grade("a"), Grade("a", "b"), Commit(["a", "b"]));
        Assert.That(trace.Outcome, Is.EqualTo("committed"));
        Assert.That(trace.Turns, Has.Count.EqualTo(4));
    }

    [Test]
    public void The_cache_key_changes_with_each_of_its_four_parts()
    {
        var spec = Anchor.Parse(SpecText, "s1.md");
        var task = new SmallTask(spec);
        var loop = new ComposeLoop(task, Settings, Gateway(new ScriptedJudge(_ => null)));
        var key = loop.CacheKey(spec, "a");
        var otherRubric = spec with { Rubrics = [.. spec.Rubrics.Take(1).Select(r => r with { Ask = "Is a in the set?" }), .. spec.Rubrics.Skip(1)] };
        var hiddenChange = spec with { Rubrics = [.. spec.Rubrics.Take(2), spec.Rubrics[2] with { Ask = "changed" }] };
        Assert.That(loop.CacheKey(spec, "b"), Is.Not.EqualTo(key), "element");
        Assert.That(loop.CacheKey(otherRubric, "a"), Is.Not.EqualTo(key), "rubric");
        Assert.That(loop.CacheKey(spec with { Context = "other" }, "a"), Is.Not.EqualTo(key), "context");
        Assert.That(new ComposeLoop(task, Settings with { CodeChecks = false }, Gateway(new ScriptedJudge(_ => null))).CacheKey(spec, "a"), Is.Not.EqualTo(key), "checker");
        Assert.That(loop.CacheKey(hiddenChange, "a"), Is.EqualTo(key), "an eval rubric is not part of the grade");
    }

    [Test]
    public async Task A_new_grader_version_does_not_use_the_cache_of_the_old_one()
    {
        var spec = Anchor.Parse(SpecText, "s1.md");
        var one = new ComposeLoop(new SmallTask(spec), Settings, Gateway(new VersionJudge("v1")));
        var two = new ComposeLoop(new SmallTask(spec), Settings, Gateway(new VersionJudge("v2")));
        await one.RunAsync(CancellationToken.None);
        await two.RunAsync(CancellationToken.None);
        Assert.That(one.CacheKey(spec, "a"), Is.Not.EqualTo(two.CacheKey(spec, "a")));
    }

    [Test]
    public async Task The_trace_has_one_score_line_for_each_anchor()
    {
        var (trace, _) = await RunAsync(Settings, Grade("a"), Commit(["a"]));
        var score = trace.Scores!.Single();
        Assert.That((score.Ok, score.Total, score.Pass), Is.EqualTo((2, 3, false)));
        Assert.That(score.Failed, Is.EqualTo(new[] { "has-b" }));
        Assert.That(trace.ScoreLines.Single(), Does.StartWith("s1: score 2 of 3 (all, fail); failed: has-b; 2 turns, 1 grades"));
        Assert.That(trace.Commits.Single().Remainder, Is.EqualTo(new[] { "has-b" }));
    }

    [Test]
    public void A_percent_rule_passes_at_its_limit()
    {
        var rule = new ScoreRule("percent", 0.5);
        Assert.That((rule.Passes(1, 2), rule.Passes(1, 3)), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task A_gap_is_refused_when_gaps_are_off_and_kept_when_they_are_on()
    {
        var gap = new { action = "commit", commit = new[] { new { spec = "s1", set = new[] { "a" }, reason = "r", gap = new { new_id = "new-b", nearest = "a" } } }, note = "n" };
        var (off, _) = await RunAsync(Settings, Grade("a"), gap);
        Assert.That(off.Turns[1].Refused.Single(), Does.Contain("a gap is not allowed"));
        var (on, judge) = await RunAsync(Settings with { Gaps = true }, Grade("a"), gap);
        Assert.That(on.Outcome, Is.EqualTo("committed"));
        Assert.That(on.Commits.Single().Gap, Is.EqualTo(new Gap("new-b", "a")));
        Assert.That(judge.Calls.First(c => c.Agent == ComposeLoop.DeciderAgent).Input, Does.Contain("# Gaps"));
    }

    private static AgentGateway Gateway(IJudge judge) => new(judge, Path.GetTempPath(), new RunLimits(80, 1.0, 60));

    private static async Task<(ComposeTrace, ScriptedJudge)> RunAsync(ComposeSettings settings, params object[] turns)
    {
        var decider = new Queue<object>(turns);
        var judge = new ScriptedJudge(call => call.Agent == ComposeLoop.GraderAgent ? Grader(call) : decider.Count > 0 ? decider.Dequeue() : null);
        var trace = await new ComposeLoop(new SmallTask(Anchor.Parse(SpecText, "s1.md")), settings, Gateway(judge)).RunAsync(CancellationToken.None);
        return (trace, judge);
    }

    /// <summary>A grader that answers each listed rubric: has-x is true if the set lists entry x.</summary>
    private static object Grader(AgentCall call)
    {
        var set = call.Input[(call.Input.IndexOf("# Set", StringComparison.Ordinal) + 5)..call.Input.IndexOf("# Rubrics", StringComparison.Ordinal)];
        var rubrics = call.Input[call.Input.IndexOf("# Rubrics", StringComparison.Ordinal)..].Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..l.IndexOf(':')]);
        return new { verdicts = rubrics.Select(r => new { rubric = r, verdict = set.Contains($"- {r[4..]}", StringComparison.Ordinal), reason = "script" }).ToArray() };
    }

    private static object Grade(params string[] set) => new { action = "grade", grade = new[] { new { spec = "s1", set } }, note = "test" };

    private static object Commit(string[]? set) => new { action = "commit", commit = new[] { new { spec = "s1", set, reason = "test" } }, note = "test" };

    private sealed class SmallTask(Anchor spec) : IComposeTask
    {
        public string Id => "small";

        public IReadOnlyList<Anchor> Specs => [spec];

        public string Catalog => "- a\n- b\n- c";

        public Anchor? Whole => null;

        public string Strategy => "Test.";

        public string GraderRules => "Test rules.";

        public bool HasEntry(string id) => id is "a" or "b" or "c";

        public string SetText(IReadOnlyList<string> ids) => string.Join('\n', ids.Select(i => $"- {i}"));

        public IReadOnlyList<RubricVerdict> CodeVerdicts(Anchor anchor, IReadOnlyList<string> set) =>
        [
            .. anchor.Visible.Where(r => r.ByCode).Select(r => new RubricVerdict(r.Id, set.Contains(r.Args["entry"]!.ToString()!), "code")),
            new RubricVerdict("set:size", set.Count <= 2, "two entries at most"),
        ];
    }

    private sealed class VersionJudge(string version) : IJudge
    {
        public Task<AgentAnswer> CallAsync(AgentCall call, CancellationToken ct) =>
            Task.FromResult(new AgentAnswer(JsonSerializer.SerializeToElement(new { action = "commit", commit = new[] { new { spec = "s1", set = (string[]?)null, reason = "r" } }, note = "n" }), 0, 0, null));

        public string Version(string agent) => version;
    }
}
