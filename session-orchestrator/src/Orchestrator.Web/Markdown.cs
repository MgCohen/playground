using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Orchestrator.Web;

/// <summary>
/// V7: the small Markdown part that the texts of sessions use: paragraphs, line breaks, "- " or "* " lists, "1. " lists,
/// `code` and **bold**. The text is HTML-encoded first, so the result has only the tags that this class writes.
/// </summary>
public static partial class Markdown
{
    /// <summary>The blocks of a text: paragraphs and lists.</summary>
    public static MarkupString Render(string text)
    {
        var html = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        string? list = null;

        void EndParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(string.Join(Break, paragraph.Select(Inline))).Append("</p>\n");
            paragraph.Clear();
        }
        void EndList()
        {
            if (list is null) return;
            html.Append("</").Append(list).Append(">\n");
            list = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                EndParagraph();
                EndList();
                continue;
            }
            var item = Item().Match(line);
            if (item.Success)
            {
                var kind = char.IsDigit(item.Groups[1].Value[0]) ? "ol" : "ul";
                EndParagraph();
                if (list != kind)
                {
                    EndList();
                    html.Append('<').Append(kind).Append('>');
                    list = kind;
                }
                html.Append("<li>").Append(Inline(item.Groups[2].Value)).Append("</li>");
                continue;
            }
            // A line under a list item with a deeper indent continues that item.
            if (list is not null && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                html.Insert(html.Length - "</li>".Length, Break + Inline(line.Trim()));
                continue;
            }
            EndList();
            paragraph.Add(line);
        }
        EndParagraph();
        EndList();
        // A newline after each block keeps a space between the blocks in the text content.
        return new MarkupString(html.ToString().TrimEnd('\n'));
    }

    // A line break keeps a newline after the tag, so the text content of the block keeps the space between the lines.
    const string Break = "<br>\n";

    /// <summary>The inline marks of one line: `code` and **bold**, as encoded HTML.</summary>
    public static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Code().Replace(encoded, m => $"<code>{m.Groups[1].Value}</code>");
        return Bold().Replace(encoded, m => $"<strong>{m.Groups[1].Value}</strong>");
    }

    /// <summary>The inline marks of one line, as markup.</summary>
    public static MarkupString Line(string text) => new(Inline(text));

    [GeneratedRegex(@"^\s{0,3}([-*]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex Item();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex Bold();
}
