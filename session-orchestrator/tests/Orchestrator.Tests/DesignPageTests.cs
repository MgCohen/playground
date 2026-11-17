using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestrator.Core.Templates;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// docs/render-design.html is docs/render-design/page.html with the view.json, example.json and purpose of each kind.
/// If the page is not current, run the tests once with ORCH_WRITE_DOCS=1 to write it again.
/// </summary>
public sealed class DesignPageTests
{
    static string Docs => Path.Combine(Home.Root(), "docs");

    public static string Build()
    {
        var data = new JsonObject();
        foreach (var kind in Kinds.All)
        {
            var folder = Path.Combine(Home.Templates(), kind);
            JsonNode Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, name)))!;
            var item = new JsonObject
            {
                ["view"] = Read("view.json"),
                ["example"] = Read("example.json"),
                ["purpose"] = Read("meta.json")["purpose"]!.DeepClone(),
            };
            // The page renders the notice title from the render file that the example names.
            if (kind == Kinds.Notice && item["example"]!["kind"]?.GetValue<string>() is { } name)
                item["renders"] = new JsonObject { [name] = File.ReadAllText(Path.Combine(folder, "render", $"{name}.md")).ReplaceLineEndings("\n") };
            data[kind] = item;
        }
        var json = data.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("</", "<\\/");
        return File.ReadAllText(Path.Combine(Docs, "render-design", "page.html")).ReplaceLineEndings("\n").Replace("/*DATA*/", json);
    }

    [Test]
    public void The_render_design_page_has_the_current_specs()
    {
        var path = Path.Combine(Docs, "render-design.html");
        var page = Build();
        if (Environment.GetEnvironmentVariable("ORCH_WRITE_DOCS") == "1") File.WriteAllText(path, page);
        Assert.That(File.ReadAllText(path).ReplaceLineEndings("\n"), Is.EqualTo(page), "run the tests with ORCH_WRITE_DOCS=1 to write docs/render-design.html again");
    }
}
