using System.Text.Json;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>The document and the preview of the example of each kind (templates/&lt;kind&gt;/view.json).</summary>
public sealed class ViewTests
{
    static readonly TemplateSet Set = Home.LoadTemplates(null);

    static IEnumerable<string> AllKinds() => Kinds.All;

    static Dictionary<string, object?> Example(string kind) =>
        Render.Fields(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(Set[kind].Folder, "example.json"))));

    static DocumentModel Doc(string kind) => Views.Document(Set, kind, Example(kind));

    static PreviewModel Pre(string kind) => Views.Preview(Set, kind, Example(kind));

    static string[] Labels(DocumentModel d) => d.Blocks.Select(b => b.Label).ToArray();

    static string[] FactLabels(DocumentModel d) => d.Facts.Select(f => f.Label).ToArray();

    [TestCaseSource(nameof(AllKinds))]
    public void Each_kind_has_a_view(string kind) => Assert.That(Set[kind].View, Is.Not.Null, $"{kind}/view.json is missing or does not parse");

    [TestCaseSource(nameof(AllKinds))]
    public void Each_view_names_only_fields_of_the_schema(string kind)
    {
        var props = JsonDocument.Parse(Set[kind].SchemaText).RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        var view = Set[kind].View!;
        var specs = view.Document.Facts.Concat(view.Document.Blocks).Concat(view.Preview.Well ?? [])
            .Append(view.Preview.Chips).Append(view.Preview.List).Append(view.Preview.Pill).OfType<FieldSpec>();
        foreach (var spec in specs)
        {
            foreach (var name in new[] { spec.Field, spec.Mark, spec.Render }.OfType<string>())
                Assert.That(props, Does.Contain(name), $"{kind}: {name}");
        }
    }

    [TestCaseSource(nameof(AllKinds))]
    public void The_example_gives_a_title_in_both_renders(string kind)
    {
        Assert.That(Doc(kind).Title, Is.Not.Empty.And.Not.Contain("{{"));
        Assert.That(Pre(kind).Title, Is.Not.Empty.And.Not.Contain("{{"));
    }

    [Test]
    public void Question()
    {
        var d = Doc(Kinds.Question);
        Assert.That(d.Title, Is.EqualTo("Which color do we use in color.txt?"));
        Assert.That(FactLabels(d), Is.EqualTo(new[] { "Risk", "Default" }));
        Assert.That(d.Facts[0].Tone, Is.EqualTo("pending"));
        Assert.That(Labels(d), Is.EqualTo(new[] { "Context", "Options", "Recommendation" }), "no snippet in the example");
        Assert.That(d.Blocks[1].Items, Is.EqualTo(new[] { "RED", "BLUE" }));
        Assert.That(d.Blocks[1].Marked, Is.EqualTo("BLUE"));

        var p = Pre(Kinds.Question);
        Assert.That(p.Well, Is.EqualTo(new Well("Recommendation", "BLUE: the other files use cool colors.")));
        Assert.That(p.Chips, Is.EqualTo(new[] { "RED", "BLUE" }));
        Assert.That(p.Marked, Is.EqualTo("BLUE"));
        Assert.That(p.Pill?.Text, Is.EqualTo("medium"));
    }

    [Test]
    public void A_question_with_no_recommendation_shows_the_context_in_the_well()
    {
        var p = Views.Preview(Set, Kinds.Question, new Dictionary<string, object?> { ["question"] = "Which port?", ["context"] = "Two apps use 8080." });
        Assert.That(p.Well, Is.EqualTo(new Well("Context", "Two apps use 8080.")));
        Assert.That(p.Line, Is.EqualTo("Two apps use 8080."));
        Assert.That(p.Chips, Is.Empty);
        Assert.That(p.Pill, Is.Null);
    }

    [Test]
    public void Answer()
    {
        var d = Doc(Kinds.Answer);
        Assert.That(d.Title, Is.EqualTo("Which color do we use in color.txt?"), "the question is the title; the answer can be a long voice text (D9)");
        Assert.That(d.Subtitle, Is.EqualTo("GREEN"));
        Assert.That(FactLabels(d), Is.EqualTo(new[] { "By", "Then" }));
        Assert.That(Labels(d), Is.EqualTo(new[] { "Reason", "Source" }));

        var p = Pre(Kinds.Answer);
        Assert.That((p.Title, p.Line), Is.EqualTo(("Which color do we use in color.txt?", "GREEN")));
        Assert.That(p.Well?.Label, Is.EqualTo("Reason"));
        Assert.That(p.Pill?.Tone, Is.EqualTo("pending"));
    }

    [Test]
    public void Review()
    {
        var d = Doc(Kinds.Review);
        Assert.That(d.Title, Is.EqualTo("Which size do we use in size.txt?"));
        Assert.That(d.Facts.Single().Tone, Is.EqualTo("pending"), "verdict change");
        Assert.That(Labels(d), Is.EqualTo(new[] { "Direction", "Branch", "Base", "Next branch" }));

        var p = Pre(Kinds.Review);
        Assert.That(p.Line, Is.EqualTo("change: Use SMALL."));
        Assert.That(p.Well, Is.EqualTo(new Well("Direction", "Use SMALL.")));
    }

    [Test]
    public void A_review_with_no_question_names_its_decision()
    {
        var d = Views.Document(Set, Kinds.Review, new Dictionary<string, object?> { ["decision"] = "toolu_9", ["verdict"] = "approve" });
        Assert.That(d.Title, Is.EqualTo("Review of toolu_9"));
        Assert.That(d.Facts.Single().Tone, Is.EqualTo("done"));
    }

    [Test]
    public void Report()
    {
        var d = Doc(Kinds.Report);
        Assert.That(d.Title, Is.EqualTo("I wrote GREEN into color.txt, because the user chose GREEN."));
        Assert.That((d.Tone, d.Icon), Is.EqualTo(("done", "doc")));
        Assert.That(Labels(d), Is.EqualTo(new[] { "Done", "Links" }));

        var p = Pre(Kinds.Report);
        Assert.That(p.Well, Is.EqualTo(new Well("Done", "color.txt")));
        Assert.That(p.Pill?.Text, Is.EqualTo("done"));
    }

    [Test]
    public void A_blocked_report_shows_its_blockers_first_in_the_failed_tone()
    {
        var fields = new Dictionary<string, object?> { ["state"] = "blocked", ["summary"] = "No token.", ["done"] = new List<object?> { "a" }, ["blockers"] = new List<object?> { "NOTION_TOKEN is missing", "b", "c", "d" } };
        var d = Views.Document(Set, Kinds.Report, fields);
        Assert.That((d.Tone, d.Icon), Is.EqualTo(("failed", "alert")));
        Assert.That(d.Blocks[0].Label, Is.EqualTo("Blockers"));
        Assert.That(d.Blocks[0].Tone, Is.EqualTo("failed"));
        var p = Views.Preview(Set, Kinds.Report, fields);
        Assert.That(p.Well, Is.EqualTo(new Well("Blockers", "NOTION_TOKEN is missing · b · c")), "three items at most");
    }

    [Test]
    public void Update()
    {
        var d = Doc(Kinds.Update);
        Assert.That(d.Title, Is.EqualTo("The size file is written. The color question is next."));
        Assert.That(d.Subtitle, Is.EqualTo("Step 2 of 3"));
        Assert.That(d.Facts.Single().Text, Is.EqualTo("60 %"));

        var p = Pre(Kinds.Update);
        Assert.That(p.Line, Is.EqualTo("Step 2 of 3"));
        Assert.That(p.Progress, Is.EqualTo(60));
    }

    [Test]
    public void Brief()
    {
        var d = Doc(Kinds.Brief);
        Assert.That(d.Title, Is.EqualTo("Color child"));
        Assert.That(FactLabels(d), Is.EqualTo(new[] { "Agent" }));
        Assert.That(Labels(d), Is.EqualTo(new[] { "Goal", "Done when", "Skills" }));
        Assert.That(d.Blocks[2].Items, Is.EqualTo(new[] { "orch-ask", "orch-report" }));

        var p = Pre(Kinds.Brief);
        Assert.That(p.Line, Is.EqualTo("Ask the user which color goes in color.txt, then write it."));
    }

    [Test]
    public void A_brief_shows_a_flag_as_yes_or_no()
    {
        var d = Views.Document(Set, Kinds.Brief, new Dictionary<string, object?> { ["title"] = "T", ["goal"] = "G", ["worktree"] = true, ["background"] = false });
        Assert.That(d.Facts.Select(f => (f.Label, f.Text)), Is.EqualTo(new[] { ("Worktree", "yes"), ("Background", "no") }));
    }

    [Test]
    public void Notice()
    {
        var d = Doc(Kinds.Notice);
        Assert.That(d.Title, Is.EqualTo("Question closed: child \"Size child\" (3f2a9c1e-0000-4000-8000-000000000000), question toolu_01RAQ: Which size do we use in size.txt? Answer: SMALL (from user)."));
        Assert.That(FactLabels(d), Is.EqualTo(new[] { "Kind", "Session", "Child" }));
        Assert.That(d.Blocks.Single().Text, Does.StartWith("Question closed").And.Contain("Do not answer it again."));

        var p = Pre(Kinds.Notice);
        Assert.That(p.Title, Is.EqualTo(d.Title));
        Assert.That(p.Pill?.Text, Is.EqualTo("question-closed"));
    }

    [Test]
    public void Plan()
    {
        var d = Doc(Kinds.Plan);
        Assert.That(d.Title, Is.EqualTo("Split the store module"));
        Assert.That(Labels(d), Is.EqualTo(new[] { "Context", "Steps" }));
        Assert.That(d.Blocks[1].As, Is.EqualTo(Looks.Steps));

        var p = Pre(Kinds.Plan);
        Assert.That(p.List, Has.Count.EqualTo(3));
        Assert.That(p.Line, Is.EqualTo("FileStore.cs has 400 lines."));
    }

    [Test]
    public void Message()
    {
        var d = Doc(Kinds.Message);
        Assert.That(d.Title, Is.EqualTo("Use the new color list in colors.md."));
        Assert.That(d.Blocks, Is.Empty);
        Assert.That(Pre(Kinds.Message).Title, Is.EqualTo(d.Title));
    }

    [Test]
    public void Ack()
    {
        var d = Doc(Kinds.Ack);
        Assert.That(d.Title, Is.EqualTo("delivered · live"));
        Assert.That(d.Facts[0].Tone, Is.EqualTo("done"));
        Assert.That(Pre(Kinds.Ack).Label, Is.EqualTo("Ack"));
    }
}
