using System.Text.RegularExpressions;

namespace Orchestrator.Tests;

/// <summary>
/// The rules of the motion pipeline (R1): motion.css owns each motion time (I11). The view files (Razor, the scripts, the
/// style sheets and the tray windows) have no time literal, except the entries of the allowed list, each with its reason.
/// </summary>
public sealed class MotionArchitectureTests
{
    /// <summary>A CSS transition or animation declaration. The value can go on more lines (#60).</summary>
    static readonly Regex CssMotion = new(@"(?<![\w-])(transition|animation)(-duration|-delay)?\s*:\s*(?<value>[^;}""'`]*)");
    static readonly Regex CssTime = new(@"(?<![\w.-])(?<n>\d*\.\d+|\d+)m?s\b");

    /// <summary>A CSS comment. The test replaces it with spaces and keeps its line breaks, so the line numbers stay correct.</summary>
    static readonly Regex CssComment = new(@"/\*.*?\*/", RegexOptions.Singleline);

    /// <summary>An inline style in markup (#60): style="…".</summary>
    static readonly Regex InlineStyle = new(@"\bstyle\s*=\s*(?<q>[""'])(?<value>.*?)\k<q>", RegexOptions.Singleline);

    /// <summary>A style value that a script sets (#60): e.style.animation = '…', e.style.transitionDelay = '…', setProperty('transition', '…').</summary>
    static readonly Regex ScriptStyle = new(@"\.style\.(?<name>transition|animation)(Duration|Delay)?\s*=\s*(?<q>[""'`])(?<value>.*?)\k<q>|setProperty\(\s*[""'](?<name>transition|animation)(-duration|-delay)?[""']\s*,\s*(?<q>[""'`])(?<value>.*?)\k<q>");

    /// <summary>A timer or a time limit with a number in a script or in C#.</summary>
    static readonly Regex CodeTime = new(@"setTimeout\([^;]*,\s*(?<n>\d+)\s*\)|TimeSpan\.From(Seconds|Milliseconds)\(\s*(?<n>\d+)|Task\.Delay\(\s*(?<n>\d+)|""orchTray\.end"",[^;]*,\s*(?<n>\d+)\)");

    /// <summary>Time literals that are no motion time: the file, the text of the line, and the reason.</summary>
    static readonly (string File, string Text, string Reason)[] Allowed =
    [
        ("src/Orchestrator.Web/Components/LiveView.cs", "Refresh = TimeSpan.FromSeconds(10)", "the refresh of the data, not a motion"),
        ("src/Orchestrator.Web/Components/LiveView.cs", "Task.Delay(300)", "the load delay groups the file events of the store, not a motion"),
    ];

    static string Repo() => Orchestrator.Store.Home.Root();

    /// <summary>The view files: the components, the scripts and style sheets of both hosts, and the tray windows.</summary>
    static IEnumerable<string> ViewFiles()
    {
        var src = Path.Combine(Repo(), "src");
        string[] folders = ["Orchestrator.Web/Components", "Orchestrator.Web/wwwroot", "Orchestrator.Tray/wwwroot"];
        foreach (var folder in folders)
            foreach (var path in Directory.EnumerateFiles(Path.Combine(src, folder), "*", SearchOption.AllDirectories))
                if (Path.GetExtension(path) is ".razor" or ".cs" or ".js" or ".css" or ".html") yield return path;
        foreach (var name in new[] { "BadgeWindow.cs", "TrayWindow.cs", "WidgetWindow.cs", "ClearHost.cs" })
            yield return Path.Combine(src, "Orchestrator.Tray", name);
    }

    /// <summary>Each time literal of the view files: the file, the line number, and the text of the line.</summary>
    static IEnumerable<(string File, int Line, string Text)> TimeLiterals()
    {
        foreach (var path in ViewFiles())
        {
            var file = Path.GetRelativePath(Repo(), path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            var lines = text.Split('\n');
            var css = path.EndsWith(".css", StringComparison.Ordinal);
            int LineOf(int index) => text[..index].Count(c => c == '\n');
            // #60: read each whole CSS declaration (it can go on more lines), and the style in markup and in scripts.
            var at = new SortedSet<int>();
            if (css)
            {
                var plain = CssComment.Replace(text, m => Regex.Replace(m.Value, @"[^\n]", " "));
                foreach (Match m in CssMotion.Matches(plain))
                    if (HasTime(m.Groups["value"].Value)) at.Add(LineOf(m.Index));
            }
            else
            {
                foreach (Match s in InlineStyle.Matches(text))
                    foreach (Match m in CssMotion.Matches(s.Groups["value"].Value))
                        if (HasTime(m.Groups["value"].Value)) at.Add(LineOf(s.Groups["value"].Index + m.Index));
                foreach (Match s in ScriptStyle.Matches(text))
                    if (HasTime(s.Groups["value"].Value)) at.Add(LineOf(s.Index));
            }
            if (!css)
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal)) continue;
                    if (CodeTime.Matches(lines[i]).Any(m => m.Groups["n"].Value != "0")) at.Add(i);
                }
            foreach (var i in at) yield return (file, i + 1, lines[i].Trim());
        }
    }

    /// <summary>True if the value of a transition or animation has a time that is not zero.</summary>
    static bool HasTime(string value) =>
        CssTime.Matches(value).Any(t => double.Parse(t.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) > 0);

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
