using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>The typed query of the stream, its named queries, its text form and its HTTP routes.</summary>
[NonParallelizable]
public sealed class QueryTests
{
    static (WebApplicationFactory<Program> Factory, string Store) Start()
    {
        var store = Directory.CreateTempSubdirectory("orch-query-").FullName;
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;
        Environment.SetEnvironmentVariable("ORCH_STORE", null);
        return (factory, store);
    }

    static StreamQuery Parse(FileStore store, params (string Key, string? Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value);
        return StreamQueryText.Parse(key => map.TryGetValue(key, out var v) ? (true, v) : (false, null), store.Clock.GetUtcNow());
    }

    [Test]
    public void The_inbox_query_gives_the_open_cards_and_the_newest_of_each_thread()
    {
        using var t = new Rig();
        var id = t.Create("start", "child");
        var open = t.Store.SaveDecision(new Decision("toolu_1", id) { Question = "Q1" });
        t.Store.SaveDecision(new Decision("toolu_2", id) { Question = "Q2" });
        t.Store.SaveAnswer(id, "toolu_2", new Answer { Value = "A", By = Parties.User });
        var report = t.Store.SaveReport(id, ReportStates.Done, "all done");
        var cards = t.Store.Query(StreamQueries.Inbox).Select(e => e.Id).ToList();
        Assert.That(cards, Is.EqualTo(new[] { open.Id, report.Id }));
        Assert.That(new Inbox(t.Store).Cards().Select(c => c.Envelope.Id), Is.EqualTo(cards));
    }

    [Test]
    public void The_open_decisions_query_skips_answers_and_forwards()
    {
        using var t = new Rig();
        var parent = t.Create("start", "parent");
        var child = t.Create("start", "child", parent);
        var asked = t.Store.SaveDecision(new Decision("toolu_1", child) { Question = "Q1" });
        t.Store.Events.Forward(asked, Parties.Session(parent));
        t.Store.SaveDecision(new Decision("toolu_2", child) { Question = "Q2" });
        t.Store.SaveAnswer(child, "toolu_2", new Answer { Value = "A", By = Parties.User });
        Assert.That(t.Store.Query(StreamQueries.OpenDecisions()).Select(e => e.Id), Is.EqualTo(new[] { asked.Id }));
        Assert.That(t.Store.OpenDecisions().Select(d => d.Id), Is.EqualTo(new[] { "toolu_1" }));
        Assert.That(t.Store.Query(StreamQueries.OpenDecisions(parent)), Is.Empty);
    }

    [Test]
    public void The_timeline_query_gives_the_newest_first_up_to_the_limit()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var sent = Enumerable.Range(0, 5).Select(i => t.Store.Send(id, $"m{i}")).ToList();
        var timeline = t.Store.Query(StreamQueries.Timeline(new StreamQuery { Kinds = new HashSet<string> { EventKinds.Message } }, 3));
        Assert.That(timeline.Select(e => e.Id), Is.EqualTo(sent.AsEnumerable().Reverse().Take(3).Select(e => e.Id)));
    }

    [Test]
    public void The_filters_party_needs_until_and_forwards_match()
    {
        using var t = new Rig();
        var a = t.Create("start", "a");
        var b = t.Create("start", "b");
        var asked = t.Store.SaveDecision(new Decision("toolu_1", a) { Question = "Q1" });
        var forward = t.Store.Events.Forward(asked, Parties.Session(b));
        var report = t.Store.SaveReport(a, ReportStates.Done, "done");
        var byParty = t.Store.Query(new StreamQuery { Party = Parties.Session(b) });
        Assert.That(byParty.Select(e => e.Id), Does.Contain(forward.Id));
        Assert.That(byParty.All(e => e.From == Parties.Session(b) || e.To == Parties.Session(b)), Is.True);
        Assert.That(t.Store.Query(new StreamQuery { Needs = Needs.Ack }).Select(e => e.Id), Is.EqualTo(new[] { report.Id }));
        Assert.That(t.Store.Query(new StreamQuery { Needs = StreamQuery.AnyNeeds }).Select(e => e.Id), Is.EqualTo(new[] { asked.Id, report.Id }));
        Assert.That(t.Store.Query(new StreamQuery { Thread = "toolu_1", Forwards = false }).Select(e => e.Id), Is.EqualTo(new[] { asked.Id }));
        var until = DateTimeOffset.FromUnixTimeMilliseconds(StreamQuery.IdTime(forward.Id)!.Value);
        Assert.That(t.Store.Query(new StreamQuery { Until = until }).Any(e => Core.Ordering.Names.Order.Compare(e.Id, forward.Id) >= 0), Is.False);
    }

    [Test]
    public void The_text_form_reads_flags_views_and_needs()
    {
        using var t = new Rig();
        Assert.That(Parse(t.Store, ("needs", "true")).Needs, Is.EqualTo(StreamQuery.AnyNeeds));
        Assert.That(Parse(t.Store, ("needs", null)).Needs, Is.EqualTo(StreamQuery.AnyNeeds));
        Assert.That(Parse(t.Store, ("needs", "review")).Needs, Is.EqualTo(Needs.Review));
        Assert.That(Parse(t.Store, ("needs", "false")).Needs, Is.Null);
        Assert.That(Parse(t.Store, ("open", null)).Open, Is.True);
        Assert.That(Parse(t.Store, ("forwards", "false")).Forwards, Is.False);
        Assert.That(Parse(t.Store, ("view", "inbox")), Is.EqualTo(StreamQueries.Inbox));
        Assert.That(Parse(t.Store, ("view", "inbox"), ("limit", "2")).Limit, Is.EqualTo(2));
        Assert.That(Parse(t.Store, ("view", "thread"), ("thread", "toolu_1")).Thread, Is.EqualTo("toolu_1"));
        Assert.That(Parse(t.Store, ("kind", "decision, query")).Kinds, Is.EquivalentTo(new[] { "decision", "query" }));
        Assert.Throws<FormatException>(() => Parse(t.Store, ("view", "nothing")));
    }

    [Test]
    public void Blocked_is_a_query_of_the_stream_and_writes_no_file()
    {
        using var t = new Rig();
        var id = t.Create("start");
        Assert.That(t.Store.Blocked(id), Is.Null);
        t.Store.SaveBlocked(id, "turn failed");
        t.Store.SaveBlocked(id, "turn failed");
        Assert.That(t.Store.Blocked(id)?.Reason, Is.EqualTo("turn failed"));
        Assert.That(t.Store.Events.OfKind(EventKinds.Blocked), Has.Count.EqualTo(1));
        t.Store.Events.Append(EventKinds.Blocked, Parties.Session(id), Parties.User, new Dictionary<string, object?> { ["text"] = "delivery unblocked", ["reason"] = null }, session: id);
        Assert.That(t.Store.Blocked(id)?.Reason, Is.Null);
        Assert.That(File.Exists(t.Store.PathOf(id, "blocked.json")), Is.False);
    }

    [Test]
    public void Two_writers_of_one_cursor_lose_no_done_id()
    {
        using var t = new Rig();
        var id = t.Create("start");
        foreach (var i in Enumerable.Range(0, 40)) t.Store.Send(id, $"m{i}");
        var sent = t.Store.Events.OfKind(EventKinds.Message);
        var other = new FileStore(t.Folder, t.Clock);
        var subscriptions = new[] { t.Store, other }.Select(s => s.Events.Subscribe(Parties.Session(id), "deliver", e => e.Kind == EventKinds.Message)).ToArray();
        Parallel.For(0, sent.Count, i => subscriptions[i % 2].Done(sent[i].Id));
        Assert.That(subscriptions[0].Next(), Is.Empty);
    }

    [Test]
    public async Task The_query_routes_give_the_named_queries()
    {
        var (factory, root) = Start();
        using var _ = factory;
        var store = new FileStore(root);
        var id = store.Create(new Meta("") { Title = "child", Cwd = "C:/x" }, "start");
        var asked = store.SaveDecision(new Decision("toolu_7", id) { Question = "Q7" });
        var client = factory.CreateClient();
        async Task<List<string>> Ids(string path)
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return json.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
        }
        Assert.That(await Ids("/api/stream/inbox"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/stream/decisions"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/stream/threads/toolu_7"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/stream?view=inbox"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/stream?kind=decision&needs=answer"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/events?needs=true"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Ids("/api/stream/timeline?limit=1"), Has.Count.EqualTo(1));
        Assert.That(await Ids("/api/inbox"), Is.EqualTo(new[] { asked.Id }));
        Assert.That((await client.GetAsync("/api/stream?view=nothing")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
