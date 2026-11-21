using System.Text.RegularExpressions;

namespace Orchestrator.Tests;

/// <summary>
/// The rules of the size ownership (R2, I10): the page owns each size of the tray. tray.css lays out the parts that the windows
/// need with tokens, so tray.js reads one value for each size and reports it to the windows.
/// </summary>
public sealed class TraySizeTests
{
    static string Repo() => Orchestrator.Store.Home.Root();

    static string TrayCss() => File.ReadAllText(Path.Combine(Repo(), "src", "Orchestrator.Tray", "wwwroot", "tray.css"));

    /// <summary>The first rule of a selector: its declarations.</summary>
    static string Rule(string css, string selector)
    {
        var at = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"no rule {selector}");
        var open = css.IndexOf('{', at);
        return css[(open + 1)..css.IndexOf('}', open)];
    }

    [Test]
    public void Tray_sizes_are_tokens()
    {
        var css = TrayCss();
        (string Selector, string Properties)[] sized =
        [
            (".tray-shadow", "inset"),
            (".tb-tab", "top|height|width|border-radius"),
            (".tb-peek", "top|right"),
        ];
        var literals = sized
            .SelectMany(s => Regex.Matches(Rule(css, s.Selector), $@"(?<![\w-])(?<p>{s.Properties})\s*:\s*(?<v>[^;]*\d+px[^;]*)")
                .Select(m => $"{s.Selector} {m.Groups["p"].Value}: {m.Groups["v"].Value.Trim()}"))
            .Where(l => !l.Contains("var(--", StringComparison.Ordinal))
            .ToList();
        Assert.That(literals, Is.Empty, "a size of tray.css that a window needs has no token:\n" + string.Join("\n", literals));
    }
}
