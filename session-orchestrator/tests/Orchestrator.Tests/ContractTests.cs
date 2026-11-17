using System.Diagnostics;
using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>The contract between the two bindings of the Stream API: the plugin (TypeScript) and the store (C#).</summary>
public sealed class ContractTests
{
    static readonly string Hooks = Path.Combine(Home.Root(), "plugin", "hooks");

    /// <summary>One set of cases for the subject rule. Both bindings must give the same subject.</summary>
    static readonly (string Kind, string Data, string Subject)[] Subjects =
    [
        ("question", """{"question":"Which  color\ndo we use?","context":"c"}""", "Which color do we use?"),
        ("answer", """{"decision":"d","text":"GREEN","by":"user","question":"Q"}""", "GREEN"),
        ("review", """{"decision":"d","verdict":"change","text":"Use SMALL."}""", "change: Use SMALL."),
        ("review", """{"decision":"d","verdict":"approve"}""", "approve"),
        ("report", """{"state":"done","summary":"All good."}""", "All good."),
        ("ack", """{"state":"delivered","by":"live"}""", "delivered (live)"),
        ("notice", """{"kind":"restart","count":2}""", "restart"),
        ("notice", """{"kind":"question-closed","question":"Q?"}""", "question-closed: Q?"),
        ("message", """{"text":"a long text that is longer than eighty characters, so the rule must cut it at eighty"}""", "a long text that is longer than eighty characters, so the rule must cut it at ei"),
        ("brief", """{"title":"Color child","goal":"g"}""", "Color child"),
    ];

    [Test]
    public void The_subject_rule_of_the_store_matches_the_cases()
    {
        foreach (var (kind, data, subject) in Subjects)
            Assert.That(Core.Events.Subjects.Of(kind, Core.Templates.Render.Fields(JsonSerializer.Deserialize<JsonElement>(data))), Is.EqualTo(subject), $"{kind} {data}");
    }

    [Test]
    public void The_subject_rule_of_the_plugin_matches_the_cases()
    {
        var cases = JsonSerializer.Serialize(Subjects.Select(c => new { kind = c.Kind, data = JsonSerializer.Deserialize<JsonElement>(c.Data), subject = c.Subject }));
        var module = new Uri(Path.Combine(Hooks, "stream.ts")).AbsoluteUri;
        var script = $"import {{ subject }} from '{module}'; const cases = {cases}; const bad = cases.filter(c => subject(c.kind, c.data) !== c.subject).map(c => `${{c.kind}}: ${{subject(c.kind, c.data)}}`); console.log(bad.length === 0 ? 'ok' : bad.join('\\n'))";
        var output = Node(script);
        if (output is null) Assert.Ignore("node is not on the PATH");
        Assert.That(output, Is.EqualTo("ok"));
    }

    [Test]
    public void The_plugin_refuses_an_unknown_kind()
    {
        var module = new Uri(Path.Combine(Hooks, "stream.ts")).AbsoluteUri;
        var output = Node($"import {{ knownKind }} from '{module}'; console.log([knownKind('question'), knownKind('ack'), knownKind('session.input'), knownKind('decisoin'), knownKind('question.forward'), knownKind('decision'), knownKind('query')].join(','))");
        if (output is null) Assert.Ignore("node is not on the PATH");
        Assert.That(output, Is.EqualTo("true,true,true,false,false,false,false"));
    }

    [Test]
    public void The_plugin_knows_the_store_version_of_the_supervisor()
    {
        var register = File.ReadAllText(Path.Combine(Hooks, "register.ts"));
        Assert.That(register, Does.Contain($"export const STORE_VERSION = {StoreVersion.Current}"));
        Assert.That(register, Does.Contain("if (!(await versionOk($))) return"));
    }

    [Test]
    public void An_envelope_of_the_plugin_reads_with_its_thread_needs_and_body()
    {
        using var t = new Rig();
        var id = t.Create("start");
        var day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000;
        var envelope = $"{ms}-abcdef12-live";
        Files.Write(Path.Combine(t.Folder, StoreFiles.Bodies, day, $"{envelope}.json"), """
            { "question": "Q?", "context": "C", "snippet": "x = 1", "options": ["A", "B"], "recommendation": "A: simple", "risk": "medium", "defaultOption": "A", "waitSeconds": 0 }
            """);
        Files.Write(Path.Combine(t.Folder, StoreFiles.Events, day, $"{envelope}.json"), $$"""
            { "id": "{{envelope}}", "at": "2026-11-16T10:00:00.000Z", "session": "{{id}}", "kind": "decision", "from": "session:{{id}}", "to": "user",
              "subject": "Q?", "body": "bodies/{{day}}/{{envelope}}.json", "refs": {}, "thread": "toolu_1", "needs": "answer" }
            """);
        var d = t.Store.Decision(id, "toolu_1")!;
        Assert.That((d.Context, d.Snippet, d.Recommendation, d.DefaultOption, d.Event), Is.EqualTo(("C", "x = 1", "A: simple", "A", envelope)));
        Assert.That(t.Store.OpenDecisions(id).Single().Options, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(t.Store.Events.Find(envelope)!.Needs, Is.EqualTo(Needs.Answer));
        Assert.That(t.Store.Events.Find(envelope)!.Kind, Is.EqualTo(EventKinds.Question), "an old store: the kind decision reads as question");
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
        var tools = File.ReadAllText(Path.Combine(Hooks, "tools.ts"));
        foreach (var kind in new[] { "question", "plan", "report", "update", "message", "brief" })
            Assert.That(tools, Does.Contain($"templateSchema($, '{kind}')"), kind);
    }

    [Test]
    public void The_plugin_writes_no_old_store_file()
    {
        foreach (var file in Directory.GetFiles(Hooks, "*.ts"))
        {
            var text = File.ReadAllText(file);
            foreach (var old in new[] { "/inbox", "/answers/", "/delivered/", "cursor.txt", "report.json", "update.json", "/decisions/", "blocked.json" })
                Assert.That(text, Does.Not.Contain(old), $"{Path.GetFileName(file)} uses {old}");
        }
    }

    static string? Node(string script)
    {
        try
        {
            var info = new ProcessStartInfo("node", ["--input-type=module", "-e", script]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
