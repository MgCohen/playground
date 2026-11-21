using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using S4.Components;

namespace S4;

/// <summary>The counts of the waiting cards by type.</summary>
public sealed record Counts(int Questions, int Reviews, int Reports, int Blocked)
{
    public int Total => Questions + Reviews + Reports + Blocked;
}

/// <summary>The link between the components and the window. The areas are in CSS pixels of the page.</summary>
public sealed class Shell
{
    /// <summary>The margin around the stage for the shadow, the sizes of the panel and the ball, and the room of the pill and the peek.</summary>
    public const int Margin = 24, Width = 350, Height = 520, Ball = 56, PillWidth = 250, PeekRoom = 130, Pad = 16;

    public static readonly RectangleF StageArea = new(0, 0, Width + 2 * Margin, Height + 2 * Margin);

    /// <summary>The closed form: the ball, the hover pill to its left and the peek bubble above it, with room for the shadow.</summary>
    public static readonly RectangleF CornerArea = new(Margin + Width - PillWidth - Pad, Margin + Height - PeekRoom - Pad, PillWidth + Pad + Margin, PeekRoom + Pad + Margin);

    public Counts Counts { get; private set; } = new(2, 1, 0, 0);

    public event Action? Toggled;
    public event Action<string, string>? CardAdded;
    public Action<RectangleF> SetHits { get; set; } = _ => { };

    public void Toggle() => Toggled?.Invoke();

    public void AddCard(string title, string sub)
    {
        Counts = Counts with { Questions = Counts.Questions + 1 };
        CardAdded?.Invoke(title, sub);
    }
}

/// <summary>
/// Spike S4: the closed form at the corner. The home ball is one element: closed, it sits at the corner with the count ring;
/// a hover shows the counts; a new card spins the ring and shows a peek bubble. A click grows the panel out of the ball, and
/// the ball moves into the app bar as the home button. Run with "probe folder" for the automatic checks.
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
        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S4 corner window", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) shell.Toggle();
        };
        var menu = new ContextMenuStrip();
        var n = 0;
        menu.Items.Add("Add a card", null, (_, _) => shell.AddCard($"Question · Session {++n}", "Which store layout do we keep?"));
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        if (args.Length >= 2 && args[0] == "probe") form.Shown += async (_, _) => await Probe.Run(form, shell, Path.GetFullPath(args[1]));
        Application.Run(form);
        icon.Visible = false;
    }
}

/// <summary>The automatic checks of S4. The results go to result.json and the screenshots to the folder.</summary>
static partial class Probe
{
    public static async Task Run(ClearForm form, Shell shell, string folder)
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
            var corner = Rectangle.Inflate(new Rectangle(At(Shell.CornerArea.X, Shell.CornerArea.Y), new Size((int)(Shell.CornerArea.Width * s), (int)(Shell.CornerArea.Height * s))), 20, 20);
            var ball = At(Shell.Margin + Shell.Width - Shell.Ball / 2f, Shell.Margin + Shell.Height - Shell.Ball / 2f);
            Shot(corner, Path.Combine(folder, "s4-closed.png"));

            // Closed: a click on an empty part of the window goes to the window below.
            back.Clicks = 0;
            Click(At(100, 100));
            await Task.Delay(400);
            results["closedClickOnEmptyReachesBack"] = back.Clicks > 0;
            form.BringToFront();
            await Task.Delay(300);

            // The hover on the ball shows the pill with the counts.
            SetCursorPos(ball.X, ball.Y);
            await Task.Delay(700);
            Shot(corner, Path.Combine(folder, "s4-hover.png"));
            results["hoverPillWidth"] = await form.Web!.ExecuteScriptAsync("document.querySelector('.pill').getBoundingClientRect().width");
            SetCursorPos(ball.X - (int)(300 * s), ball.Y - (int)(300 * s));
            await Task.Delay(500);
            results["afterHoverPillWidth"] = await form.Web.ExecuteScriptAsync("document.querySelector('.pill').getBoundingClientRect().width");

            // A new card: the ring spins and the peek bubble shows.
            form.BeginInvoke(() => shell.AddCard("Question · Session 7", "Which store layout do we keep?"));
            await Task.Delay(450);
            Shot(corner, Path.Combine(folder, "s4-peek.png"));
            results["peekShows"] = await form.Web.ExecuteScriptAsync("document.querySelector('.peek.on') !== null");
            await Task.Delay(5200);
            results["peekHidesAfter5s"] = await form.Web.ExecuteScriptAsync("document.querySelector('.peek.on') === null");

            // A click on the ball opens the panel; the ball moves to the middle of the app bar.
            var frames = form.Web.ExecuteScriptAsync("s4.frames(700).then(t => window.__f = t)");
            Click(ball);
            await Task.Delay(150);
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s4-mid-grow.png"));
            await frames;
            await Task.Delay(700);
            var times = JsonSerializer.Deserialize<double[]>(await form.Web.ExecuteScriptAsync("window.__f")) ?? [];
            results["growFrames"] = new { count = times.Length, mean = Math.Round(times.DefaultIfEmpty().Average(), 1), longest = Math.Round(times.DefaultIfEmpty().Max(), 1), late = times.Count(t => t > 20) };
            results["homeBallVersusBarMiddle"] = await form.Web.ExecuteScriptAsync(
                "(h => (g => ({ ballX: h.left + h.width / 2, ballY: h.top + h.height / 2, gapX: g.left + g.width / 2, gapY: g.top + g.height / 2 }))(document.querySelector('.bar .gap').getBoundingClientRect()))(document.querySelector('.home').getBoundingClientRect())");
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s4-open.png"));

            // The home ball closes the panel again; then a click on an empty part goes to the window below again.
            var home = At(Shell.Margin + Shell.Width / 2f, Shell.Margin + Shell.Height - 42);
            Click(home);
            await Task.Delay(150);
            Shot(Rectangle.Inflate(form.Bounds, 20, 20), Path.Combine(folder, "s4-mid-close.png"));
            await Task.Delay(700);
            results["homeClickCloses"] = await form.Web.ExecuteScriptAsync("document.querySelector('.stage.open') === null");
            back.Clicks = 0;
            Click(At(100, 100));
            await Task.Delay(400);
            results["closedAgainClickOnEmptyReachesBack"] = back.Clicks > 0;
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
