using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Web;

namespace Orchestrator.Tests;

/// <summary>C4: the stream id in each envelope, the stream filter of the query, the CLI flags and the HTTP parameter.</summary>
[NonParallelizable]
public sealed class StreamFilterTests
{
    /// <summary>A root in stream C with one child (no stream given), and a root with no stream.</summary>
    static (string Parent, string Child, string Other) Tree(Rig rig)
    {
        var messages = new Messages(rig.Store);
        var parent = messages.NewSession(new BriefFields { Title = "Parent C", Goal = "g", Cwd = "C:/x", Stream = "C" }, "test");
        var child = messages.NewSession(new BriefFields { Title = "Child C", Goal = "g", Cwd = "C:/x", Parent = parent }, "test");
        var other = messages.NewSession(new BriefFields { Title = "Other", Goal = "g", Cwd = "C:/x" }, "test");
        return (parent, child, other);
    }

    static string Run(Rig rig, params string[] args)
    {
        var output = new StringWriter();
        Environment.SetEnvironmentVariable("ORCH_STORE", rig.Folder);
        try
        {
            var code = Cli.Cli.Run(args, output, output, rig.Folder);
            Assert.That(code, Is.EqualTo(0), output.ToString());
            return output.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }

    [Test]
    public void An_envelope_has_the_stream_of_its_session_and_a_child_has_the_stream_of_its_parent()
    {
        using var rig = new Rig();
        var (parent, child, other) = Tree(rig);
        Assert.That(rig.Store.Session(child).Stream, Is.EqualTo("C"));
        var asked = rig.Store.SaveDecision(new Decision("toolu_c", child) { Question = "Q" });
        var free = rig.Store.SaveDecision(new Decision("toolu_o", other) { Question = "Q" });
        Assert.That((asked.Stream, free.Stream), Is.EqualTo(("C", (string?)null)));
        Assert.That(rig.Store.Events.All().Where(e => e.Kind == EventKinds.Brief).Select(e => e.Stream), Is.EqualTo(new[] { "C", "C", null }));

        var forward = rig.Store.Events.Forward(asked, Parties.Session(parent));
        Assert.That(forward.Stream, Is.EqualTo("C"), "a forward keeps the stream of the original");
        var given = rig.Store.Events.Post(new PostInput(EventKinds.SessionOutput, Parties.Session(other), Parties.User, new Dictionary<string, object?> { ["text"] = "x" }) { Session = other, Stream = "D" });
        Assert.That(given.Stream, Is.EqualTo("D"), "a stream in the post input wins");

        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(rig.Folder, StoreFiles.Events), $"{free.Id}.json", SearchOption.AllDirectories).Single());
        Assert.That(text, Does.Not.Contain("\"stream\""), "an envelope with no stream has no stream field");
    }

    [Test]
    public void An_old_envelope_with_no_stream_field_is_readable_and_has_no_stream()
    {
        var old = """{"id":"1763300000000-a1b2c3d4-cli","at":"2026-11-16T10:00:00.000Z","session":"s","kind":"update","from":"session:s","to":"user","subject":"x","body":"bodies/2026-11-16/1763300000000-a1b2c3d4-cli.json","refs":{}}""";
        var e = JsonSerializer.Deserialize<Event>(old, StoreJson.Options)!;
        Assert.That((e.Id, e.Stream), Is.EqualTo(("1763300000000-a1b2c3d4-cli", (string?)null)));
        Assert.That(new StreamQuery { Stream = "C" }.Matches(e), Is.False);
        Assert.That(new StreamQuery().Matches(e), Is.True);
    }

    [Test]
    public void The_query_filters_by_stream_in_the_record_and_in_the_text_form()
    {
        using var rig = new Rig();
        var (parent, child, _) = Tree(rig);
        var map = new Dictionary<string, string?> { ["stream"] = "c", ["kind"] = "brief" };
        var query = StreamQueryText.Parse(key => map.TryGetValue(key, out var v) ? (true, v) : (false, null), rig.Clock.GetUtcNow());
        Assert.That(query.Stream, Is.EqualTo("c"));
        Assert.That(rig.Store.Query(query).Select(e => e.Session), Is.EqualTo(new[] { parent, child }), "the match ignores the case");
        Assert.That(StreamQueryText.Keys, Does.Contain("stream"));
    }

    [Test]
    public void The_cli_filters_events_tree_status_and_questions_by_stream()
    {
        using var rig = new Rig();
        var (parent, child, other) = Tree(rig);
        rig.Store.SaveDecision(new Decision("toolu_c", child) { Question = "Question of C" });
        rig.Store.SaveDecision(new Decision("toolu_o", other) { Question = "Question of other" });

        var tree = Run(rig, "tree", "--stream", "C");
        Assert.That(tree, Does.Contain("Parent C").And.Contain("  " + child[..8]).And.Not.Contain("Other"));
        Assert.That(Run(rig, "tree"), Does.Contain("Other"));
        Assert.That(Run(rig, "tree", "--stream", "Z"), Does.Contain("No sessions in stream Z."));

        var questions = Run(rig, "questions", "--stream", "C");
        Assert.That(questions, Does.Contain("Question of C").And.Not.Contain("Question of other"));

        var status = Run(rig, "status", "--stream", "C");
        Assert.That(status, Does.Contain($"id: {parent}").And.Contain($"id: {child}").And.Contain("stream: C").And.Not.Contain(other));

        var events = Run(rig, "events", "--stream", "C", "--kind", "question");
        Assert.That(events, Does.Contain("toolu_c").Or.Contain("Question of C"));
        Assert.That(events, Does.Not.Contain("Question of other"));
    }

    [Test]
    public void Orch_new_takes_a_stream_and_a_child_with_no_stream_takes_the_stream_of_its_parent()
    {
        using var rig = new Rig();
        Run(rig, "new", "--title", "Root", "--goal", "g", "--cwd", "C:/x", "--stream", "E");
        var root = rig.Store.AllSessions().Single(id => rig.Store.Session(id).Title == "Root");
        Run(rig, "new", "--title", "Kid", "--goal", "g", "--cwd", "C:/x", "--parent", root);
        Run(rig, "new", "--title", "Own", "--goal", "g", "--cwd", "C:/x", "--parent", root, "--stream", "F");
        string StreamOf(string title) => rig.Store.Session(rig.Store.AllSessions().Single(id => rig.Store.Session(id).Title == title)).Stream!;
        Assert.That((StreamOf("Root"), StreamOf("Kid"), StreamOf("Own")), Is.EqualTo(("E", "E", "F")));
    }

    [Test]
    public void The_new_session_api_body_takes_a_stream()
    {
        Assert.That(new NewSession("t", "C:/x", "g", null) { Stream = "C" }.ToBrief().Stream, Is.EqualTo("C"));
        Assert.That(new NewSession("t", "C:/x", "g", "p") { Stream = " " }.ToBrief().Stream, Is.Null, "an empty stream takes the stream of the parent");
    }

    [Test]
    public async Task The_new_session_api_takes_a_type_and_a_workflow()
    {
        var store = Directory.CreateTempSubdirectory("orch-new-").FullName;
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        using var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;
        Environment.SetEnvironmentVariable("ORCH_STORE", null);
        var client = factory.CreateClient();
        var body = new StringContent("""{"title":"T","cwd":"C:/x","goal":"g","stream":"C","type":"decider","workflow":"planning"}""", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/sessions", body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var meta = new FileStore(store).Session(id);
        Assert.That((meta.Stream, meta.Type?.Agent, meta.Workflow), Is.EqualTo(("C", "decider", "planning")));
        Assert.That(new NewSession("t", "C:/x", "g", null) { Type = " ", Workflow = "" }.ToBrief() is { Type: null, Workflow: null }, "empty fields mean none");
    }

    [Test]
    public async Task The_http_api_takes_a_stream_parameter()
    {
        var store = Directory.CreateTempSubdirectory("orch-stream-").FullName;
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        using var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;
        Environment.SetEnvironmentVariable("ORCH_STORE", null);
        var files = new FileStore(store);
        var messages = new Messages(files);
        var parent = messages.NewSession(new BriefFields { Title = "P", Goal = "g", Cwd = "C:/x", Stream = "C" }, "test");
        var child = messages.NewSession(new BriefFields { Title = "K", Goal = "g", Cwd = "C:/x", Parent = parent }, "test");
        var other = messages.NewSession(new BriefFields { Title = "O", Goal = "g", Cwd = "C:/x" }, "test");
        var asked = files.SaveDecision(new Decision("toolu_c", child) { Question = "Q" });
        files.SaveDecision(new Decision("toolu_o", other) { Question = "Q" });
        var client = factory.CreateClient();
        async Task<List<string>> Values(string path, string property)
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().Select(e => e.GetProperty(property).GetString()!).ToList();
        }
        Assert.That(await Values("/api/stream?stream=C&kind=question", "id"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Values("/api/stream/decisions?stream=C", "id"), Is.EqualTo(new[] { asked.Id }));
        Assert.That(await Values("/api/events?stream=C&kind=brief", "session"), Is.EqualTo(new[] { parent, child }));
        Assert.That(await Values("/api/decisions?stream=C", "id"), Is.EqualTo(new[] { "toolu_c" }));
        Assert.That(await Values("/api/sessions?stream=C", "id"), Is.EquivalentTo(new[] { parent, child }));
        Assert.That(await Values("/api/sessions", "id"), Has.Count.EqualTo(3));
    }
}
