using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using S1.Components;

namespace S1;

/// <summary>The link between the components and the window: the window says "opened", the components ask for "hide".</summary>
public sealed class Shell
{
    public event Action? Opened;
    public Action Hide { get; set; } = () => { };
    public void RaiseOpened() => Opened?.Invoke();
}

/// <summary>
/// Spike S1: one plain window. The system draws the round corners and the shadow. The page moves its content at open and
/// close. The header drags the window with app-region. A tray icon click shows or hides the window.
/// Run with "probe folder" for the automatic checks: screenshots, frame times, drag, and the Opacity fade.
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
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(shell);
        using var provider = services.BuildServiceProvider();
        var form = new PlainForm(provider, shell, fade: args.Contains("fade"));
        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S1 plain window", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) form.Toggle();
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        if (args.Length >= 2 && args[0] == "probe") form.Shown += async (_, _) => await Probe.Run(form, Path.GetFullPath(args[1]));
        form.Open();
        Application.Run(form);
        icon.Visible = false;
    }
}

sealed partial class PlainForm : Form
{
    readonly Shell shell;
    readonly bool fade;
    readonly System.Windows.Forms.Timer fader = new() { Interval = 10 };
    readonly System.Diagnostics.Stopwatch clock = new();
    public readonly BlazorWebView View;
    public readonly TaskCompletionSource Ready = new();

    public PlainForm(IServiceProvider services, Shell shell, bool fade)
    {
        this.shell = shell;
        this.fade = fade;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "S1 plain window";
        BackColor = Color.FromArgb(0xe6, 0xe3, 0xde);
        var s = DeviceDpi / 96f;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size((int)(350 * s), (int)(520 * s));
        Location = new Point(area.Right - Width - (int)(12 * s), area.Bottom - Height - (int)(12 * s));
        View = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        View.RootComponents.Add<Main>("#app");
        View.BlazorWebViewInitialized += (_, e) =>
        {
            // app-region: drag in the page gives the system move loop. The setting must be on before the first navigation.
            e.WebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            e.WebView.CoreWebView2.NavigationCompleted += (_, _) => Ready.TrySetResult();
        };
        Controls.Add(View);
        shell.Hide = () => BeginInvoke(Hide);
        fader.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 150);
            Opacity = t;
            if (t >= 1) fader.Stop();
        };
    }

    public void Open()
    {
        if (fade)
        {
            Opacity = 0;
            clock.Restart();
            fader.Start();
        }
        Show();
        Activate();
        shell.RaiseOpened();
    }

    public void Toggle()
    {
        if (Visible) shell.Hide();
        else Open();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The system round corners. On Windows 11 a window with round corners also gets the system shadow.
        var round = 2;
        _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

/// <summary>The automatic checks of S1. The results go to result.json and the screenshots to the folder.</summary>
static partial class Probe
{
    public static async Task Run(PlainForm form, string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object>();
        // A white window behind the widget, so the screenshots show the shadow.
        var back = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, BackColor = Color.White, ShowInTaskbar = false };
        back.Bounds = Rectangle.Inflate(form.Bounds, 80, 80);
        back.Show();
        form.BringToFront();
        try
        {
            await form.Ready.Task;
            await Until(form, "document.querySelector('.win') !== null");
            await Task.Delay(1200);
            var s = form.DeviceDpi / 96f;
            var m = (int)(24 * s);
            Shot(Rectangle.Inflate(form.Bounds, m, m), Path.Combine(folder, "s1-rest.png"));
            var corner = Shot(new Rectangle(form.Left - m, form.Top - m, 2 * m, 2 * m), null);
            results["cornerAndShadow"] = Enumerable.Range(0, 2 * m).Select(i => Hex(corner.GetPixel(i, i))).ToList();

            // The frame times during the open: hide, show, measure 700 ms.
            form.Hide();
            await Task.Delay(400);
            var frames = form.View.WebView.CoreWebView2.ExecuteScriptAsync("s1.frames(700).then(t => window.__f = t)");
            form.Open();
            await frames;
            await Task.Delay(800);
            var times = JsonSerializer.Deserialize<double[]>(await form.View.WebView.CoreWebView2.ExecuteScriptAsync("window.__f")) ?? [];
            results["openFrames"] = new { count = times.Length, mean = Math.Round(times.DefaultIfEmpty().Average(), 1), longest = Math.Round(times.DefaultIfEmpty().Max(), 1), late = times.Count(t => t > 20) };

            // The drag: press on the header, move 120 by -80 in 12 steps, release.
            var before = form.Location;
            var grip = new Point(form.Left + (int)(120 * s), form.Top + (int)(20 * s));
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
            back.Bounds = Rectangle.Inflate(form.Bounds, 80, 80);

            // The Opacity fade (a layered top window): does the web content show at 50 %?
            form.Opacity = .5;
            await Task.Delay(500);
            Shot(Rectangle.Inflate(form.Bounds, m, m), Path.Combine(folder, "s1-opacity-50.png"));
            var mid = Shot(new Rectangle(form.Left + form.Width / 2, form.Top + (int)(80 * s), 1, 1), null).GetPixel(0, 0);
            results["opacity50RowPixel"] = Hex(mid);
            form.Opacity = 1;
            await Task.Delay(300);
            results["opacity100RowPixel"] = Hex(Shot(new Rectangle(form.Left + form.Width / 2, form.Top + (int)(80 * s), 1, 1), null).GetPixel(0, 0));
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Application.Exit();
    }

    static async Task Until(PlainForm form, string test)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await form.View.WebView.CoreWebView2.ExecuteScriptAsync(test) == "true") return;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
}
