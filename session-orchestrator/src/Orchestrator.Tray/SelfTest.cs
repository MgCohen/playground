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

    /// <summary>The place file of the self-test: a new file in the self-test folder, so the widget starts at its home location.</summary>
    public static string FreshPlaceFile(string folder)
    {
        var file = Path.Combine(folder, "place.json");
        if (File.Exists(file)) File.Delete(file);
        return file;
    }

    public static async Task<int> Run(WidgetForm form, string folder)
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
        await web.ExecuteScriptAsync($"{Go}('')");
        await Until(() => Found(web, ".wait-block .ring-n"), TimeSpan.FromSeconds(20));
        await Task.Delay(1200);
        var scale = form.DeviceDpi / 96f;
        var size = form.Size;
        var expect = new Size((int)(WidgetForm.LogicalWidth * scale), (int)(WidgetForm.LogicalHeight * scale));
        var home = form.HomeLocation();
        checks.Add(new Check("widget", size == expect && form.Location == home && !form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None,
            $"size {size.Width} × {size.Height} (expect {expect.Width} × {expect.Height}) at {form.DeviceDpi} DPI, location {form.Location.X},{form.Location.Y} (home {home.X},{home.Y}), not in the taskbar, no title bar", ""));
        var area = Screen.FromControl(form).WorkingArea;
        var gap = area.Bottom - form.Bottom;
        var margin = (int)(12 * scale);
        var fit = form.Fit(new Point(form.Left, area.Bottom - form.Height));
        checks.Add(new Check("taskbar-gap", gap == margin && area.Bottom - (fit.Y + form.Height) == margin,
            $"gap {gap} px above the work area bottom (expect {margin}); a drop on the taskbar edge moves up to a gap of {area.Bottom - (fit.Y + form.Height)} px", ""));
        // The scroll bar width of the page: the window width less the width of the content box.
        var bar = JsonSerializer.Deserialize<double>(await web.ExecuteScriptAsync("window.innerWidth - document.documentElement.clientWidth"));
        var scrolls = JsonSerializer.Deserialize<bool>(await web.ExecuteScriptAsync("document.documentElement.scrollHeight > window.innerHeight"));
        checks.Add(new Check("no-scroll-bar", bar == 0 && scrolls, $"scroll bar width {bar} px (expect 0), the home page scrolls {scrolls}", ""));
        var corner = Path.Combine(folder, "tray-desktop-corner.png");
        checks.Add(new Check("desktop-corner", Corner(form, corner), "screenshot of the screen corner with the widget and the tray", corner));
        checks.Add(await Pin(form, web, folder));
        checks.Add(await Drag(form, web));
        checks.Add(Toggle(form));
        foreach (var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")} {c.View}: {c.Message}");
        await File.WriteAllTextAsync(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.All(c => c.Pass) ? 0 : 1;
    }

    /// <summary>The pin in the page header: a click removes the pin (TopMost off), a second click sets it again.</summary>
    static async Task<Check> Pin(WidgetForm form, CoreWebView2 web, string folder)
    {
        const string click = "document.querySelector('.hd.win .pin').click()";
        var start = form.Pinned;
        await web.ExecuteScriptAsync(click);
        var off = await Until(() => Task.FromResult(form.Pinned ? null : "off"), TimeSpan.FromSeconds(5)) is not null
            && await Found(web, ".hd.win .pin:not(.on)") is not null;
        await web.ExecuteScriptAsync(click);
        var on = await Until(() => Task.FromResult(form.Pinned ? "on" : null), TimeSpan.FromSeconds(5)) is not null
            && await Until(() => Found(web, ".hd.win .pin.on"), TimeSpan.FromSeconds(5)) is not null;
        await Task.Delay(400);
        var png = Path.Combine(folder, "tray-pin.png");
        await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        return new Check("pin", start && off && on, $"pinned at start {start}, after click 1 off {off}, after click 2 on {on}", png);
    }

    /// <summary>A real mouse drag of the header title: the widget moves, and the new place is saved.</summary>
    static async Task<Check> Drag(WidgetForm form, CoreWebView2 web)
    {
        var rect = JsonSerializer.Deserialize<double[]>(JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(
            "JSON.stringify((r => [r.left + 20, r.top + r.height / 2])(document.querySelector('.hd.win .title').getBoundingClientRect()))"))!)!;
        var scale = form.DeviceDpi / 96f;
        var client = form.PointToScreen(Point.Empty);
        var from = new Point(client.X + form.Padding.Left + (int)(rect[0] * scale), client.Y + form.Padding.Top + (int)(rect[1] * scale));
        var by = new Size(-(int)(160 * scale), -(int)(120 * scale));
        var before = form.Location;
        var back = Cursor.Position;
        // The drag loop of Windows runs on the UI thread, so the mouse input comes from another thread.
        await Task.Run(() => Mouse.Drag(from, from + by));
        await Task.Delay(500);
        Cursor.Position = back;
        var moved = form.Location == before + by;
        var saved = WidgetForm.LoadPlace(form.PlaceFile);
        return new Check("drag", moved && form.Place == form.Location && saved == form.Location,
            $"from {before.X},{before.Y} to {form.Location.X},{form.Location.Y} (expect {before.X + by.Width},{before.Y + by.Height}), saved place {form.Place}, place file {saved}", "");
    }

    /// <summary>The tray icon click: hide, then show again at the saved place.</summary>
    static Check Toggle(WidgetForm form)
    {
        var place = form.Location;
        form.Toggle();
        var hidden = !form.Visible;
        form.Toggle();
        return new Check("toggle", hidden && form.Visible && form.Location == place, $"hidden {hidden}, visible again {form.Visible} at {form.Location.X},{form.Location.Y}", "");
    }

    /// <summary>Copies the bottom right part of the screen: the widget, the margin and the taskbar with the tray.</summary>
    static bool Corner(WidgetForm form, string png)
    {
        var screen = Screen.FromControl(form).Bounds;
        var margin = (int)(40 * form.DeviceDpi / 96f);
        var left = Math.Max(screen.Left, form.Left - margin);
        var top = Math.Max(screen.Top, form.Top - margin);
        var area = Rectangle.FromLTRB(left, top, screen.Right, screen.Bottom);
        using var bitmap = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return File.Exists(png);
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
