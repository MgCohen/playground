using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using S6.Components;

namespace S6;

/// <summary>The link between the flow component and the window.</summary>
public sealed class Shell
{
    public const string Start = "start", Open = "open", Dock = "dock", Badge = "badge";
    public const string PanelPlace = "panel", BadgePlace = "badge";

    public string Phase { get; set; } = Start;
    public DateTime DockedAt { get; private set; }

    public event Action<string>? Requested;

    /// <summary>Moves the window to a place while nothing shows. The task ends when the window is there.</summary>
    public Func<string, Task> Place { get; set; } = _ => Task.CompletedTask;

    public void Request(string what)
    {
        if (what == Dock) DockedAt = DateTime.UtcNow;
        Requested?.Invoke(what);
    }
}

/// <summary>
/// The two places of the window, each with its own saved value. The panel place is a free point (a drag of the header).
/// The badge place is the right edge of the work area of the panel screen, at a saved height (a drag up and down).
/// </summary>
sealed class Places(ClearForm form)
{
    // CSS pixels: the margin for the shadow, the panel, and the badge at its hover width.
    const int Margin = 24, PanelW = 350, PanelH = 520, BadgeW = 250, BadgeH = 76, BadgeRest = 100;

    float S => form.DeviceDpi / 96f;
    Point? panel;
    int? badgeTop;

    public bool IsBadge { get; private set; }

    Screen PanelScreen => panel is { } p ? Screen.FromPoint(p) : Screen.PrimaryScreen!;

    public Rectangle Panel()
    {
        var size = new Size((int)((PanelW + 2 * Margin) * S), (int)((PanelH + 2 * Margin) * S));
        var area = PanelScreen.WorkingArea;
        var p = panel ?? new Point(area.Right - size.Width, area.Bottom - size.Height);
        // A place off the work area (a monitor that is gone, a drag to the edge) comes back onto it.
        p = new Point(Math.Clamp(p.X, area.Left - (int)(Margin * S), area.Right - size.Width + (int)(Margin * S)),
            Math.Clamp(p.Y, area.Top - (int)(Margin * S), area.Bottom - size.Height + (int)(Margin * S)));
        return new Rectangle(p, size);
    }

    public Rectangle Badge()
    {
        var size = new Size((int)((BadgeW + Margin) * S), (int)((BadgeH + 2 * Margin) * S));
        var area = PanelScreen.WorkingArea;
        var top = Math.Clamp(badgeTop ?? area.Top + area.Height / 2 - size.Height / 2, area.Top, area.Bottom - size.Height);
        return new Rectangle(area.Right - size.Width, top, size.Width, size.Height);
    }

    /// <summary>The click area of the badge: at rest only the right part, at hover the full width.</summary>
    public Rectangle BadgeHits(bool wide) =>
        wide ? new Rectangle(Point.Empty, form.Size) : new Rectangle(form.Width - (int)((BadgeRest + 16) * S), 0, (int)((BadgeRest + 16) * S), form.Height);

    public void Go(string place)
    {
        IsBadge = place == Shell.BadgePlace;
        form.BadgeMode = IsBadge;
        form.Bounds = IsBadge ? Badge() : Panel();
        form.SetHitArea([IsBadge ? BadgeHits(false) : new Rectangle(Point.Empty, form.Size)]);
    }

    public void SavePanel() => panel = form.Location;

    public void SaveBadge() => badgeTop = form.Top;
}

/// <summary>
/// Spike S6: the full flow with two separate places. A tray icon click opens or docks. A click outside the panel docks it.
/// Run with "probe folder" for the automatic checks.
/// </summary>
static partial class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var shell = new Shell();
        var services = new ServiceCollection();
        services.AddBlazorWebView();
        services.AddSingleton(shell);
        using var provider = services.BuildServiceProvider();
        var form = new ClearForm(provider, typeof(Main));
        var places = new Places(form);
        places.Go(Shell.PanelPlace);
        shell.Place = place =>
        {
            var done = new TaskCompletionSource();
            form.BeginInvoke(async () =>
            {
                places.Go(place);
                // One composition of the screen at the new place before the next step.
                await Task.Delay(16);
                done.SetResult();
            });
            return done.Task;
        };
        form.PanelMoved += places.SavePanel;
        form.BadgeMoved += places.SaveBadge;
        form.BadgeClicked += () => shell.Request(Shell.Open);
        var hoverRun = 0;
        form.BadgeHover += on =>
        {
            var mine = ++hoverRun;
            if (on) form.SetHitArea([places.BadgeHits(true)]);
            // The fold of the hover ends before the click area gets small again.
            else _ = Task.Delay(260).ContinueWith(_ => form.BeginInvoke(() =>
            {
                if (mine == hoverRun && places.IsBadge) form.SetHitArea([places.BadgeHits(false)]);
            }));
        };
        // A click outside the panel docks it.
        form.Deactivate += (_, _) =>
        {
            if (shell.Phase == Shell.Open) shell.Request(Shell.Dock);
        };
        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S6 places", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (shell.Phase == Shell.Open) shell.Request(Shell.Dock);
            // The click on the icon took the focus first, and that docked the panel: no open again.
            else if (DateTime.UtcNow - shell.DockedAt > TimeSpan.FromMilliseconds(400))
            {
                shell.Request(Shell.Open);
                form.Activate();
            }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start again", null, (_, _) => shell.Request(Shell.Start));
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        if (args.Length >= 2 && args[0] == "measure") form.Shown += async (_, _) => await Probe.Measure(form, shell, Path.GetFullPath(args[1]));
        if (args.Length >= 2 && args[0] == "probe") form.Shown += async (_, _) => await Probe.Run(form, shell, Path.GetFullPath(args[1]));
        Application.Run(form);
        icon.Visible = false;
    }
}

/// <summary>The automatic checks of S6. The results go to result.json and the pictures to the folder.</summary>
static partial class Probe
{
    static nint own;
    static readonly List<string> Skipped = [];

    /// <summary>The measure mode: no mouse. The sizes of the window, the WebView and the page in each place.</summary>
    public static async Task Measure(ClearForm form, Shell shell, string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object>();
        const string page = "JSON.stringify({ inner: [innerWidth, innerHeight], dpr: devicePixelRatio, html: [document.documentElement.clientWidth, document.documentElement.clientHeight], stage: (r => r && [r.left, r.top, r.width, r.height])(document.querySelector('.stage')?.getBoundingClientRect()), badge: (r => r && [r.left, r.top, r.width, r.height])(document.querySelector('.badge')?.getBoundingClientRect()) })";
        try
        {
            await form.Ready.Task;
            await Until(form, "document.querySelector('.stage.open') !== null");
            await Task.Delay(1000);
            results["panel"] = new { host = form.Sizes(), page = await form.Web!.ExecuteScriptAsync(page) };
            form.BeginInvoke(() => shell.Request(Shell.Dock));
            await Task.Delay(1500);
            results["badge"] = new { host = form.Sizes(), page = await form.Web.ExecuteScriptAsync(page) };
            await Task.Delay(1500);
            results["badge later"] = new { host = form.Sizes(), page = await form.Web.ExecuteScriptAsync(page) };
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        File.WriteAllText(Path.Combine(folder, "measure.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Application.Exit();
    }

    public static async Task Run(ClearForm form, Shell shell, string folder)
    {
        Directory.CreateDirectory(folder);
        own = form.Handle;
        var results = new Dictionary<string, object>();
        try
        {
            await form.Ready.Task;
            await Until(form, "document.querySelector('.stage.open') !== null");
            await Task.Delay(1200);
            var s = form.DeviceDpi / 96f;
            var area = Screen.FromHandle(form.Handle).WorkingArea;
            Point At(float x, float y) => new(form.Left + (int)(x * s), form.Top + (int)(y * s));
            results["1 start"] = new { panel = form.Bounds.ToString() };

            // 2. Drag the panel header to a new place.
            results["2 region at header"] = form.RegionAt(new Point((int)(144 * s), (int)(44 * s)));
            var before = form.Location;
            await Drag(At(24 + 120, 24 + 20), -400, -250);
            results["2 panel drag"] = new { moved = new { x = form.Left - before.X, y = form.Top - before.Y }, expected = new { x = -400, y = -250 } };
            var panelPlace = form.Bounds;

            // 3. Minimize: fold, ball out, then the badge at the right edge (not at the panel place).
            var frames = form.Web!.ExecuteScriptAsync("s6.meter(1100).then(t => window.__f = t)");
            var sheet = new List<(int, Bitmap, string)>();
            Click(At(24 + 350 - 25, 24 + 25));
            await Record(form, sheet, 1100);
            await frames;
            Save(sheet, Path.Combine(folder, "s6-1-dock.png"));
            results["3 dock"] = new { phase = shell.Phase, window = form.Bounds.ToString(), rightEdge = form.Right == area.Right, frames = Stats(await Times(form)) };

            // 4. Hover the badge: the white block and the ring go left together; the pointer out brings them back.
            var ringAtRest = await form.Web.ExecuteScriptAsync("document.querySelector('.ring').getBoundingClientRect().left");
            var badge = new Point(form.Right - (int)(50 * s), form.Top + (int)((24 + 38) * s));
            SetCursorPos(badge.X, badge.Y);
            await Task.Delay(500);
            (await Page(form)).Save(Path.Combine(folder, "s6-2-hover.png"));
            var ringAtHover = await form.Web.ExecuteScriptAsync("document.querySelector('.ring').getBoundingClientRect().left");
            var widthAtHover = await form.Web.ExecuteScriptAsync("document.querySelector('.badge').getBoundingClientRect().width");
            SetCursorPos(badge.X - (int)(500 * s), badge.Y);
            await Task.Delay(500);
            results["4 hover steps"] = string.Join(",", form.Steps);
            results["4 hover"] = new { ringAtRest, ringAtHover, widthAtHover, widthAfter = await form.Web.ExecuteScriptAsync("document.querySelector('.badge').getBoundingClientRect().width") };

            // 5. Drag the badge: up and down only; it stays on the right edge.
            var top = form.Top;
            await Drag(badge, -300, -200);
            results["5 badge drag"] = new { movedY = form.Top - top, expectedY = -200, rightEdge = form.Right == area.Right, phase = shell.Phase };
            var badgePlace = form.Bounds;

            // 6. Click the badge: it slides out, the window goes to the panel place, the ball pops in, the panel grows.
            frames = form.Web.ExecuteScriptAsync("s6.meter(1100).then(t => window.__f = t)");
            sheet.Clear();
            Click(new Point(form.Right - (int)(50 * s), form.Top + (int)((24 + 38) * s)));
            await Record(form, sheet, 1100);
            await frames;
            Save(sheet, Path.Combine(folder, "s6-3-open.png"));
            results["6 open"] = new { phase = shell.Phase, atPanelPlace = form.Bounds == panelPlace, frames = Stats(await Times(form)) };

            // 7. Dock again: the badge comes back at its own place.
            Click(At(24 + 350 - 25, 24 + 25));
            await Task.Delay(1300);
            results["7 dock again"] = new { phase = shell.Phase, atBadgePlace = form.Bounds == badgePlace };

            // 8. Open from the tray, then a click outside docks.
            form.BeginInvoke(() => shell.Request(Shell.Open));
            await Task.Delay(1200);
            using var other = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 10, 10), ShowInTaskbar = false };
            other.Show();
            other.Activate();
            await Task.Delay(1400);
            results["8 focus loss"] = new { phase = shell.Phase, atBadgePlace = form.Bounds == badgePlace };
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        results["skippedClicks"] = Skipped;
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Application.Exit();
    }

    /// <summary>Pictures of the page each 100 ms, with the window place.</summary>
    static async Task Record(ClearForm form, List<(int, Bitmap, string)> sheet, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var t = 0; t <= ms; t += 100)
        {
            while (clock.ElapsedMilliseconds < t) await Task.Delay(5);
            sheet.Add((t, await Page(form), form.BadgeMode ? "badge place" : "panel place"));
        }
    }

    static void Save(List<(int T, Bitmap B, string Place)> sheet, string file)
    {
        const float k = .45f;
        var h = (int)(sheet.Max(x => x.B.Height) * k);
        var widths = sheet.Select(x => (int)(x.B.Width * k)).ToList();
        using var all = new Bitmap(widths.Sum() + 6 * sheet.Count, h + 34);
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.FromArgb(70, 110, 160));
            using var font = new Font("Segoe UI", 9);
            var x = 0;
            for (var i = 0; i < sheet.Count; i++)
            {
                g.DrawImage(sheet[i].B, x, 34, widths[i], (int)(sheet[i].B.Height * k));
                g.DrawString($"{sheet[i].T} ms", font, Brushes.White, x + 2, 1);
                g.DrawString(sheet[i].Place, font, Brushes.White, x + 2, 16);
                x += widths[i] + 6;
            }
        }
        all.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    static async Task<double[]> Times(ClearForm form) => JsonSerializer.Deserialize<double[]>(await form.Web!.ExecuteScriptAsync("window.__f")) ?? [];

    static object Stats(double[] t) => new { count = t.Length, longest = Math.Round(t.DefaultIfEmpty().Max(), 1), late = t.Count(x => x > 20) };

    static async Task<Bitmap> Page(ClearForm form)
    {
        var stream = new MemoryStream();
        await form.Web!.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    static async Task Until(ClearForm form, string test)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await form.Web!.ExecuteScriptAsync(test) == "true") return;
            await Task.Delay(100);
        }
    }

    /// <summary>A press, a move in 12 steps, a release. Only on the spike window.</summary>
    static async Task Drag(Point from, int dx, int dy)
    {
        if (WindowFromPoint(new Pt(from.X, from.Y)) != own)
        {
            Skipped.Add($"drag {from.X},{from.Y}");
            return;
        }
        SetCursorPos(from.X, from.Y);
        await Task.Delay(100);
        mouse_event(0x2, 0, 0, 0, 0);
        for (var i = 1; i <= 12; i++)
        {
            await Task.Delay(16);
            SetCursorPos(from.X + dx * i / 12, from.Y + dy * i / 12);
        }
        await Task.Delay(50);
        mouse_event(0x4, 0, 0, 0, 0);
        await Task.Delay(300);
    }

    /// <summary>A click, only on the spike window.</summary>
    static void Click(Point at)
    {
        if (WindowFromPoint(new Pt(at.X, at.Y)) != own)
        {
            Skipped.Add($"click {at.X},{at.Y}");
            return;
        }
        SetCursorPos(at.X, at.Y);
        Thread.Sleep(60);
        mouse_event(0x2, 0, 0, 0, 0);
        Thread.Sleep(30);
        mouse_event(0x4, 0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Pt(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Pt point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
}
