using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// The self-test of the window (--selftest &lt;folder&gt;): it opens each view, examines the DOM and writes a screenshot of each view.
/// The exit code is 0 when all checks pass.
/// </summary>
public static class SelfTest
{
    public sealed record Check(string View, bool Pass, string Message, string Screenshot);

    static readonly (string View, string Link, string Title, Func<int[], bool> Test, string Expect)[] Views =
    [
        ("queue", "", "open-title", c => c.Length == 2 && c[0] >= 2 && c[1] >= 1, "2 or more open decisions and 1 or more defaults to review"),
        ("sessions", "sessions", "tree-title", c => c.Length == 1 && c[0] == 6, "6 sessions"),
        ("timeline", "timeline", "timeline-title", c => c.Length == 1 && c[0] > 0, "1 or more events"),
    ];

    public static async Task<int> Run(MainForm form, string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<Check>();
        var web = await Until(() => Task.FromResult(form.Web), TimeSpan.FromSeconds(60)) ?? throw new TimeoutException("WebView2 did not start in 60 seconds.");
        foreach (var (view, link, title, test, expect) in Views)
        {
            if (link.Length > 0) await web.ExecuteScriptAsync($"document.querySelector('nav a[href=\"{link}\"]').click()");
            var counts = await Until(async () => await Counts(web, title), TimeSpan.FromSeconds(30));
            // The fonts and the layout settle after the first render.
            await Task.Delay(1500);
            var png = Path.Combine(folder, $"tray-{view}.png");
            await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            var pass = counts is not null && test(counts);
            checks.Add(new Check(view, pass, $"expect {expect}, found [{string.Join(", ", counts ?? [])}]", png));
        }
        var size = form.ClientSize;
        checks.Add(new Check("size", size.Width >= 1000, $"window client size {size.Width} × {size.Height} pixels at {form.DeviceDpi} DPI", ""));
        foreach (var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")} {c.View}: {c.Message}");
        await File.WriteAllTextAsync(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.All(c => c.Pass) ? 0 : 1;
    }

    /// <summary>The numbers in the count badges of the view, or null while the view is not there.</summary>
    static async Task<int[]?> Counts(CoreWebView2 web, string titleId)
    {
        var json = await web.ExecuteScriptAsync(
            $"document.getElementById('{titleId}') ? JSON.stringify([...document.querySelectorAll('section.view .count')].map(e => parseInt(e.textContent, 10))) : null");
        return JsonSerializer.Deserialize<string?>(json) is { } list ? JsonSerializer.Deserialize<int[]>(list) : null;
    }

    static async Task<T?> Until<T>(Func<Task<T?>> probe, TimeSpan limit) where T : class
    {
        var end = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < end)
        {
            if (await probe() is { } value) return value;
            await Task.Delay(200);
        }
        return null;
    }
}
