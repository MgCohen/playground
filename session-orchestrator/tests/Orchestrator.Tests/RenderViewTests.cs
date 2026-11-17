using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Templates;
using Orchestrator.Store;
using Orchestrator.Web;
using Orchestrator.Web.Components.Pages;
using Orchestrator.Web.Components.Ui;

namespace Orchestrator.Tests;

/// <summary>R2: the Open view, the card and the stream row render the example of each kind from its view.json.</summary>
public sealed class RenderViewTests
{
    static IEnumerable<string> AllKinds() => Kinds.All;

    static Dictionary<string, object?> Example(TemplateSet set, string kind) =>
        Render.Fields(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(set[kind].Folder, "example.json"))));

    sealed class Scene : IDisposable
    {
        public readonly Rig Rig = new();
        public readonly BunitContext Context = new();
        public readonly Board Board;
        public readonly Event Event;
        public readonly Dictionary<string, object?> Fields;

        public Scene(string kind)
        {
            Board = new Board(Rig.Store, Rig.Supervisor);
            Context.JSInterop.Mode = JSRuntimeMode.Loose;
            Context.Services.AddSingleton(Board);
            Context.Services.AddSingleton(new StreamHub(Rig.Store));
            var session = Rig.Store.Create(new Meta("") { Title = "Render " + kind, Cwd = "C:/x" }, "start");
            Fields = Example(Rig.Store.Templates, kind);
            var meta = Rig.Store.Templates[kind].Meta!;
            string Party(string cls) => cls == "session" ? Parties.Session(session) : cls == "*" ? Parties.User : cls;
            var from = Party(meta.From.Contains("session") ? "session" : meta.From[0]);
            var to = Party(meta.To.Contains("user") ? "user" : meta.To[0]);
            Event = Rig.Store.Events.Post(new PostInput(kind, from, to, Fields) { Session = session, Thread = "t-" + kind });
        }

        public void Dispose()
        {
            Context.Dispose();
            Rig.Dispose();
        }
    }

    static string Norm(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static void Has(IElement element, string text, string what) => Assert.That(Norm(element.TextContent), Does.Contain(Norm(text)), what);

    [TestCaseSource(nameof(AllKinds))]
    public void The_open_view_renders_the_full_document_of_the_view(string kind)
    {
        using var scene = new Scene(kind);
        var doc = Views.Document(scene.Rig.Store.Templates, kind, scene.Fields);
        Assert.That(doc.Title, Is.Not.Empty.And.Not.EqualTo(doc.Label), $"{kind}: the example gives a title");

        var page = scene.Context.Render<Open>(p => p.Add(x => x.Id, scene.Event.Id));
        Assert.That(Norm(page.Find(".q").TextContent), Is.EqualTo(Norm(doc.Title)));
        if (doc.Subtitle is { } sub) Has(page.Find(".subtitle"), sub, "subtitle");
        foreach (var fact in doc.Facts)
        {
            var row = page.Find($".facts [data-field='{fact.Field}']");
            Has(row, fact.Label, $"{kind}: fact label {fact.Field}");
            Has(row, fact.Text, $"{kind}: fact {fact.Field}");
        }
        var labels = page.FindAll("h2.gt").Select(h => h.TextContent).ToList();
        foreach (var block in doc.Blocks)
        {
            Assert.That(labels, Does.Contain(block.Label), $"{kind}: block label {block.Field}");
            var box = page.Find($".blk[data-field='{block.Field}']");
            Assert.That(box.ClassList, Does.Contain(block.As), $"{kind}: look of {block.Field}");
            foreach (var item in block.Items.Count > 0 && block.As is not (Looks.Text or Looks.Code or Looks.Well) ? block.Items : [block.Text])
                Has(box, item, $"{kind}: block {block.Field}");
            if (block.Marked is { } marked) Assert.That(box.QuerySelector(".chip.def")?.TextContent, Is.EqualTo(marked));
        }
        Assert.That(page.FindAll(".blk").Count, Is.EqualTo(doc.Blocks.Count), $"{kind}: only the blocks of the view");
    }

    [TestCaseSource(nameof(AllKinds))]
    public void The_stream_row_renders_the_preview_of_the_view(string kind)
    {
        using var scene = new Scene(kind);
        var pre = Views.Preview(scene.Rig.Store.Templates, kind, scene.Fields);
        var row = scene.Context.Render<EventRow>(p => p.Add(x => x.Event, scene.Event));
        Assert.That(Norm(row.Find(".t2").TextContent), Is.EqualTo(Norm(pre.Title)));
        if (pre.Line is { } line) Has(row.Find(".line"), line, $"{kind}: line");
        else Assert.That(row.FindAll(".line"), Is.Empty);
        if (pre.Pill is { } pill) Has(row.Find(".l3"), pill.Text, $"{kind}: pill");
        Assert.That(row.FindAll(".pbar").Count, Is.EqualTo(pre.Progress is null ? 0 : 1), $"{kind}: progress");
    }

    [TestCaseSource(nameof(AllKinds))]
    public void The_card_renders_the_preview_of_the_view(string kind)
    {
        using var scene = new Scene(kind);
        var pre = Views.Preview(scene.Rig.Store.Templates, kind, scene.Fields);
        var card = Ui.Classify(new Card(scene.Event, scene.Event.Session ?? "", "Render " + kind, scene.Board.Fields(scene.Event))) with { Preview = scene.Board.Preview(scene.Event) };
        var view = scene.Context.Render<CardPreview>(p => p.Add(x => x.Card, card).Add(x => x.Now, scene.Board.Now));
        Assert.That(Norm(view.Find(".q").TextContent), Is.EqualTo(Norm(pre.Title)));
        if (pre.Well is { } well)
        {
            Has(view.Find(".well"), well.Label, $"{kind}: well label");
            Has(view.Find(".well"), well.Text, $"{kind}: well");
        }
        else if (pre.Line is { } line) Has(view.Find(".line"), line, $"{kind}: line");
        Assert.That(view.FindAll(".in ul.doc li").Select(li => li.TextContent), Is.EqualTo(pre.List), $"{kind}: list");
        Assert.That(view.FindAll(".chip").Select(c => c.TextContent), Is.EqualTo(pre.Chips), $"{kind}: chips");
        if (pre.Marked is { } marked) Assert.That(view.Find(".chip.def").TextContent, Is.EqualTo(marked));
        if (pre.Pill is { } pill) Has(view.Find(".head"), pill.Text, $"{kind}: pill");
        Assert.That(view.FindAll(".pbar").Count, Is.EqualTo(pre.Progress is null ? 0 : 1), $"{kind}: progress");
    }

    [Test]
    public void A_kind_with_no_view_shows_each_field_as_text()
    {
        using var rig = new Rig();
        var board = new Board(rig.Store, rig.Supervisor);
        var doc = board.Document("pr.open", new Dictionary<string, object?> { ["url"] = "https://x/pr/1", ["branches"] = new List<object?> { "a", "b" } }, "PR 1");
        Assert.That(doc.Title, Is.EqualTo("PR 1"));
        Assert.That(doc.Blocks.Select(b => (b.Label, b.As)), Is.EqualTo(new[] { ("Url", Looks.Text), ("Branches", Looks.List) }));
        Assert.That(board.Preview("pr.open", new Dictionary<string, object?>(), "PR 1").Title, Is.EqualTo("PR 1"));
    }
}
