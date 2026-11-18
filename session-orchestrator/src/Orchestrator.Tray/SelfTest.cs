using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// The self-test of the window (--selftest &lt;folder&gt;): it opens each A4 view with the mock data, examines the DOM,
/// and writes a screenshot of each view. The exit code is 0 when all checks pass.
/// </summary>
public static partial class SelfTest
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
        var expect = form.Scaled();
        var home = form.HomeLocation();
        checks.Add(new Check("widget", size == expect && form.Location == home && !form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None,
            $"size {size.Width} × {size.Height} (expect {expect.Width} × {expect.Height}) at {form.DeviceDpi} DPI, location {form.Location.X},{form.Location.Y} (home {home.X},{home.Y}), not in the taskbar, no title bar", ""));
        var area = Screen.FromControl(form).WorkingArea;
        var gap = area.Bottom - form.Bottom;
        var margin = (int)(12 * scale);
        var fit = form.Fit(new Point(form.Left, area.Bottom - form.Height));
        checks.Add(new Check("taskbar-gap", gap == margin && area.Bottom - (fit.Y + form.Height) == margin,
            $"gap {gap} px above the work area bottom (expect {margin}); a drop on the taskbar edge moves up to a gap of {area.Bottom - (fit.Y + form.Height)} px", ""));
        // The scroll bar gutter of the page: the window width less the width of the content box.
        var bar = JsonSerializer.Deserialize<double>(await web.ExecuteScriptAsync("window.innerWidth - document.documentElement.clientWidth"));
        var scrolls = JsonSerializer.Deserialize<bool>(await web.ExecuteScriptAsync("document.documentElement.scrollHeight > window.innerHeight"));
        // S3, S4: a fixed gutter of 6 CSS px for the quiet scroll bar, so the content never moves.
        checks.Add(new Check("scroll-gutter", bar == 6 && scrolls, $"scroll bar gutter {bar} px (expect 6), the home page scrolls {scrolls}", ""));
        // The backdrop behind the app bar: a fixed layer under the bar that fades the content into the canvas.
        var backdrop = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(
            "(s => s.position + ' ' + s.zIndex + ' ' + s.height + ' ' + (s.backgroundImage.startsWith('linear-gradient') ? 'gradient' : 'none'))(getComputedStyle(document.body, '::after'))"));
        checks.Add(new Check("app-bar-backdrop", backdrop == "fixed 9 92px gradient", $"body::after {backdrop} (expect fixed 9 92px gradient)", ""));
        checks.Add(await ScrollEnd(web));
        await web.ExecuteScriptAsync($"{Go}('')");
        await Until(() => Found(web, ".wait-block .ring-n"), TimeSpan.FromSeconds(20));
        await Task.Delay(800);
        checks.AddRange(await RenderShots(form, web, folder));
        checks.AddRange(await Motion(web, folder));
        await web.ExecuteScriptAsync($"{Go}('')");
        await Until(() => Found(web, ".wait-block .ring-n"), TimeSpan.FromSeconds(20));
        await Task.Delay(800);
        var corner = Path.Combine(folder, "tray-desktop-corner.png");
        checks.Add(new Check("desktop-corner", Corner(form, corner), "screenshot of the screen corner with the widget and the tray", corner));
        checks.Add(await Pin(form, web, folder));
        checks.Add(await Drag(form, web));
        checks.Add(await Toggle(form));
        checks.Add(await PlaceClamp(form, folder));
        checks.Add(await Grow(form, folder));
        checks.Add(await Video(form, folder));
        foreach (var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")} {c.View}: {c.Message}");
        await File.WriteAllTextAsync(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.All(c => c.Pass) ? 0 : 1;
    }

    /// <summary>
    /// At full scroll, the last content of each view ends above the backdrop of the app bar and above the home button.
    /// The app bar is sticky in the page flow, so its own space is the bottom padding of the page.
    /// </summary>
    static async Task<Check> ScrollEnd(CoreWebView2 web)
    {
        var lines = new List<string>();
        var pass = true;
        foreach (var view in new[] { "", "cards", "stream", "sessions" })
        {
            await web.ExecuteScriptAsync($"{Go}('{view}')");
            await Task.Delay(1500);
            await web.ExecuteScriptAsync("window.scrollTo(0, 1e6)");
            await Task.Delay(400);
            var m = JsonSerializer.Deserialize<double[]>(await web.ExecuteScriptAsync("""
                (() => { const page = document.querySelector('.page');
                  const kids = [...page.children].filter(e => !e.matches('.nav2') && e.getBoundingClientRect().height > 0);
                  const fade = innerHeight - parseFloat(getComputedStyle(document.body, '::after').height);
                  return [Math.max(...kids.map(e => e.getBoundingClientRect().bottom)), fade, document.querySelector('.nav2 .home').getBoundingClientRect().top]; })()
                """))!;
            var ok = m[0] <= m[1] && m[0] <= m[2];
            pass &= ok;
            lines.Add($"/{view} last {m[0]:0}{(ok ? "" : " (under the bar)")}");
        }
        return new Check("scroll-end", pass, $"{string.Join(", ", lines)}; each must end above the backdrop and the home button", "");
    }

    /// <summary>
    /// R2: the full document (Open) and the preview (the card, or the stream row for a notice) of the question,
    /// report and notice kinds, from their view.json. Each check takes a screenshot of the desktop corner.
    /// </summary>
    static async Task<IEnumerable<Check>> RenderShots(WidgetForm form, CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();
        async Task Shot(string view, string open, string expect)
        {
            await web.ExecuteScriptAsync(open);
            var found = await Until(() => Found(web, expect), TimeSpan.FromSeconds(20)) is not null;
            await Task.Delay(1200);
            var png = Path.Combine(folder, $"tray-corner-{view}.png");
            checks.Add(new Check(view, found && Corner(form, png), $"expect {expect}{(found ? "" : " (not found)")}", png));
        }
        // The stream row of the first envelope of a kind; for a report, the one with links.
        static string Row(string kind, string has = "") => $"[...document.querySelectorAll('.tli[data-kind=\"{kind}\"]')].find(r => r.textContent.includes('{has}'))";
        foreach (var (kind, has, expect) in new[] { ("question", "filters", ".blk.chips"), ("report", "typed shape", ".blk.links"), ("notice", "", ".facts [data-field=kind]") })
        {
            await web.ExecuteScriptAsync($"{Go}('stream')");
            await Until(() => Found(web, $".tli[data-kind={kind}]"), TimeSpan.FromSeconds(20));
            if (kind == "notice")
            {
                await Shot("row-notice", $"({Row(kind)}).scrollIntoView({{block: 'center'}})", ".tli[data-kind=notice] .t2");
            }
            await Shot($"open-{kind}", $"{Go}(({Row(kind, has)}).querySelector('a.ev').getAttribute('href'))", $".doc-page {expect}");
        }
        await web.ExecuteScriptAsync($"{Go}('cards')");
        await Until(() => Found(web, ".pcard"), TimeSpan.FromSeconds(20));
        await Shot("card-question", "window.scrollTo(0, 0)", ".pcard .chips .chip");
        await Shot("card-report", "[...document.querySelectorAll('.seg button')].find(b => b.textContent.startsWith('Reports')).click()", ".pcard .pill");
        return checks;
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

    /// <summary>
    /// Two real mouse drags of the header title (up left, then back): the widget lands exactly on each target, and the new place is saved.
    /// </summary>
    static async Task<Check> Drag(WidgetForm form, CoreWebView2 web)
    {
        var rect = JsonSerializer.Deserialize<double[]>(JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(
            "JSON.stringify((r => [r.left + 20, r.top + r.height / 2])(document.querySelector('.hd.win .title').getBoundingClientRect()))"))!)!;
        var scale = form.DeviceDpi / 96f;
        var by = new Size(-(int)(160 * scale), -(int)(120 * scale));
        var back = Cursor.Position;
        var lines = new List<string>();
        var pass = true;
        foreach (var move in new[] { by, new Size(-by.Width, -by.Height) })
        {
            var client = form.PointToScreen(Point.Empty);
            var from = new Point(client.X + (int)(rect[0] * scale), client.Y + (int)(rect[1] * scale));
            var before = form.Location;
            // The drag loop runs on the UI thread, so the mouse input comes from another thread.
            await Task.Run(() => Mouse.Drag(from, from + move));
            for (var i = 0; i < 50 && form.Dragging; i++) await Task.Delay(20);
            await Task.Delay(200);
            var target = before + move;
            var saved = WidgetForm.LoadPlace(form.PlaceFile);
            var ok = form.Location == target && form.Place == form.Location && saved == form.Location;
            pass &= ok;
            lines.Add($"from {before.X},{before.Y} to {form.Location.X},{form.Location.Y} (expect {target.X},{target.Y}, off by {form.Location.X - target.X},{form.Location.Y - target.Y}), place file {saved}");
        }
        Cursor.Position = back;
        return new Check("drag", pass, string.Join("; ", lines), "");
    }

    /// <summary>
    /// A saved place on a screen that is gone (right of all screens, as X 2001 on a 1707 px screen) comes back into a current work area.
    /// </summary>
    static async Task<Check> PlaceClamp(WidgetForm form, string folder)
    {
        var keep = form.Place;
        var right = Screen.AllScreens.Max(s => s.Bounds.Right);
        var gone = new Point(right + 300, form.Top);
        form.HideWidget();
        await Settle(form);
        form.UsePlace(gone);
        form.ShowWidget();
        await Settle(form);
        await Task.Delay(300);
        var bounds = form.Bounds;
        var inside = Screen.AllScreens.Any(s => s.WorkingArea.Contains(bounds));
        var png = Path.Combine(folder, "tray-e2-place-clamp.png");
        Corner(form, png);
        var text = $"saved place {gone.X},{gone.Y} (right edge of all screens {right}); shown at {bounds.X},{bounds.Y} size {bounds.Width} × {bounds.Height}, inside a work area {inside}";
        if (keep is { } k)
        {
            form.HideWidget();
            await Settle(form);
            form.UsePlace(k);
            form.ShowWidget();
            await Settle(form);
        }
        return new Check("place-clamp", inside, text, png);
    }

    /// <summary>The tray icon click: hide, then show again at the saved place. Each change waits for the shrink or the grow.</summary>
    static async Task<Check> Toggle(WidgetForm form)
    {
        var place = form.Location;
        form.Toggle();
        var shrinking = form.Visible && form.Growing;
        await Settle(form);
        var hidden = !form.Visible;
        var shrink = Frames(form.GrowFrames);
        var shrinkCount = form.GrowFrames.Count;
        form.Toggle();
        await Settle(form);
        return new Check("toggle", shrinking && hidden && form.Visible && form.Location == place && shrink.Down,
            $"shrinks first {shrinking}, hidden {hidden} after {shrinkCount} frames ({shrink.Text}), visible again {form.Visible} at {form.Location.X},{form.Location.Y}", "");
    }

    /// <summary>
    /// The widget grows from height 0 at its bottom edge to its full height. The check hides the widget, shows it again,
    /// and copies the screen corner at three times of the grow. The shown height must go up on each frame and end at the full height.
    /// </summary>
    static async Task<Check> Grow(WidgetForm form, string folder)
    {
        form.HideWidget();
        await Settle(form);
        form.ShowWidget();
        var pngs = new List<string>();
        foreach (var at in new[] { 60, 140 })
        {
            await Task.Delay(at - (pngs.Count == 0 ? 0 : 60));
            var png = Path.Combine(folder, $"tray-grow-{pngs.Count + 1}.png");
            Corner(form, png);
            pngs.Add(png);
        }
        await Settle(form);
        var last = Path.Combine(folder, "tray-grow-3.png");
        Corner(form, last);
        pngs.Add(last);
        var grow = Frames(form.GrowFrames);
        var full = form.GrowFrames.Count > 0 && form.GrowFrames[^1] == form.Height;
        var small = form.GrowFrames.Count > 0 && form.GrowFrames[0] < form.Height / 4;
        return new Check("grow", grow.Up && full && small && form.GrowFrames.Count >= 5,
            $"{form.GrowFrames.Count} frames in {WidgetForm.GrowMs} ms, {grow.Text}; starts small {small}, ends at the full height {full}", string.Join(";", pngs));
    }

    /// <summary>
    /// Records one shrink and one grow as JPEG frames of the screen corner, in grow-video\ with times.json.
    /// A background thread copies the screen, so the UI thread and the motion run as usual.
    /// </summary>
    static async Task<Check> Video(WidgetForm form, string folder)
    {
        var dir = Path.Combine(folder, "grow-video");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var screen = Screen.FromControl(form).Bounds;
        var margin = (int)(40 * form.DeviceDpi / 96f);
        var area = Rectangle.FromLTRB(Math.Max(screen.Left, form.Left - margin), Math.Max(screen.Top, form.Top - margin), Math.Min(screen.Right, form.Right + margin), Math.Min(screen.Bottom, form.Bottom + margin));
        var frames = new List<(long Ms, Bitmap Image)>();
        using var stop = new CancellationTokenSource();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var recorder = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var bitmap = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                frames.Add((clock.ElapsedMilliseconds, bitmap));
            }
        });
        await Task.Delay(400);
        form.HideWidget();
        await Settle(form);
        await Task.Delay(500);
        form.ShowWidget();
        await Settle(form);
        await Task.Delay(600);
        await stop.CancelAsync();
        await recorder;
        var jpeg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var quality = new System.Drawing.Imaging.EncoderParameters(1);
        quality.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
        for (var i = 0; i < frames.Count; i++)
        {
            using var half = new Bitmap(frames[i].Image, area.Width / 2, area.Height / 2);
            half.Save(Path.Combine(dir, $"f{i:D3}.jpg"), jpeg, quality);
            frames[i].Image.Dispose();
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "times.json"), JsonSerializer.Serialize(frames.Select(f => f.Ms)));
        var fps = frames.Count / (clock.ElapsedMilliseconds / 1000.0);
        return new Check("video", frames.Count > 30, $"{frames.Count} frames at {fps:F0} frames a second", dir);
    }

    static async Task Settle(WidgetForm form)
    {
        for (var i = 0; i < 100 && form.Growing; i++) await Task.Delay(20);
    }

    static (bool Up, bool Down, string Text) Frames(IReadOnlyList<int> frames)
    {
        var up = frames.Count > 1 && frames.Zip(frames.Skip(1)).All(p => p.Second >= p.First);
        var down = frames.Count > 1 && frames.Zip(frames.Skip(1)).All(p => p.Second <= p.First);
        var text = frames.Count == 0 ? "no frames" : $"heights {string.Join(" ", frames)}";
        return (up, down, text);
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
