using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using S5.Components;

namespace S5;

/// <summary>The link between the components and the window. The areas are in CSS pixels of the page.</summary>
public sealed class Shell
{
    public const string Start = "start", Open = "open", Docked = "docked";

    /// <summary>The strip width: the shadow margin at the left, the panel, and the gap at the right.</summary>
    public const int Width = 24 + 350 + 12;

    /// <summary>The height of the strip (the work area), in CSS pixels. The window sets it at the start.</summary>
    public float Height { get; set; } = 1000;

    /// <summary>The panel with room for its shadow.</summary>
    public RectangleF PanelArea => new(0, Height - 12 - 520 - 24, Width, 520 + 24 + 12);

    /// <summary>The tab at its hover width, with room for its shadow.</summary>
    public RectangleF TabArea => new(Width - 250 - 20, Height / 2 - 38 - 20, 250 + 20, 76 + 40);

    public string State { get; set; } = "";
    public DateTime DockedAt { get; private set; }

    public event Action<string>? Requested;
    public Action<RectangleF[]> Hits { get; set; } = _ => { };

    public void SetHits(params RectangleF[] areas) => Hits(areas);

    public void Request(string state)
    {
        if (state == Docked) DockedAt = DateTime.UtcNow;
        Requested?.Invoke(state);
    }
}

/// <summary>
/// Spike S5: the full flow with simple shapes. A tray icon click opens or docks. A click outside the window docks it.
/// Run with "probe folder" for the automatic checks: a frame sheet of each change, the hover, the clicks, the frame times.
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
        var s = form.DeviceDpi / 96f;
        var area = Screen.PrimaryScreen!.WorkingArea;
        shell.Height = area.Height / s;
        form.Bounds = new Rectangle(area.Right - (int)(Shell.Width * s), area.Top, (int)(Shell.Width * s), area.Height);
        shell.Hits = css => form.BeginInvoke(() =>
        {
            var k = form.DeviceDpi / 96f;
            form.SetHitArea(css.Select(c => Rectangle.Round(new RectangleF(c.X * k, c.Y * k, c.Width * k, c.Height * k))).ToArray());
        });
        // A click outside the window docks the panel.
        form.Deactivate += (_, _) =>
        {
            if (shell.State == Shell.Open) shell.Request(Shell.Docked);
        };
        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S5 flow", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // The click on the icon took the focus first, and that docked the panel: no open again.
            if (shell.State == Shell.Docked && DateTime.UtcNow - shell.DockedAt > TimeSpan.FromMilliseconds(400))
            {
                shell.Request(Shell.Open);
                form.Activate();
            }
            else if (shell.State == Shell.Open) shell.Request(Shell.Docked);
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start again", null, (_, _) => shell.Request(Shell.Start));
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        if (args.Length >= 2 && args[0] == "probe") form.Shown += async (_, _) => await Probe.Run(form, shell, Path.GetFullPath(args[1]));
        Application.Run(form);
        icon.Visible = false;
    }
}

/// <summary>The automatic checks of S5. The results go to result.json and the frame sheets to the folder.</summary>
static partial class Probe
{
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
            Point At(float x, float y) => new(form.Left + (int)(x * s), form.Top + (int)(y * s));
            // The part of the strip with the panel and the tab.
            var top = (int)((shell.Height / 2 - 60) * s);
            var view = new Rectangle(form.Left, form.Top + Math.Min(top, (int)((shell.Height - 560) * s)), form.Width, form.Bottom - form.Top - Math.Min(top, (int)((shell.Height - 560) * s)));

            // 1. The start: the ball pops in, the panel grows out of it.
            results["startFrames"] = await Sheet(form, view, () => form.BeginInvoke(() => shell.Request(Shell.Start)), 1000, Path.Combine(folder, "s5-1-start.png"));
            results["openBallAtHome"] = await Js(form, "(b => (r => ({ x: r.left + r.width / 2, y: innerHeight - (r.top + r.height / 2) }))(b.getBoundingClientRect()))(document.querySelector('.ball'))");

            // 2. The ball is a button: a click swaps its color.
            var ball = At(171 + 28, shell.Height - 26 - 28);
            var before = await Js(form, "getComputedStyle(document.querySelector('.ball')).backgroundColor");
            Click(ball);
            await Task.Delay(400);
            results["ballClickColor"] = new { before, after = await Js(form, "getComputedStyle(document.querySelector('.ball')).backgroundColor") };
            Click(ball);
            await Task.Delay(300);

            // 3. Minimize: the panel folds into the ball, the ball flies to the edge, the tab slides in.
            var min = At(Shell.Width - 12 - 10 - 15, shell.Height - 12 - 520 + 25);
            results["dockFrames"] = await Sheet(form, view, () => Click(min), 1200, Path.Combine(folder, "s5-2-dock.png"));
            results["dockedState"] = await Js(form, "document.querySelector('.stage').className");
            // A click on the empty strip goes to the window below.
            var empty = At(100, shell.Height - 300);
            results["dockedEmptyIsOtherWindow"] = WindowFromPoint(new Pt(empty.X, empty.Y)) != form.Handle;

            // 4. The hover on the tab extends it; the pointer out folds it.
            var tab = At(Shell.Width - 40, shell.Height / 2);
            SetCursorPos(tab.X, tab.Y);
            await Task.Delay(500);
            (await Page(form)).Save(Path.Combine(folder, "s5-3-hover.png"));
            results["hoverTabWidth"] = await Js(form, "document.querySelector('.tab').getBoundingClientRect().width");
            SetCursorPos(tab.X - (int)(400 * s), tab.Y);
            await Task.Delay(500);
            results["afterHoverTabWidth"] = await Js(form, "document.querySelector('.tab').getBoundingClientRect().width");

            // 5. A click on the tab: the tab slides out, the ball flies back, the panel grows again.
            results["reopenFrames"] = await Sheet(form, view, () => Click(tab), 1000, Path.Combine(folder, "s5-4-reopen.png"));
            results["reopenedState"] = await Js(form, "document.querySelector('.stage').className");

            // 6. A click outside (another window takes the focus) docks the panel.
            using var other = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 10, 10), ShowInTaskbar = false };
            other.Show();
            other.Activate();
            await Task.Delay(1300);
            results["focusLossDocks"] = await Js(form, "document.querySelector('.stage').className");
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        results["skippedClicks"] = Skipped;
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Application.Exit();
    }

    /// <summary>Starts a change, then takes a picture each 100 ms; the pictures go side by side in one sheet. Returns the frame times.</summary>
    static async Task<object> Sheet(ClearForm form, Rectangle view, Action start, int ms, string file)
    {
        var frames = form.Web!.ExecuteScriptAsync($"s5.frames({ms}).then(t => window.__f = t)");
        start();
        var shots = new List<Bitmap>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var t = 0; t <= ms; t += 100)
        {
            while (clock.ElapsedMilliseconds < t) await Task.Delay(5);
            shots.Add(await Page(form));
        }
        await frames;
        await Task.Delay(100);
        const float k = .4f;
        // Only the part of the page with the shapes: from the tab to the bottom.
        var crop = new Rectangle(0, view.Top - form.Top, shots[0].Width, shots[0].Height - (view.Top - form.Top));
        int w = (int)(crop.Width * k), h = (int)(crop.Height * k);
        using var sheet = new Bitmap(w * shots.Count, h + 18);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(70, 110, 160));
            using var font = new Font("Segoe UI", 9);
            for (var i = 0; i < shots.Count; i++)
            {
                g.DrawImage(shots[i], new Rectangle(i * w, 18, w, h), crop, GraphicsUnit.Pixel);
                g.DrawString($"{i * 100} ms", font, Brushes.White, i * w + 4, 2);
            }
        }
        sheet.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        foreach (var b in shots) b.Dispose();
        var times = JsonSerializer.Deserialize<double[]>(await form.Web.ExecuteScriptAsync("window.__f")) ?? [];
        return new { count = times.Length, mean = Math.Round(times.DefaultIfEmpty().Average(), 1), longest = Math.Round(times.DefaultIfEmpty().Max(), 1), late = times.Count(t => t > 20) };
    }

    static readonly List<string> Skipped = [];

    static async Task<string> Js(ClearForm form, string script) => await form.Web!.ExecuteScriptAsync(script);

    static async Task Until(ClearForm form, string test)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await form.Web!.ExecuteScriptAsync(test) == "true") return;
            await Task.Delay(100);
        }
    }

    /// <summary>A picture of the page only (WebView2 preview), with no other window over it.</summary>
    static async Task<Bitmap> Page(ClearForm form)
    {
        var stream = new MemoryStream();
        await form.Web!.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    static Bitmap Shot(Rectangle area, string? file)
    {
        var b = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        if (file is not null) b.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        return b;
    }

    /// <summary>The spike window. A real click goes only to a point on it, never to another window on the screen.</summary>
    static nint own;

    static void Click(Point at)
    {
        var hit = WindowFromPoint(new Pt(at.X, at.Y));
        if (hit != own)
        {
            Skipped.Add($"{at.X},{at.Y}");
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
