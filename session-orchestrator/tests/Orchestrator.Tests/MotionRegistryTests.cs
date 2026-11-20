using System.Text.RegularExpressions;
using Orchestrator.Web.Features.Motion.Domain;
using Orchestrator.Web.Plugins.Js.Contracts;
using Orchestrator.Web.Plugins.Js.Infrastructure;

namespace Orchestrator.Tests;

/// <summary>
/// The motion registry and the JS commands of the motion pipeline (R1, S4). A registry row has only the step name and the state
/// class. The element with data-motion is the target of the step, and its CSS rule owns the wait and the spec ID.
/// </summary>
public sealed class MotionRegistryTests
{
    static readonly Regex RazorStep = new("data-motion=\"(?<names>[^\"@]+)\"");
    static readonly Regex CssStep = new("\\[data-motion~=\"(?<name>[^\"]+)\"\\]");
    static readonly Regex SpecComment = new("/\\* (?<id>[A-Z]\\d+)");

    static string Repo() => Orchestrator.Store.Home.Root();

    static string Src => Path.Combine(Repo(), "src");

    /// <summary>Each JS command record of Orchestrator.Web.</summary>
    public static IEnumerable<Type> JsCommands() =>
        typeof(JsCommand).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(JsCommand)));

    [Test]
    public void Registry_matches_motion_css()
    {
        var rows = MotionSteps.All.Select(s => s.Name).ToHashSet();
        var marked = Directory.EnumerateFiles(Path.Combine(Src, "Orchestrator.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => RazorStep.Matches(File.ReadAllText(f)))
            .SelectMany(m => m.Groups["names"].Value.Split(' '))
            .ToHashSet();
        var css = new[] { "Orchestrator.Web/wwwroot/motion.css", "Orchestrator.Tray/wwwroot/tray.css" }
            .SelectMany(f => File.ReadAllLines(Path.Combine(Src, f)))
            .Where(l => CssStep.IsMatch(l))
            .ToList();
        var ruled = css.SelectMany(l => CssStep.Matches(l)).Select(m => m.Groups["name"].Value).ToHashSet();
        var design = File.ReadAllText(Path.Combine(Repo(), "docs", "motion-design.html"));
        var noSpec = css.Where(l => SpecComment.Match(l) is not { Success: true } m || !Regex.IsMatch(design, $"\\b{m.Groups["id"].Value}\\b")).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Except(marked), Is.Empty, "a registry row with no data-motion element");
            Assert.That(marked.Except(rows), Is.Empty, "a data-motion element with no registry row");
            Assert.That(rows.Except(ruled), Is.Empty, "a registry row with no step rule in the CSS");
            Assert.That(ruled.Except(rows), Is.Empty, "a step rule with no registry row");
            Assert.That(noSpec, Is.Empty, "a step rule with no spec ID of docs/motion-design.html");
        });
    }

    [Test]
    public void No_direct_js_calls()
    {
        var js = Path.Combine("Plugins", "Js");
        var hits = Directory.EnumerateFiles(Src, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".razor" or ".cs")
            .Where(f => !f.Contains(js, StringComparison.Ordinal) && File.ReadAllText(f).Contains("IJSRuntime", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.That(hits, Is.Empty, "Send a JS command record (Plugins/Js) for each C# to JS call");
    }

    [Test]
    public void Js_names_exist()
    {
        var scripts = string.Join("\n", new[] { "Orchestrator.Web/wwwroot", "Orchestrator.Tray/wwwroot" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(Src, d), "*.js"))
            .Select(File.ReadAllText));
        var missing = JsCommands().Select(JsNames.NameOf).Where(name =>
        {
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            return !scripts.Contains($"window.{name[..dot]} =", StringComparison.Ordinal)
                || !Regex.IsMatch(scripts, $"\\b{name[(dot + 1)..]}\\s*:");
        }).ToList();
        Assert.That(JsCommands().Count(), Is.GreaterThanOrEqualTo(6));
        Assert.That(missing, Is.Empty, "No JS function for these command names");
    }
}
