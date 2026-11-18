using PatternEngine.Domains.Topology;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Topology;

public sealed class PartCountTests
{
    [Test]
    public async Task A_child_over_the_part_count_of_its_parent_escalates()
    {
        var dir = Repo.Path("src", "PatternEngine", "Domains", "Topology");
        var settings = Settings.Load(Repo.Path("pe.settings.yaml"), Path.Combine(dir, "pack.settings.yaml"));
        var pack = new PackContext(new TopologyPack(), dir, settings, PatternCatalog.Load(Path.Combine(dir, "patterns")));
        var judge = new ScriptedJudge(call => call.Agent switch
        {
            "decider" when call.Input.StartsWith("Request: Design", StringComparison.Ordinal) => new { status = "solved", pattern = "cqrs_system", children = new[] { "read the orders", "an email for each order" }, reason = "two jobs" },
            "decider" => new { status = "solved", pattern = "query_handler", reason = "reads" },
            "escalation" => new { action = "decide", status = "solved", pattern = "observer", variant = "plain", reason = "an email is a reaction to an event" },
            "composer" => new { fragment = TopologyReference.Block(call.Input.Contains("Pattern: observer", StringComparison.Ordinal) ? "observer" : "query_handler", "plain")!.ToJsonString() },
            _ => null,
        });
        var work = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, work);
        var run = await EngineRun.RunAsync(pack, "Design a system to read orders and send an email for each order.", work, EngineSettings.From(settings) with { Decider = EngineSettings.StatusDecider }, judge, new NoUser(), CancellationToken.None);
        Assert.That(run.Trace.Tree!.ToString(), Is.EqualTo("cqrs_system(plain)[query_handler(plain), observer(plain)]"));
        Assert.That(run.Trace.Notes, Has.Some.Contains("allows at most"));
    }
}
