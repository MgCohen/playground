using Orchestrator.Core.Contract;
using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class ContractTests
{
    [Test]
    public void A_decision_of_the_plugin_reads_with_each_template_field()
    {
        using var t = new Rig();
        var id = t.Create("start");
        Files.Write(t.Store.PathOf(id, StoreFiles.Decisions, "toolu_1.json"), """
            { "id": "toolu_1", "session": "s", "kind": "ask_user", "question": "Q?", "context": "C", "snippet": "x = 1",
              "options": ["A", "B"], "recommendation": "A: simple", "risk": "medium", "defaultOption": "A",
              "mode": "pending", "waitSeconds": 0, "asked": "2026-11-16T10:00:00.000Z" }
            """);
        var d = t.Store.Decision(id, "toolu_1")!;
        Assert.That((d.Context, d.Snippet, d.Recommendation, d.DefaultOption), Is.EqualTo(("C", "x = 1", "A: simple", "A")));
        Assert.That(t.Store.OpenDecisions(id).Single().Options, Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public void A_report_of_the_plugin_reads_with_its_event_id()
    {
        using var t = new Rig();
        var id = t.Create("start");
        Files.Write(t.Store.PathOf(id, StoreFiles.Report), """
            { "state": "done", "summary": "S", "done": ["a"], "links": ["b"], "at": "2026-11-16T10:00:00.000Z", "event": "1794-abc-supervisor" }
            """);
        Assert.That(t.Store.Report(id), Is.EqualTo(new Report("done", "S", "2026-11-16T10:00:00.000Z") { Event = "1794-abc-supervisor" }));
    }

    [Test]
    public void An_old_text_inbox_file_and_a_json_inbox_file_both_read()
    {
        using var t = new Rig();
        var id = t.Create("one");
        Files.Write(t.Store.PathOf(id, StoreFiles.Inbox, "2.txt"), "two");
        Files.Write(t.Store.PathOf(id, StoreFiles.Inbox, "3.json"), """{ "text": "three", "from": "session:p" }""");
        var pending = t.Store.Pending(id);
        Assert.That(pending.Text, Is.EqualTo("one\n\n---\n\ntwo\n\n---\n\nthree"));
        Assert.That(pending.Senders, Is.EquivalentTo(new[] { "user", "session:p" }));
    }

    [Test]
    public void A_lease_of_the_plugin_gives_the_cwd_of_an_app_session()
    {
        using var t = new Rig();
        Files.Write(t.Store.PathOf("app", StoreFiles.Lease), """
            { "holder": "live", "since": "2026-11-16T10:00:00.000Z", "reason": { "event": "session.start", "start": { "cwd": "C:/repo" } } }
            """);
        Assert.That(t.Store.Cwd("app"), Is.EqualTo("C:/repo"));
    }

    [Test]
    public void Each_plugin_tool_reads_its_input_schema_from_a_template()
    {
        var tools = File.ReadAllText(Path.Combine(Home.Root(), "plugin", "hooks", "tools.ts"));
        foreach (var kind in new[] { "decision", "report", "update", "brief" })
            Assert.That(tools, Does.Contain($"templateSchema($, '{kind}')"), kind);
    }
}
