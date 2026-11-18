using PatternEngine.Domains.Party;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Tests.Party;

public sealed class PartyRunnerTests
{
    [Test]
    public async Task The_grader_check_with_the_oracle_grader_is_all_true()
    {
        var judge = new ScriptedJudge(PartyScript.OracleGrader);
        var runner = new PartyRunner(PartyScript.Pack, () => judge, new RunLimits(80, 1.0, 60), Path.Combine(Path.GetTempPath(), "pe-tests"));
        var result = await runner.GraderCheckAsync(new PartyRunOptions("dev", "haiku", 1, [], "greedy", "test"), _ => { }, CancellationToken.None);
        Assert.That(result.GraderCheck, Has.Count.EqualTo(20));
        Assert.That(result.GraderCheck.SelectMany(l => l.Wrong), Is.Empty);
        Assert.That(result.GraderCheck.Count(l => l.Wrong.Count == 0), Is.EqualTo(20));
        Assert.That(result.Report, Does.StartWith("Grader accuracy: "));
    }

    [TestCase("composer-decider")]
    [TestCase("grader")]
    public void The_compose_agent_files_load(string name)
    {
        var agent = new AgentCatalog([Support.Repo.Path("agents")]).Get(name);
        Assert.That(agent.Tools, Does.Contain(AgentFile.StructuredOutputTool));
    }

    [Test]
    public void A_verdict_with_the_full_rubric_line_as_its_id_counts()
    {
        var rubrics = PartyScript.Pack.Case("c1").Quests[0].Visible;
        var data = System.Text.Json.JsonSerializer.SerializeToElement(new { verdicts = new[] { new { rubric = "healer: Does the party have a healer?", verdict = true, reason = "x" } } });
        var verdicts = Features.Compose.Domain.ComposeLoop.ReadVerdicts(new AgentAnswer(data, 0, 0, null), rubrics);
        Assert.That(verdicts[0].Ok, Is.True);
        Assert.That(verdicts[1].Ok, Is.Null);
    }

    [Test]
    public async Task A_run_grades_each_case_with_the_oracle()
    {
        var turns = new Queue<object>([
            new { action = "grade", grade = new[] { new { spec = "c1", set = new[] { "paladin" } } }, note = "one hero" },
            new { action = "commit", commit = new[] { new { spec = "c1", set = new[] { "paladin" }, reason = "all yes" } }, note = "done" },
        ]);
        var judge = new ScriptedJudge(call => call.Agent == "grader" ? PartyScript.OracleGrader(call) : turns.Dequeue());
        var runner = new PartyRunner(PartyScript.Pack, () => judge, new RunLimits(80, 1.0, 60), Path.Combine(Path.GetTempPath(), "pe-tests"));
        var result = await runner.RunAsync(new PartyRunOptions("dev", "haiku", 1, ["c1"], "greedy", "test"), _ => { }, CancellationToken.None);
        var run = result.Runs.Single();
        Assert.That(run.Pass, Is.True);
        Assert.That(run.GraderAccuracy, Is.EqualTo("2 of 2"));
        Assert.That(run.Verdicts.Select(v => v.Rubric), Is.EqualTo(new[] { "valid", "smallest" }));
        Assert.That(result.Report, Does.StartWith("Pass: 1 of 1. smallest: 1 of 1."));
    }
}
