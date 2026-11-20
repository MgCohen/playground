using System.Text.RegularExpressions;

namespace Orchestrator.Tests;

/// <summary>
/// The rules of the motion pipeline (R1): motion.css owns each motion time (I11). The view files (Razor, the scripts, the
/// style sheets and the tray windows) have no time literal, except the entries of the allowed list, each with its reason.
/// </summary>
public sealed class MotionArchitectureTests
{
    /// <summary>A time in a CSS transition or animation declaration that is not zero.</summary>
    static readonly Regex CssMotion = new(@"\b(transition|animation)(-duration|-delay)?\s*:\s*(?<value>[^;}]*)");
    static readonly Regex CssTime = new(@"(?<![\w.-])(?<n>\d*\.\d+|\d+)m?s\b");

    /// <summary>A timer or a time limit with a number in a script or in C#.</summary>
    static readonly Regex CodeTime = new(@"setTimeout\([^;]*,\s*(?<n>\d+)\s*\)|TimeSpan\.From(Seconds|Milliseconds)\(\s*(?<n>\d+)|Task\.Delay\(\s*(?<n>\d+)|""orchTray\.end"",[^;]*,\s*(?<n>\d+)\)");

    /// <summary>Time literals that are no motion time: the file, the text of the line, and the reason.</summary>
    static readonly (string File, string Text, string Reason)[] Allowed =
    [
        ("src/Orchestrator.Web/Components/LiveView.cs", "Refresh = TimeSpan.FromSeconds(10)", "the refresh of the data, not a motion"),
        ("src/Orchestrator.Web/Components/LiveView.cs", "Task.Delay(300)", "the load delay groups the file events of the store, not a motion"),
        ("src/Orchestrator.Web/Components/Tray/TrayStage.razor", "\"orchTray.end\"", "the wait limits in C#: S3 replaces them with --t-limit"),
        ("src/Orchestrator.Web/Components/Tray/BadgeRoot.razor", "\"orchTray.end\"", "the wait limits in C#: S3 replaces them with --t-limit"),
    ];

    static string Repo() => Orchestrator.Store.Home.Root();

    /// <summary>The view files: the components, the scripts and style sheets of both hosts, and the tray windows.</summary>
    static IEnumerable<string> ViewFiles()
    {
        var src = Path.Combine(Repo(), "src");
        string[] folders = ["Orchestrator.Web/Components", "Orchestrator.Web/wwwroot", "Orchestrator.Tray/wwwroot"];
        foreach (var folder in folders)
            foreach (var path in Directory.EnumerateFiles(Path.Combine(src, folder), "*", SearchOption.AllDirectories))
                if (Path.GetExtension(path) is ".razor" or ".cs" or ".js" or ".css") yield return path;
        foreach (var name in new[] { "BadgeWindow.cs", "TrayWindow.cs", "WidgetWindow.cs", "ClearHost.cs" })
            yield return Path.Combine(src, "Orchestrator.Tray", name);
    }

    /// <summary>Each time literal of the view files: the file, the line number, and the text of the line.</summary>
    static IEnumerable<(string File, int Line, string Text)> TimeLiterals()
    {
        foreach (var path in ViewFiles())
        {
            var file = Path.GetRelativePath(Repo(), path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            var css = path.EndsWith(".css", StringComparison.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("--", StringComparison.Ordinal)) continue;
                var found = css
                    ? CssMotion.Matches(line).SelectMany(m => CssTime.Matches(m.Groups["value"].Value)).Any(t => double.Parse(t.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) > 0)
                    : CodeTime.Matches(line).Any(m => m.Groups["n"].Value != "0");
                if (found) yield return (file, i + 1, line.Trim());
            }
        }
    }

    [Test]
    public void Motion_times_are_tokens()
    {
        var bad = TimeLiterals().Where(t => !Allowed.Any(a => a.File == t.File && t.Text.Contains(a.Text, StringComparison.Ordinal))).ToList();
        Assert.That(bad, Is.Empty, "Use a token of motion.css (I11):\n" + string.Join("\n", bad.Select(b => $"{b.File}:{b.Line}: {b.Text}")));
    }

    [Test]
    public void Each_allowed_time_literal_is_still_in_use()
    {
        var all = TimeLiterals().ToList();
        var unused = Allowed.Where(a => !all.Any(t => t.File == a.File && t.Text.Contains(a.Text, StringComparison.Ordinal))).ToList();
        Assert.That(unused, Is.Empty, "Remove these entries: " + string.Join("; ", unused.Select(u => $"{u.File}: {u.Text}")));
    }

    /// <summary>The tokens that reduced motion keeps: the dwell times and the limits.</summary>
    static readonly string[] Kept = ["--t-undo", "--t-peek-show", "--t-limit", "--t-vt-start", "--t-vt-render", "--t-frame-limit", "--t-window-gap"];

    [Test]
    public void Reduced_motion_sets_each_duration_token_to_1ms()
    {
        var css = File.ReadAllText(Path.Combine(Repo(), "src/Orchestrator.Web/wwwroot/motion.css"));
        var at = css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThan(0), "motion.css has the reduced motion rule");
        var root = css[..at];
        var reduced = css[at..];
        var times = Regex.Matches(root, @"(--t-[a-z-]+)\s*:").Select(m => m.Groups[1].Value).Distinct().Where(t => !Kept.Contains(t)).ToList();
        var missing = times.Where(t => !Regex.IsMatch(reduced, Regex.Escape(t) + @"\s*:\s*1ms;")).ToList();
        Assert.That(missing, Is.Empty, "Set these duration tokens to 1ms in the reduced motion rule (I13): " + string.Join(", ", missing));
        Assert.That(reduced, Does.Not.Contain("none !important"), "reduced motion keeps each motion at 1 ms, so its end event comes");
    }

    [Test]
    public void The_scan_finds_a_time_literal()
    {
        Assert.That(CssMotion.Matches(".a { transition: left .15s; }").SelectMany(m => CssTime.Matches(m.Groups["value"].Value)).Count(), Is.EqualTo(1));
        Assert.That(CssMotion.Matches(".a { transition: left var(--t-toggle) 0s; }").SelectMany(m => CssTime.Matches(m.Groups["value"].Value))
            .Count(t => double.Parse(t.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) > 0), Is.EqualTo(0));
        Assert.That(CodeTime.IsMatch("setTimeout(end, 600);"), Is.True);
        Assert.That(CodeTime.IsMatch("Task.Delay(300).ContinueWith"), Is.True);
        Assert.That(CodeTime.IsMatch("setTimeout(end, ms(\"--t-vt-render\"));"), Is.False);
    }
}
