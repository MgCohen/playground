using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using S7.Components;

namespace S7;

/// <summary>
/// The coordinator of the two windows. A dock: the panel folds, the panel window hides, the badge window shows with the badge
/// out of view, the badge slides in. An open is the reverse. No window moves or changes its size. One request runs at a time.
/// </summary>
public sealed class Coordinator
{
    public const string Open = "open", Dock = "dock";

    public Func<Task> PanelOpen { get; set; } = () => Task.CompletedTask;
    public Func<Task> PanelFold { get; set; } = () => Task.CompletedTask;
    public Func<Task> BadgeIn { get; set; } = () => Task.CompletedTask;
    public Func<Task> BadgeOut { get; set; } = () => Task.CompletedTask;

    public ClearForm? Panel { get; set; }
    public ClearForm? Badge { get; set; }

    /// <summary>The form that shows: "panel" or "badge".</summary>
    public string State { get; private set; } = "panel";

    /// <summary>The time of the last Show() of the badge window (Unix ms), for the probe.</summary>
    public long BadgeShownAt { get; private set; }

    bool busy;
    string? pending;

    public void Request(string what) => Panel?.BeginInvoke(() => _ = Run(what));

    public async Task Run(string what)
    {
        if (busy)
        {
            pending = what;
            return;
        }
        busy = true;
        try
        {
            if (what == Dock && State == "panel") await DoDock();
            else if (what == Open && State == "badge") await DoOpen();
        }
        finally
        {
            busy = false;
        }
        if (pending is { } next)
        {
            pending = null;
            await Run(next);
        }
    }

    async Task DoDock()
    {
        await PanelFold();
        Panel!.Trace("fold ended: hide the panel window");
        Panel.Hide();
        Panel.SetContentVisible(false);
        BadgeShownAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Badge!.SetContentVisible(true);
        Badge.Show();
        Badge.Trace("badge window shown");
        await BadgeIn();
        State = "badge";
    }

    async Task DoOpen()
    {
        await BadgeOut();
        Badge!.Trace("badge out: hide the badge window");
        Badge.Hide();
        Badge.SetContentVisible(false);
        Panel!.SetContentVisible(true);
        Panel.Show();
        Panel.Activate();
        Panel.Trace("panel window shown");
        await PanelOpen();
        State = "panel";
    }
}

/// <summary>Spike S7: the panel and the badge as two windows. Run with "measure folder one|two" for the numbers.</summary>
static class Program
{
    // CSS pixels: the margin for the shadow, the panel, and the badge at its hover width (as S6).
    const int Margin = 24, PanelW = 350, PanelH = 520, BadgeW = 250, BadgeH = 76;

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var co = new Coordinator();
        var services = new ServiceCollection();
        services.AddBlazorWebView();
        services.AddSingleton(co);
        using var provider = services.BuildServiceProvider();
        var measure = args.Length >= 2 && args[0] == "measure";
        var two = !(args.Length >= 3 && args[2] == "one");
        var clock = Stopwatch.StartNew();

        var panel = new ClearForm(provider, typeof(PanelRoot), "panel");
        co.Panel = panel;
        var s = panel.DeviceDpi / 96f;
        var area = Screen.PrimaryScreen!.WorkingArea;
        var panelSize = new Size((int)((PanelW + 2 * Margin) * s), (int)((PanelH + 2 * Margin) * s));
        panel.Bounds = new Rectangle(area.Right - panelSize.Width, area.Bottom - panelSize.Height, panelSize.Width, panelSize.Height);
        panel.SetHitArea([new Rectangle(Point.Empty, panel.Size)]);

        ClearForm? badge = null;
        if (two)
        {
            badge = new ClearForm(provider, typeof(BadgeRoot), "badge") { BadgeMode = true };
            co.Badge = badge;
            var badgeSize = new Size((int)((BadgeW + Margin) * s), (int)((BadgeH + 2 * Margin) * s));
            badge.Bounds = new Rectangle(area.Right - badgeSize.Width, area.Top + area.Height / 2 - badgeSize.Height / 2, badgeSize.Width, badgeSize.Height);
            badge.SetHitArea([new Rectangle(Point.Empty, badge.Size)]);
            badge.BadgeClicked += () => co.Request(Coordinator.Open);
            // The window handle starts the WebView of the badge now; the window stays hidden until the first dock.
            _ = badge.Handle;
        }

        using var icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "S7 two windows", Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) co.Request(co.State == "panel" ? Coordinator.Dock : Coordinator.Open);
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;

        panel.Shown += async (_, _) =>
        {
            await panel.Ready.Task;
            panel.Trace($"panel ready at {clock.ElapsedMilliseconds} ms");
            await co.PanelOpen();
            if (measure) await Probe.Run(co, Path.GetFullPath(args[1]), two, clock);
        };
        Application.Run(panel);
        icon.Visible = false;
    }
}

/// <summary>The numbers of S7: memory, start times, and 10 dock and open cycles. No mouse input.</summary>
static class Probe
{
    public static async Task Run(Coordinator co, string folder, bool two, Stopwatch clock)
    {
        Directory.CreateDirectory(folder);
        var results = new Dictionary<string, object?>();
        var log = new List<string>();
        void Step(string text) => log.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        try
        {
            results["mode"] = two ? "two windows" : "one window";
            results["panelReadyMs"] = clock.ElapsedMilliseconds;
            if (co.Badge is { } b)
            {
                await b.Ready.Task;
                results["badgeReadyMs"] = clock.ElapsedMilliseconds;
            }
            await Task.Delay(3000);
            results["memoryAtRest"] = Memory(co.Panel!.WebEnv!);
            if (!two)
            {
                Write(folder, results, log);
                Application.Exit();
                return;
            }
            Step("stress start");
            var cycles = new List<object>();
            for (var i = 0; i < 10; i++)
            {
                Step("dock");
                var t0 = clock.ElapsedMilliseconds;
                await co.Run(Coordinator.Dock);
                var dockMs = clock.ElapsedMilliseconds - t0;
                Step("docked");
                await Task.Delay(300);
                var seen = JsonSerializer.Deserialize<Seen>(await co.Badge!.Web!.ExecuteScriptAsync("window.s7seen"));
                await Task.Delay(500);
                Step("open");
                t0 = clock.ElapsedMilliseconds;
                await co.Run(Coordinator.Open);
                var openMs = clock.ElapsedMilliseconds - t0;
                Step("opened");
                await Task.Delay(1200);
                cycles.Add(new
                {
                    dockMs,
                    openMs,
                    badgeShowToVisibleMs = seen?.visible is { } v ? Math.Round(v - co.BadgeShownAt) : (double?)null,
                    badgeShowToFrameMs = seen?.frame is { } f ? Math.Round(f - co.BadgeShownAt) : (double?)null,
                });
            }
            results["cycles"] = cycles;
            results["memoryAfterCycles"] = Memory(co.Panel!.WebEnv!);
        }
        catch (Exception e)
        {
            results["error"] = e.ToString();
        }
        Write(folder, results, log);
        Application.Exit();
    }

    sealed record Seen(double? visible, double? frame);

    static void Write(string folder, Dictionary<string, object?> results, List<string> log)
    {
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(folder, "stress.txt"), log);
    }

    /// <summary>The memory of this process and of each WebView2 process of the environment, in MB.</summary>
    static object Memory(CoreWebView2Environment env)
    {
        var rows = new List<object>();
        long ws = 0, priv = 0;
        void Add(int pid, string kind)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                ws += p.WorkingSet64;
                priv += p.PrivateMemorySize64;
                rows.Add(new { kind, pid, workingSetMb = p.WorkingSet64 / 1048576, privateMb = p.PrivateMemorySize64 / 1048576 });
            }
            catch (ArgumentException)
            {
            }
        }
        Add(Environment.ProcessId, "host");
        foreach (var info in env.GetProcessInfos()) Add(info.ProcessId, info.Kind.ToString());
        return new { processes = rows.Count, workingSetMb = ws / 1048576, privateMb = priv / 1048576, rows };
    }
}
