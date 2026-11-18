using Orchestrator.Core.Contract;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>The modes and the workflow definitions of the template system (F2), and the layers of core and store.</summary>
public sealed class DefinitionTests
{
    static readonly TemplateSpec Spec = Home.Spec();
    static readonly TemplateSet Templates = Home.LoadTemplates(null);

    string root = "";

    [SetUp]
    public void Folder() => root = Directory.CreateTempSubdirectory("orch-definitions-").FullName;

    [TearDown]
    public void Clean()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }

    static Definition Workflow(string json) => new(new Dictionary<string, string> { ["workflow.json"] = json });

    static string Steps(string steps, string entry = "a") =>
        $$"""{ "name": "flow", "version": 1, "purpose": "A test.", "entry": "{{entry}}", "done": "Done.", "steps": [ {{steps}} ] }""";

    [Test]
    public void The_four_core_modes_are_the_presets_of_the_session_design()
    {
        var modes = Home.Modes(null);
        var rows = modes.List().Select(m => modes.Get(m.Kind)!).Select(m => (m.Name, m.Interaction, m.Autonomy, m.Route, m.Visibility));
        Assert.That(rows, Is.EquivalentTo(new[]
        {
            ("approve", "task", "approve", "parent", "public"),
            ("ask", "stepwise", "ask", "parent", "public"),
            ("auto", "task", "auto", "parent", "public"),
            ("chat", "conversation", "ask", "user", "public"),
        }));
        foreach (var info in modes.List()) Assert.That(modes.Check(info.Kind, modes.Files(info.Kind)!), Is.Empty, info.Kind);
    }

    [Test]
    public void The_axis_values_of_the_schema_are_the_values_of_the_code()
    {
        var bad = new Definition(new Dictionary<string, string>
        {
            ["mode.json"] = """{ "name": "loud", "version": 1, "interaction": "shout", "autonomy": "auto", "route": "user", "visibility": "public", "describe": "x" }""",
        });
        Assert.That(ModeDefinition.Check("loud", bad, Spec), Has.Some.Contains("mode.json/interaction"));
        foreach (var value in ModeDefinition.Interactions)
            Assert.That(ModeDefinition.Check("loud", new Definition(new Dictionary<string, string> { ["mode.json"] = bad["mode.json"]!.Replace("shout", value) }), Spec), Is.Empty, value);
    }

    [Test]
    public void The_planning_workflow_is_a_valid_graph_with_the_question_loop()
    {
        var workflows = Home.Workflows(null, Templates);
        Assert.That(workflows.List().Select(w => w.Kind), Is.EqualTo(new[] { "planning" }));
        Assert.That(workflows.Check("planning", workflows.Files("planning")!), Is.Empty);
        var planning = workflows.Get("planning")!;
        Assert.That(planning.Steps.Select(s => s.Id), Is.EqualTo(new[] { "read", "ask", "answer", "record", "spec", "approve", "propose" }));
        Assert.That(planning.Entry, Is.EqualTo("read"));
        Assert.That(planning.Step("record")!.Next!.Select(n => n.Step), Does.Contain("ask"), "the loop back to the next question");
        Assert.That(planning.Step("approve")!.Approval, Is.EqualTo(new StepApproval("user", "required")));
        Assert.That(planning.Steps.Where(s => s.Done).Select(s => s.Id), Is.EqualTo(new[] { "propose" }));
    }

    [Test]
    public void A_workflow_with_a_broken_graph_is_refused()
    {
        var errors = WorkflowDefinition.Check("flow", Workflow(Steps("""
            { "id": "a", "title": "A", "executor": "ask", "instructions": "x", "session": "owner", "templates": ["sparkle"], "next": [{ "step": "z" }] },
            { "id": "a", "title": "A2", "executor": "agent", "instructions": "x" },
            { "id": "b", "title": "B", "executor": "agent", "instructions": "x" }
            """)), Spec, Templates.Has);
        Assert.That(errors, Has.Some.Contains("the id \"a\" is not unique"));
        Assert.That(errors, Has.Some.Contains("steps.a.next: no step \"z\""));
        Assert.That(errors, Has.Some.Contains("an ask step has no session"));
        Assert.That(errors, Has.Some.Contains("no message template \"sparkle\""));
        Assert.That(errors, Has.Some.Contains("no step can end the work"));
        Assert.That(errors, Has.Some.Contains("steps.b: the entry step does not reach it"));
    }

    [Test]
    public void A_workflow_with_a_missing_entry_step_or_a_wrong_name_is_refused()
    {
        var text = Steps("""{ "id": "a", "title": "A", "executor": "tool", "instructions": "x", "done": true }""", entry: "start");
        Assert.That(WorkflowDefinition.Check("flow", Workflow(text), Spec, null), Has.Some.Contains("entry: no step \"start\""));
        Assert.That(WorkflowDefinition.Check("other", Workflow(text), Spec, null), Has.Some.Contains("name is \"flow\""));
        Assert.That(WorkflowDefinition.Check("flow", Workflow(text.Replace("\"tool\"", "\"robot\"")), Spec, null), Has.Some.Contains("executor"));
    }

    [Test]
    public void A_store_mode_replaces_a_core_mode_until_it_is_removed()
    {
        var modes = Home.Modes(root);
        var chat = modes.Files("chat")!;
        var quiet = new Definition(new Dictionary<string, string>
        {
            ["mode.json"] = chat["mode.json"]!.Replace("\"public\"", "\"private\""),
            ["render/session.md"] = "Mode chat, private.",
        });
        var info = modes.Add("chat", quiet);
        Assert.That((info.Source, info.ReplacesCore), Is.EqualTo((TemplateSources.Store, true)));
        Assert.That(Home.Modes(root).Get("chat")!.Visibility, Is.EqualTo("private"));
        Assert.That(() => modes.Add("chat", quiet), Throws.TypeOf<TemplateException>().With.Message.Contains("--replace"));
        Assert.That(() => modes.Add("bad", new Definition(new Dictionary<string, string> { ["mode.json"] = "{}" })), Throws.TypeOf<TemplateException>());

        modes.Remove("chat");
        Assert.That(Home.Modes(root).Get("chat")!.Visibility, Is.EqualTo("public"));
        Assert.That(() => modes.Remove("chat"), Throws.TypeOf<TemplateException>().With.Message.Contains("core mode"));
    }

    [Test]
    public void A_replace_in_the_local_store_keeps_the_old_folder()
    {
        var modes = Home.Modes(root);
        var auto = modes.Files("auto")!;
        modes.Add("auto", auto);
        modes.Add("auto", new Definition(new Dictionary<string, string> { ["mode.json"] = auto["mode.json"]!.Replace("\"parent\"", "\"user\"") }), replace: true);
        var removed = Path.Combine(root, StoreFiles.Templates, Catalog.Modes.Folder, LocalDefinitionRepository.Removed);
        Assert.That(Directory.GetDirectories(removed), Has.Length.EqualTo(1));
        Assert.That(Home.Modes(root).Get("auto")!.Route, Is.EqualTo("user"));
    }

    [Test]
    public void The_catalog_folders_are_not_message_kinds()
    {
        Assert.That(Home.TemplateStore(null).List().Select(t => t.Kind), Is.EquivalentTo(Kinds.All));
        Assert.That(TemplateStore.IsName(Catalog.Modes.Folder), Is.False);
        Assert.That(TemplateStore.IsName(Catalog.Workflows.Folder), Is.False);
    }

    [Test]
    public void The_cli_checks_and_lists_the_modes_and_workflows()
    {
        var old = Environment.GetEnvironmentVariable("ORCH_STORE");
        Environment.SetEnvironmentVariable("ORCH_STORE", root);
        try
        {
            string Run(params string[] args)
            {
                var output = new StringWriter();
                var code = Cli.Cli.Run(args, output, output, root);
                return $"{code}: {output}";
            }
            var check = Run("template", "check");
            Assert.That(check, Does.StartWith("0: "));
            Assert.That(check, Does.Contain("modes/chat: ok").And.Contain("workflows/planning: ok"));
            Assert.That(Run("template", "modes"), Does.Match(@"chat\s+core"));
            Assert.That(Run("template", "workflows"), Does.Match(@"planning\s+core"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", old);
        }
    }
}
