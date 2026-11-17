using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// The self-test of the window (--selftest &lt;folder&gt;): it opens each A4 view with the mock data, examines the DOM,
/// and writes a screenshot of each view. The exit code is 0 when all checks pass.
/// </summary>
public static class SelfTest
{
    public sealed record Check(string View, bool Pass, string Message, string Screenshot);

    /// <summary>One step: a script that opens the view, then a CSS selector that must match when the view is there.</summary>
    sealed record Step(string View, string Open, string Expect);

    // Go(path) clicks a temporary link, so the Blazor router opens the view as a user click does.
    const string Go = "(p => { const a = document.createElement('a'); a.href = p; document.body.appendChild(a); a.click(); a.remove(); })";

    static readonly Step[] Steps =
    [
        new("home", $"{Go}('')", ".wait-block .ring-n"),
        new("cards", $"{Go}('cards')", ".pcard"),
        new("answer-sheet", "document.querySelector('.acts [data-act=\"left\"]').click()", ".sheet textarea"),
        new("open", $"document.querySelector('.scrim')?.click(); {Go}(document.querySelector('.card-link').getAttribute('href'))", ".doc-page"),
        new("stream", $"{Go}('stream')", ".tl"),
        new("sessions", $"{Go}('sessions')", "a.srow"),
        new("session-status", $"{Go}(([...document.querySelectorAll('a.srow')].find(a => a.textContent.includes('Plan the inbox')) ?? document.querySelector('a.srow')).getAttribute('href'))", ".hero"),
        new("session-stream", $"{Go}(document.querySelector('.sticky-acts a.pri').getAttribute('href'))", ".lock"),
    ];

    public static async Task<int> Run(MainForm form, string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<Check>();
        var web = await Until(() => Task.FromResult(form.Web), TimeSpan.FromSeconds(60)) ?? throw new TimeoutException("WebView2 did not start in 60 seconds.");
        await Until(() => Found(web, "main.page"), TimeSpan.FromSeconds(30));
        foreach (var step in Steps)
        {
            string? error = null;
            try
            {
                await web.ExecuteScriptAsync(step.Open);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            var found = await Until(() => Found(web, step.Expect), TimeSpan.FromSeconds(20)) is not null;
            // The fonts and the layout settle after the first render.
            await Task.Delay(1200);
            var png = Path.Combine(folder, $"tray-{step.View}.png");
            await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            var path = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync("location.pathname + location.search"));
            checks.Add(new Check(step.View, found, $"{path}: expect {step.Expect}{(found ? "" : " (not found)")}{(error is null ? "" : $", error {error}")}", png));
        }
        var size = form.ClientSize;
        checks.Add(new Check("size", size.Width >= 1000, $"window client size {size.Width} × {size.Height} pixels at {form.DeviceDpi} DPI", ""));
        foreach (var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")} {c.View}: {c.Message}");
        await File.WriteAllTextAsync(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.All(c => c.Pass) ? 0 : 1;
    }

    /// <summary>A non-null value when the selector matches an element of the page.</summary>
    static async Task<string?> Found(CoreWebView2 web, string selector) =>
        JsonSerializer.Deserialize<bool>(await web.ExecuteScriptAsync($"!!document.querySelector({JsonSerializer.Serialize(selector)})")) ? selector : null;

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
