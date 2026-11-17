using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Templates;
using Orchestrator.Store;
using Orchestrator.Web.Components.Ui.Looks;

namespace Orchestrator.Tests;

/// <summary>The template store: the interface, the local implementation (core and store folders), the spec checks and the look catalog.</summary>
public sealed class TemplateStoreTests
{
    static readonly TemplateSpec Spec = TemplateSpec.Load(Home.Templates());

    string root = "";

    string StoreRoot => Path.Combine(root, StoreFiles.Templates);

    LocalTemplateStore Store() => new(Home.Templates(), StoreRoot, Spec);

    [SetUp]
    public void Folder() => root = Directory.CreateTempSubdirectory("orch-templates-").FullName;

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

    /// <summary>A new kind: a schema, a meta, a view and an example, and no C# change.</summary>
    static TemplateFiles RiskNote(string kind = "risk-note", string? view = null) => new(kind,
        """
        { "$schema": "https://json-schema.org/draft/2020-12/schema", "description": "A risk that a session found.", "type": "object", "required": ["risk", "level"],
          "properties": { "risk": { "type": "string", "minLength": 1 }, "level": { "enum": ["low", "high"] }, "steps": { "type": "array", "items": { "type": "string" } } },
          "additionalProperties": false }
        """,
        $$"""
        { "type": "{{kind}}", "version": 1, "purpose": "Tell the user about a risk.", "from": ["session"], "to": ["user", "session"], "needs": "ack", "replies": ["ack"], "card": null }
        """,
        view ?? """
        { "label": "Risk note", "icon": "alert", "tone": "pending",
          "toneBy": { "field": "level", "values": { "high": "failed" } },
          "document": { "title": ["{{risk}}"], "facts": [ { "field": "level", "label": "Level", "as": "pill", "tones": { "low": "done", "high": "failed" } } ],
                        "blocks": [ { "field": "steps", "label": "Steps", "as": "steps" } ] },
          "preview": { "title": ["{{risk}}"], "pill": { "field": "level", "label": "Level", "as": "pill" } } }
        """,
        """{ "risk": "The token is in the log.", "level": "high", "steps": ["Rotate the token", "Clean the log"] }""",
        new Dictionary<string, string> { ["session"] = "Risk ({{level}}): {{risk}}" });

    static IEnumerable<string> CoreKinds() => Kinds.All;

    [TestCaseSource(nameof(CoreKinds))]
    public void Each_core_template_passes_the_spec(string kind)
    {
        var files = new LocalTemplateStore(Home.Templates(), null).Get(kind)!;
        Assert.That(TemplateCheck.Check(files, Spec), Is.Empty);
    }

    [Test]
    public void The_core_list_has_the_core_kinds_only()
    {
        var list = Store().List();
        Assert.That(list.Select(t => t.Kind), Is.EquivalentTo(Kinds.All), "_spec is not a kind");
        Assert.That(list.All(t => t.Source == TemplateSources.Core && !t.ReplacesCore));
    }

    [Test]
    public void An_added_template_works_with_no_code_change()
    {
        var info = Store().Add(RiskNote());
        Assert.That(info, Is.EqualTo(new TemplateInfo("risk-note", TemplateSources.Store, false, Path.Combine(StoreRoot, "risk-note"))));

        var store = new FileStore(root);
        Assert.That(store.Templates.Has("risk-note"));
        var session = store.Create(new Meta("") { Title = "Child", Cwd = "C:/x" });
        var posted = store.Events.Post(new PostInput("risk-note", Parties.Session(session), Parties.User, new Dictionary<string, object?> { ["risk"] = "The token is in the log.", ["level"] = "high" }));
        Assert.That(() => store.Events.Post(new PostInput("risk-note", Parties.Session(session), Parties.User, new Dictionary<string, object?> { ["risk"] = "X", ["level"] = "medium" })),
            Throws.TypeOf<MessageException>(), "the stream checks the schema of an added kind");

        var doc = Views.Document(store.Templates, "risk-note", store.Events.Fields(posted));
        Assert.That((doc.Title, doc.Tone, doc.Icon), Is.EqualTo(("The token is in the log.", "failed", "alert")));
        Assert.That(doc.Facts.Single().Tone, Is.EqualTo("failed"));
        Assert.That(store.Renderer.Text(posted, "session"), Is.EqualTo("Risk (high): The token is in the log."));
    }

    [Test]
    public void A_store_template_replaces_a_core_template_until_it_is_removed()
    {
        var core = new LocalTemplateStore(Home.Templates(), null).Get(Kinds.Message)!;
        var view = core.View!.Replace("\"label\": \"Message\"", "\"label\": \"Note\"");
        Store().Add(core with { View = view });

        var set = new TemplateSet(Store(), Home.Templates());
        Assert.That(set[Kinds.Message].ReplacesCore);
        Assert.That(set[Kinds.Message].View!.Label, Is.EqualTo("Note"));

        Store().Remove(Kinds.Message);
        set = new TemplateSet(Store(), Home.Templates());
        Assert.That((set[Kinds.Message].Source, set[Kinds.Message].View!.Label), Is.EqualTo((TemplateSources.Core, "Message")));
        Assert.That(Directory.GetDirectories(Path.Combine(StoreRoot, LocalTemplateStore.Removed)), Has.Length.EqualTo(1), "the removed files stay");
    }

    [Test]
    public void A_second_add_needs_replace()
    {
        Store().Add(RiskNote());
        Assert.That(() => Store().Add(RiskNote()), Throws.TypeOf<TemplateException>().With.Message.Contains("--replace"));
        Assert.That(Store().Add(RiskNote(), replace: true).Kind, Is.EqualTo("risk-note"));
    }

    [Test]
    public void A_core_template_cannot_be_removed() =>
        Assert.That(() => Store().Remove(Kinds.Question), Throws.TypeOf<TemplateException>().With.Message.Contains("core template"));

    [Test]
    public void A_template_that_does_not_pass_the_spec_is_not_added()
    {
        var bad = RiskNote(view: """
            { "label": "Risk", "icon": "alert", "tone": "pending",
              "document": { "title": ["{{risk}} {{owner}}"], "facts": [ { "field": "steps", "label": "Steps", "as": "chips" } ],
                            "blocks": [ { "field": "level", "label": "Level", "as": "sparkle" } ] },
              "preview": { "title": ["{{risk}}"] } }
            """);
        var errors = TemplateCheck.Check(bad, Spec);
        Assert.That(errors, Has.Some.Contains("\"owner\" is not a property"));
        Assert.That(errors, Has.Some.Contains("look \"chips\" is not allowed as a fact"));
        Assert.That(errors, Has.Some.Contains("look \"sparkle\" is not in looks.json"));
        Assert.That(() => Store().Add(bad), Throws.TypeOf<TemplateException>());
        Assert.That(Directory.Exists(Path.Combine(StoreRoot, "risk-note")), Is.False);
    }

    [TestCase("Risk")]
    [TestCase("pr.opened")]
    [TestCase("check")]
    [TestCase("x")]
    public void A_bad_name_is_refused(string kind) =>
        Assert.That(TemplateCheck.Check(RiskNote(kind), Spec), Has.Some.Contains("the name must match"));

    [Test]
    public void The_meta_type_must_be_the_folder_name() =>
        Assert.That(TemplateCheck.Check(RiskNote() with { Kind = "risk-two" }, Spec), Has.Some.Contains("type is \"risk-note\""));

    [Test]
    public void A_broken_store_template_does_not_stop_the_others()
    {
        Directory.CreateDirectory(Path.Combine(StoreRoot, "broken"));
        File.WriteAllText(Path.Combine(StoreRoot, "broken", "schema.json"), "{ not json");
        var set = new TemplateSet(Store(), Home.Templates());
        Assert.That(set.Has(Kinds.Question));
        Assert.That(set.LoadErrors, Has.One.StartsWith("broken"));
    }

    [Test]
    public void The_cli_lists_checks_adds_and_removes()
    {
        var folder = Path.Combine(root, "source", "risk-note");
        RiskNote().WriteFolder(folder);
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
            Assert.That(Run("template", "check", "--folder", folder), Does.StartWith("0: risk-note: ok"));
            Assert.That(Run("template", "add", folder), Does.StartWith("0: Added risk-note"));
            Assert.That(Run("template"), Does.Match(@"risk-note\s+store\s+A risk that a session found\."));
            Assert.That(Run("template", "risk-note"), Does.Contain("level (required): low|high"));
            Assert.That(Run("template", "remove", "risk-note"), Does.StartWith("0: Removed risk-note"));
            Assert.That(Run("template", "remove", "question"), Does.Contain("core template"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", old);
        }
    }

    [Test]
    public void Each_look_of_the_catalog_has_one_app_component_and_one_page_function()
    {
        var names = Spec.Looks.Looks.Select(l => l.Name).ToList();
        Assert.That(LookComponents.All.Keys, Is.EquivalentTo(names), "Components/Ui/Looks/<Name>Look.razor");

        var page = File.ReadAllText(Path.Combine(Home.Root(), "docs", "render-design", "page.html"));
        var body = Regex.Match(page, @"const LOOKS = \{(.*?)\n\};", RegexOptions.Singleline).Groups[1].Value;
        var keys = Regex.Matches(body, @"^\s{2}(\w+): \{", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        Assert.That(keys, Is.EquivalentTo(names), "the LOOKS object of docs/render-design/page.html");
        foreach (var look in Spec.Looks.Looks)
        {
            var line = Regex.Match(body, $@"^\s{{2}}{look.Name}: \{{(.*)$", RegexOptions.Multiline).Groups[1].Value;
            foreach (var place in new[] { LookCatalog.Fact, LookCatalog.Block })
                Assert.That(line.Contains($"{place}:"), Is.EqualTo(look.Places.Contains(place)), $"page: {look.Name} as a {place}");
        }
    }
}
