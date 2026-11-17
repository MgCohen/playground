using System.Text.Json;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class TemplateTests
{
    static readonly TemplateSet Set = new(Home.Templates());

    static IEnumerable<string> AllKinds() => Kinds.All;

    static JsonElement Example(string kind) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(Set[kind].Folder, "example.json")));

    [Test]
    public void Each_kind_has_a_template() => Assert.That(Set.All.Select(t => t.Kind), Is.EquivalentTo(Kinds.All));

    [TestCaseSource(nameof(AllKinds))]
    public void The_example_matches_the_schema(string kind)
    {
        var check = Set.Validate(kind, Example(kind));
        Assert.That(check.IsValid, Is.True, check.Message);
    }

    [TestCaseSource(nameof(AllKinds))]
    public void Each_render_of_the_example_fills_each_place(string kind)
    {
        var fields = Render.Fields(Example(kind));
        var names = Set[kind].RenderNames().Prepend(null).Where(name => name is not null || Set[kind].HasRender());
        foreach (var name in names)
        {
            var text = Set.Render(kind, fields, name);
            Assert.That(text, Is.Not.Empty, $"{kind}/{name}");
        }
    }

    [Test]
    public void A_missing_required_field_is_refused()
    {
        var check = Set.Validate(Kinds.Question, JsonSerializer.Deserialize<JsonElement>("""{"context":"C","risk":"low"}"""));
        Assert.That(check.IsValid, Is.False);
        Assert.That(check.Message, Does.Contain("required").IgnoreCase);
    }

    [Test]
    public void A_question_needs_only_its_text_and_a_risk_with_a_default()
    {
        bool Valid(string json) => Set.Validate(Kinds.Question, JsonSerializer.Deserialize<JsonElement>(json)).IsValid;
        Assert.That(Valid("""{"question":"Which port?"}"""), Is.True, "a question with no default");
        Assert.That(Valid("""{"question":"Which color?","options":["RED","BLUE"],"risk":"low","defaultOption":"BLUE"}"""), Is.True, "a question with a default");
        Assert.That(Valid("""{"question":"Which color?","defaultOption":"BLUE"}"""), Is.False, "a default with no risk");
        Assert.That(Valid("""{"question":"Which color?","risk":"low"}"""), Is.True, "a risk with no default");
    }

    [Test]
    public void An_unknown_verdict_is_refused() =>
        Assert.That(Set.Validate(Kinds.Review, JsonSerializer.Deserialize<JsonElement>("""{"decision":"d","verdict":"maybe"}""")).IsValid, Is.False);

    [Test]
    public void A_section_shows_only_when_its_field_has_a_value()
    {
        Assert.That(Render.Text("A{{#x}} [{{x}}]{{/x}}.", new Dictionary<string, object?> { ["x"] = "1" }), Is.EqualTo("A [1]."));
        Assert.That(Render.Text("A{{#x}} [{{x}}]{{/x}}.", new Dictionary<string, object?> { ["x"] = "" }), Is.EqualTo("A."));
        Assert.That(Render.Text("{{list}}", new Dictionary<string, object?> { ["list"] = new List<object?> { "a", "b" } }), Is.EqualTo("a, b"));
    }

    [Test]
    public void Each_notice_kind_in_the_schema_has_a_render_file()
    {
        var schema = JsonDocument.Parse(Set[Kinds.Notice].SchemaText).RootElement;
        var kinds = schema.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.That(Set[Kinds.Notice].RenderNames(), Is.EquivalentTo(kinds));
    }
}
