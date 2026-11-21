using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using S2.Components;

namespace S2;

/// <summary>The link between the components and the window. The areas are in CSS pixels of the page.</summary>
public sealed class Shell
{
    /// <summary>The margin around the stage for the shadow, and the sizes of the panel and the ball.</summary>
    public const int Margin = 24, Width = 350, Height = 520, Ball = 56, BallPad = 16;

    public static readonly RectangleF StageArea = new(0, 0, Width + 2 * Margin, Height + 2 * Margin);
    public static readonly RectangleF BallArea = new(Margin + Width - Ball - BallPad, Margin + Height - Ball - BallPad, Ball + 2 * BallPad, Ball + 2 * BallPad);

    public event Action? Toggle;
    public Action<RectangleF> SetHits { get; set; } = _ => { };
    public void RaiseToggle() => Toggle?.Invoke();
}

/// <summary>
/// Spike S2: one transparent window. The closed form is a ball at the bottom right; a click grows the panel out of it, in CSS.
/// The clicks outside the ball (closed) go to the window below. The header drags the window.
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
        var s = form.DeviceDpi / 96f;
        var area = Screen.PrimaryScreen!.WorkingArea;
        form.Size = new Size((int)(Shell.StageArea.Width * s), (int)(Shell.StageArea.Height * s));
        form.Location = new Point(area.Right - form.Width, area.Bottom - form.Height);
        shell.SetHits = css => form.BeginInvoke(() =>
        {
            var k = form.DeviceDpi / 96f;
            form.SetHitArea([Rectangle.Round(new RectangleF(css.X * k, css.Y * k, css.Width * k, css.Height * k))]);
        });
        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S2 clear window", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) shell.RaiseToggle();
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        if (args.Length >= 2 && args[0] == "probe") form.Shown += async (_, _) => await Probe.Run(form, Path.GetFullPath(args[1]));
        Application.Run(form);
        icon.Visible = false;
    }
}

/// <summary>The automatic checks of S2. The results go to result.json and the screenshots to the folder.</summary>
static partial class Probe
{
    public static async Task Run(ClearForm form, string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object>();
        var back = new BackForm { Bounds = Rectangle.Inflate(form.Bounds, 60, 60) };
        back.Show();
        form.BringToFront();
        try
        {
            await form.Ready.Task;
            await Until(form, "document.querySelector('.stage') !== null");
            await Task.Delay(1500);
            var s = form.DeviceDpi / 96f;
            Point At(float x, float y) => new(form.Left + (int)(x * s), form.Top + (int)(y * s));
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s2-closed.png"));

            // Closed: a click on an empty part of the window goes to the window below.
            back.Clicks = 0;
            Click(At(100, 100));
            await Task.Delay(400);
            results["closedClickOnEmptyReachesBack"] = back.Clicks > 0;
            form.BringToFront();
            await Task.Delay(300);

            // A click on the ball opens the panel. The frame times during the grow.
            var frames = form.Web!.ExecuteScriptAsync("s2.frames(700).then(t => window.__f = t)");
            var ball = At(Shell.Margin + Shell.Width - Shell.Ball / 2f, Shell.Margin + Shell.Height - Shell.Ball / 2f);
            Click(ball);
            await frames;
            await Task.Delay(900);
            results["ballClickOpens"] = await form.Web.ExecuteScriptAsync("document.querySelector('.stage.open') !== null") == "true";
            var times = JsonSerializer.Deserialize<double[]>(await form.Web.ExecuteScriptAsync("window.__f")) ?? [];
            results["growFrames"] = new { count = times.Length, mean = Math.Round(times.DefaultIfEmpty().Average(), 1), longest = Math.Round(times.DefaultIfEmpty().Max(), 1), late = times.Count(t => t > 20) };
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s2-open.png"));
            // The left edge of the panel at mid height: the shadow over the stripes, then the panel.
            var edge = Shot(new Rectangle(At(0, 300).X, At(0, 300).Y, (int)(40 * s), 1), null);
            results["openLeftEdge"] = Enumerable.Range(0, edge.Width).Select(i => Hex(edge.GetPixel(i, 0))).ToList();

            // A mid-grow screenshot: close, then open and take the picture after 120 ms.
            await form.Web.ExecuteScriptAsync("document.querySelector('.close').click()");
            await Task.Delay(800);
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s2-closed-again.png"));
            back.Clicks = 0;
            Click(At(100, 100));
            await Task.Delay(400);
            results["closedAgainClickOnEmptyReachesBack"] = back.Clicks > 0;
            form.BringToFront();
            await Task.Delay(300);
            Click(ball);
            await Task.Delay(120);
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s2-mid-grow.png"));
            await Task.Delay(900);

            // The drag of the header: press, move 120 by -80 in 12 steps, release.
            var before = form.Location;
            var grip = At(Shell.Margin + 120, Shell.Margin + 20);
            SetCursorPos(grip.X, grip.Y);
            await Task.Delay(100);
            mouse_event(0x2, 0, 0, 0, 0);
            for (var i = 1; i <= 12; i++)
            {
                await Task.Delay(16);
                SetCursorPos(grip.X + 10 * i, grip.Y - (int)(6.67 * i));
            }
            await Task.Delay(50);
            mouse_event(0x4, 0, 0, 0, 0);
            await Task.Delay(300);
            results["drag"] = new { moved = new { x = form.Left - before.X, y = form.Top - before.Y }, expected = new { x = 120, y = -80 } };
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Application.Exit();
    }

    static async Task Until(ClearForm form, string test)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await form.Web!.ExecuteScriptAsync(test) == "true") return;
            await Task.Delay(100);
        }
    }

    static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    static Bitmap Shot(Rectangle area, string? file)
    {
        var b = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        if (file is not null) b.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        return b;
    }

    static void Click(Point at)
    {
        SetCursorPos(at.X, at.Y);
        Thread.Sleep(80);
        mouse_event(0x2, 0, 0, 0, 0);
        Thread.Sleep(40);
        mouse_event(0x4, 0, 0, 0, 0);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
}

/// <summary>A striped window behind the spike window. It counts the clicks that reach it.</summary>
sealed class BackForm : Form
{
    public int Clicks;

    public BackForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var w = (int)(12 * DeviceDpi / 96f);
        for (var x = 0; x < Width; x += 2 * w)
        {
            e.Graphics.FillRectangle(Brushes.MediumSeaGreen, x, 0, w, Height);
            e.Graphics.FillRectangle(Brushes.MidnightBlue, x + w, 0, w, Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Clicks++;
    }
}
