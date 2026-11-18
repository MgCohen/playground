using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.Tray;

/// <summary>
/// The tray icon and the widget. A left click on the icon shows or hides the widget. Right click: the menu
/// (Show, Pin on top, Exit). Exit closes the widget and the icon.
/// </summary>
public sealed partial class TrayApp : ApplicationContext
{
    static readonly Color Accent = Color.FromArgb(0xff, 0x6a, 0x1a);

    readonly NotifyIcon icon;
    readonly Icon dot;
    bool exiting;

    public TrayApp(IServiceProvider services, string label, bool show, bool pinned, string? placeFile)
    {
        dot = Dot(Accent);
        Form = new WidgetForm(services, dot, pinned, placeFile);
        services.GetRequiredService<WidgetWindow>().Form = Form;
        var pin = new ToolStripMenuItem("Pin on top") { CheckOnClick = true, Checked = pinned };
        pin.CheckedChanged += (_, _) => Form.Pinned = pin.Checked;
        Form.PinnedChanged += () => pin.Checked = Form.Pinned;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Form.ShowWidget());
        menu.Items.Add(pin);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        icon = new NotifyIcon { Icon = dot, Text = Tip($"Orchestrator ({label})"), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Form.Toggle();
        };
        Tab = EdgeTab.Start(placeFile is null ? null : Path.Combine(Path.GetDirectoryName(placeFile)!, Path.GetFileNameWithoutExtension(placeFile).Replace("place", "edge-tab") + ".txt"));
        // The tab runs on its own thread: its events come back to the thread of the widget.
        Tab.OpenRequested += () => Tab.SlideOut(() => Form.BeginInvoke(Form.ShowWidget));
        Form.Minimized += Tab.SlideIn;
        // Always: a slide in that waits on the thread of the tab is not on the screen yet, but it runs before this slide out (V4).
        Form.Showing += () => Tab.SlideOut();
        // A notice that came during the shrink shows when the widget is hidden (V4).
        Form.Hidden += () =>
        {
            if (waiting is not { } notice) return;
            waiting = null;
            Notify(notice);
        };
        Store = services.GetRequiredService<Orchestrator.Store.FileStore>();
        board = services.GetRequiredService<Orchestrator.Web.Board>();
        hub = services.GetRequiredService<Orchestrator.Web.StreamHub>();
        hub.Changed += Changed;
        Tab.SetCounts(Counts().Counts);
        if (show) Form.ShowWidget();
        // The window handle exists, so a second start can marshal to the UI thread.
        else _ = Form.Handle;
    }

    public WidgetForm Form { get; }

    /// <summary>N1: the minimized form of the widget at the right edge of the screen.</summary>
    public EdgeTab Tab { get; }

    /// <summary>The store that the tray shows. The self-test writes to it.</summary>
    public Orchestrator.Store.FileStore Store { get; }

    readonly Orchestrator.Web.Board board;
    readonly Orchestrator.Web.StreamHub hub;

    int changing;

    // A burst of store changes gives one count: the first change waits a short time, the others in that time do nothing.
    void Changed()
    {
        if (exiting || Form.IsDisposed || !Form.IsHandleCreated) return;
        if (Interlocked.Exchange(ref changing, 1) == 1) return;
        _ = Task.Delay(250).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref changing, 0);
            if (exiting || Form.IsDisposed) return;
            Form.BeginInvoke(() => Notify(Counts()));
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// N2: a new waiting card while the widget is hidden (closed or minimized): the edge tab slides in, its ring spins, and the
    /// peek bubble shows the newest card. While the widget shows, the counts change with no notice.
    /// </summary>
    public void Notify((WaitCounts Counts, string? Title, string? Sub) next)
    {
        // One form at a time (V4): while the widget shows or grows, the tab stays hidden and gets only the counts.
        if (Form.Visible && !Form.Hiding)
        {
            waiting = null;
            Tab.SetCounts(next.Counts);
            return;
        }
        // While the widget shrinks, the notice waits for the end of the hide.
        if (Form.Hiding)
        {
            waiting = next;
            return;
        }
        if (next.Counts.Total > 0 && !Tab.OnScreen) Tab.SlideIn();
        Tab.SetCounts(next.Counts, next.Title, next.Sub);
    }

    (WaitCounts Counts, string? Title, string? Sub)? waiting;

    /// <summary>The waiting cards by type, and the newest one for the peek bubble.</summary>
    public (WaitCounts Counts, string? Title, string? Sub) Counts()
    {
        var cards = board.Cards();
        var counts = new WaitCounts(
            cards.Count(c => c.Group == Orchestrator.Web.CardGroup.Questions),
            cards.Count(c => c.Group == Orchestrator.Web.CardGroup.Reviews),
            cards.Count(c => c.Group == Orchestrator.Web.CardGroup.Reports),
            cards.Count(c => c.Group == Orchestrator.Web.CardGroup.Blocked));
        var newest = cards.MaxBy(c => c.Id, StringComparer.Ordinal);
        return (counts, newest is null ? null : $"{newest.Label} · {newest.SessionTitle}", newest?.Title);
    }

    /// <summary>Shows the widget from any thread. A second start of the app calls it.</summary>
    public void ShowFromAnyThread()
    {
        if (exiting || Form.IsDisposed) return;
        Form.BeginInvoke(Form.ShowWidget);
    }

    public void Exit()
    {
        if (exiting) return;
        exiting = true;
        Form.Exiting = true;
        Tab.Stop();
        Form.Close();
        icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hub.Changed -= Changed;
            Tab.Stop();
            icon.Dispose();
            Form.Dispose();
            dot.Dispose();
        }
        base.Dispose(disposing);
    }

    // The tray tip has a maximum of 127 characters.
    static string Tip(string text) => text.Length <= 127 ? text : text[..124] + "...";

    /// <summary>A 16 × 16 dot of one color with a white ring.</summary>
    static Icon Dot(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 0, 0, 15, 15);
            g.FillEllipse(fill, 2, 2, 11, 11);
        }
        var handle = bitmap.GetHicon();
        using var temp = System.Drawing.Icon.FromHandle(handle);
        var copy = (Icon)temp.Clone();
        DestroyIcon(handle);
        return copy;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
