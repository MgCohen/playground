using Orchestrator.Web;

namespace Orchestrator.Tests;

/// <summary>V7: the small Markdown part of the text blocks of a document page.</summary>
public sealed class MarkdownTests
{
    static string Html(string text) => Markdown.Render(text).Value;

    [Test]
    public void Paragraphs_and_line_breaks()
    {
        Assert.That(Html("One\ntwo\n\nThree"), Is.EqualTo("<p>One<br>\ntwo</p>\n<p>Three</p>"));
    }

    [Test]
    public void Bullet_and_numbered_lists()
    {
        Assert.That(Html("Issues now:\n- V1, toast\n- V2, ring\n1. Start\n2. Read"),
            Is.EqualTo("<p>Issues now:</p>\n<ul><li>V1, toast</li><li>V2, ring</li></ul>\n<ol><li>Start</li><li>Read</li></ol>"));
    }

    [Test]
    public void An_indented_line_continues_its_item()
    {
        Assert.That(Html("- One\n  more of one\n- Two"), Is.EqualTo("<ul><li>One<br>\nmore of one</li><li>Two</li></ul>"));
    }

    [Test]
    public void Code_and_bold()
    {
        Assert.That(Markdown.Inline("Use `orch send` and **never** port 7777"),
            Is.EqualTo("Use <code>orch send</code> and <strong>never</strong> port 7777"));
    }

    [Test]
    public void Html_in_the_text_is_encoded()
    {
        Assert.That(Html("<script>x</script> `<b>`"), Is.EqualTo("<p>&lt;script&gt;x&lt;/script&gt; <code>&lt;b&gt;</code></p>"));
    }

    [Test]
    public void A_dash_inside_a_line_is_text()
    {
        Assert.That(Html("Fake and -FromLive"), Is.EqualTo("<p>Fake and -FromLive</p>"));
    }
}
