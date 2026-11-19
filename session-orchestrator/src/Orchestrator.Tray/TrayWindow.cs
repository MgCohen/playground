using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The tray window: the composition host at one of two places. The panel place is a free point (a drag of the header moves it).
/// The badge place is the right edge of the work area of the panel screen, at a saved height (a drag moves it up and down).
/// The stage of the views (TrayStage) sends each phase with <see cref="Enter"/>; the window goes to the place of the phase while
/// nothing shows, and sets its click area. It changes no look and plays no motion.
/// </summary>
public sealed class TrayWindow : ClearHost
{
    /// <summary>The panel and the margin around it for its shadow, in logical pixels (tray.css).</summary>
    public const int PanelWidth = 350, PanelHeight = 720, ShadowMargin = 24, Gap = 12;

    /// <summary>The badge window: the bubble, the gap and the tab at its hover width, with a margin for the shadow (tray.css).</summary>
    public const int BadgeWidth = 332, BadgeHeight = 96, TabRest = 68, TabWide = 280, TabMargin = 14;

    readonly string? placeFile, badgeFile;
    Point? panel;
    int? badgeTop;
    bool pinned, hover, peek;

    public TrayWindow(IServiceProvider services, Icon icon, bool pinned, string? placeFile) : base(services)
    {
        this.pinned = pinned;
        this.placeFile = placeFile;
        badgeFile = placeFile is null ? null : Path.Combine(Path.GetDirectoryName(placeFile)!, Path.GetFileNameWithoutExtension(placeFile).Replace("place", "badge") + ".txt");
        panel = LoadPlace(placeFile);
        badgeTop = LoadTop(badgeFile);
        Text = "Orchestrator";
        Icon = icon;
        Bounds = PanelBounds();
        PanelMoved += SavePanel;
        BadgeMoved += SaveBadge;
        BadgeClicked += () => Requested?.Invoke(TrayRequest.Open);
        BadgeHover += on =>
        {
            hover = on;
            if (on) Hits();
            // The fold of the hover ends before the click area gets small again.
            else _ = Task.Delay(300).ContinueWith(_ => BeginInvoke(() => { if (!hover) Hits(); }), TaskScheduler.Default);
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    /// <summary>The phase of the stage that the window is in.</summary>
    public string Phase { get; private set; } = TrayPhase.Hidden;

    /// <summary>A request of the user for the stage (the badge click, the tray icon, the menu, a second start of the app).</summary>
    public event Action<string>? Requested;

    public void Request(string what) => Requested?.Invoke(what);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Pinned
    {
        get => pinned;
        set
        {
            if (pinned == value) return;
            pinned = value;
            TopMost = BadgeMode || pinned;
            PinnedChanged?.Invoke();
        }
    }

    public event Action? PinnedChanged;

    /// <summary>The saved place of the panel after a drag, or null for its home place above the tray.</summary>
    public Point? Place => panel;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    /// <summary>The stage enters a phase: the window goes to its place. The task ends when the window is there.</summary>
    public Task EnterPhase(string phase)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(() =>
        {
            Faults.Trace($"tray phase {phase} (from {Phase})");
            Phase = phase;
            switch (phase)
            {
                case TrayPhase.Start:
                    Go(badge: false);
                    if (!Visible)
                    {
                        Faults.Trace("window show");
                        Show();
                    }
                    break;
                case TrayPhase.Open:
                    Faults.Trace("window activate");
                    Activate();
                    break;
                case TrayPhase.BadgeOut:
                    Go(badge: true);
                    if (!Visible) Show();
                    break;
                case TrayPhase.Hidden:
                    Hide();
                    break;
            }
            done.SetResult();
        });
        return done.Task;
    }

    /// <summary>The badge shows its peek bubble: the click area takes the bubble in.</summary>
    public void Peek(bool on) => BeginInvoke(() =>
    {
        peek = on;
        Hits();
    });

    void Go(bool badge)
    {
        BadgeMode = badge;
        ResetBadgeInput();
        hover = peek = false;
        TopMost = badge || pinned;
        Faults.Trace($"window bounds {(badge ? BadgeBounds() : PanelBounds())} (was {Bounds})");
        Bounds = badge ? BadgeBounds() : PanelBounds();
        Hits();
    }

    float Dpi => DeviceDpi / 96f;

    /// <summary>The click area: the panel with its shadow, cut to the work area; at the badge place the tab at rest or at its hover width, and the bubble when it peeks.</summary>
    void Hits()
    {
        if (!BadgeMode)
        {
            var work = Screen.FromRectangle(Bounds).WorkingArea;
            var inside = Rectangle.Intersect(new Rectangle(Point.Empty, Size), new Rectangle(work.X - Left, work.Y - Top, work.Width, work.Height));
            SetHitArea(inside);
            return;
        }
        var wide = peek ? BadgeWidth : hover ? TabWide + TabMargin : TabRest + TabMargin;
        var w = (int)(wide * Dpi);
        SetHitArea(new Rectangle(Width - w, 0, w, Height));
    }

    /// <summary>The size of the panel window at the DPI: the panel is at most the work area height less the gap above and below.</summary>
    Size PanelSize()
    {
        var area = PanelScreen().WorkingArea;
        var panelHeight = Math.Min((int)(PanelHeight * Dpi), area.Height - (int)(2 * Gap * Dpi));
        return new Size((int)((PanelWidth + 2 * ShadowMargin) * Dpi), panelHeight + (int)(2 * ShadowMargin * Dpi));
    }

    Screen PanelScreen() => panel is { } p ? Screen.FromPoint(p) : Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>The panel window: the saved place of the panel (or its home place above the tray), fit into the work area with the gap.</summary>
    public Rectangle PanelBounds()
    {
        var size = PanelSize();
        var area = PanelScreen().WorkingArea;
        var m = (int)(ShadowMargin * Dpi);
        var g = (int)(Gap * Dpi);
        var inner = new Size(size.Width - 2 * m, size.Height - 2 * m);
        var p = panel ?? new Point(area.Right - inner.Width - g, area.Bottom - inner.Height - g);
        p = new Point(Math.Clamp(p.X, area.Left + g, Math.Max(area.Left + g, area.Right - inner.Width - g)),
            Math.Clamp(p.Y, area.Top + g, Math.Max(area.Top + g, area.Bottom - inner.Height - g)));
        return new Rectangle(p.X - m, p.Y - m, size.Width, size.Height);
    }

    /// <summary>The badge window: at the right edge of the work area of the panel screen, at the saved height (N1: else the tab top at 42 % of the height).</summary>
    public Rectangle BadgeBounds()
    {
        var area = PanelScreen().WorkingArea;
        var size = new Size((int)(BadgeWidth * Dpi), (int)(BadgeHeight * Dpi));
        var top = Math.Clamp(badgeTop ?? area.Top + (int)(area.Height * .42) - (int)(TabMargin * Dpi), area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Rectangle(area.Right - size.Width, top, size.Width, size.Height);
    }

    void SavePanel()
    {
        var m = (int)(ShadowMargin * Dpi);
        panel = new Point(Left + m, Top + m);
        Faults.Trace($"panel drag end: {panel}");
        // A drop at the edge of the work area keeps the gap, so the panel never touches the taskbar.
        Bounds = PanelBounds();
        Hits();
        Write(placeFile, System.Text.Json.JsonSerializer.Serialize(new SavedPlace(panel.Value.X, panel.Value.Y)));
    }

    void SaveBadge()
    {
        badgeTop = Top;
        Faults.Trace($"badge drag end: top {Top}");
        Write(badgeFile, Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Sets the saved place of the panel without a drag. The self-test uses it.</summary>
    public void UsePlace(Point point)
    {
        panel = point;
        Write(placeFile, System.Text.Json.JsonSerializer.Serialize(new SavedPlace(point.X, point.Y)));
    }

    static void Write(string? file, string text)
    {
        if (file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static Point? LoadPlace(string? file)
    {
        try
        {
            return file is not null && File.Exists(file) && System.Text.Json.JsonSerializer.Deserialize<SavedPlace>(File.ReadAllText(file)) is { } p ? new Point(p.X, p.Y) : null;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static int? LoadTop(string? file)
    {
        try
        {
            return file is not null && File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var top) ? top : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    sealed record SavedPlace(int X, int Y);

    // A monitor that goes away, or a new DPI: the window comes back onto a current screen at its place.
    void DisplayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { if (Visible) Go(BadgeMode); });
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) Go(BadgeMode);
    }

    // Alt+F4 or a close of the system: the tray hides. Only Exit in the tray menu stops the app.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Requested?.Invoke(TrayRequest.Hide);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        base.Dispose(disposing);
    }
}
