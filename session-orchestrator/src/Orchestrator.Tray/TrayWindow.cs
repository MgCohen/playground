using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The panel window: the composition host of the panel, at its own place. The place is a free point (a drag of the header
/// moves it). The window never changes its job: the badge has its own window (<see cref="BadgeWindow"/>). The tray flow
/// (Components/Tray/TrayFlow) sends each phase through <see cref="WidgetWindow"/>; the window shows, hides or activates, and
/// sets its click area. It changes no look and plays no motion.
/// </summary>
public sealed class TrayWindow : ClearHost
{
    /// <summary>
    /// The edge gap: the space between the panel and the edge of the work area, in logical pixels. The window owns it, because
    /// it is a rule of the place on the screen. The page owns the sizes of the panel and of its shadow margin (R2, tray.css).
    /// </summary>
    public const int Gap = 12;

    readonly string? placeFile;
    Point? panel;
    bool pinned;

    public TrayWindow(IServiceProvider services, Icon icon, bool pinned, string? placeFile) : base(services, typeof(Orchestrator.Web.Components.Tray.TrayRoot))
    {
        this.pinned = pinned;
        this.placeFile = placeFile;
        panel = LoadPlace(placeFile);
        Text = "Orchestrator";
        Icon = icon;
        TopMost = pinned;
        // Before the first size report the page has no size yet: the window stays hidden (ShowAtPlace needs a report).
        Bounds = new Rectangle(Slot.Area(PanelScreen()).Right - 1, Slot.Area(PanelScreen()).Bottom - 1, 1, 1);
        MoveEnded += SavePanel;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    /// <summary>The phase of the tray flow, as the windows last saw it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Phase { get; internal set; } = TrayPhase.Hidden;

    /// <summary>A request of the user for the tray flow (the tray icon, the menu, a badge click, a second start of the app).</summary>
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
            TopMost = pinned;
            PinnedChanged?.Invoke();
        }
    }

    public event Action? PinnedChanged;

    /// <summary>The saved place of the panel after a drag, or null for its home place above the tray.</summary>
    public Point? Place => panel;

    /// <summary>The screen of the panel: the badge window uses the same screen.</summary>
    public Screen PanelScreen() => panel is { } p ? Screen.FromPoint(p) : Screen.PrimaryScreen ?? Screen.AllScreens[0];

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    /// <summary>Shows the panel window at its place (it shows nothing until the page draws the start phase).</summary>
    public void ShowAtPlace()
    {
        if (Sizes is null)
        {
            Faults.Add(new InvalidOperationException("The panel page gave no sizes (orchTray.sizes)."), "panel window");
            return;
        }
        Bounds = PanelBounds();
        Hits();
        if (!Visible)
        {
            Faults.Trace("panel window show");
            Show();
        }
    }

    float Dpi => DeviceDpi / 96f;

    /// <summary>R2: the last sizes that the page of this window measured (orchTray.sizes), in CSS px.</summary>
    public Orchestrator.Web.Features.Tray.Slices.Sizes.TraySizesResult? Sizes { get; private set; }

    public void UseSizes(Orchestrator.Web.Features.Tray.Slices.Sizes.TraySizesResult sizes)
    {
        Sizes = sizes;
        if (!Visible) return;
        Bounds = PanelBounds();
        Hits();
    }

    /// <summary>The shadow margin around the panel in window pixels (the token --tray-margin of the page).</summary>
    public int ShadowMargin => (int)((Sizes?.Margin ?? 0) * Dpi);

    /// <summary>The click area: the panel with its shadow, cut to the work area.</summary>
    void Hits()
    {
        var work = Slot.Area(Screen.FromRectangle(Bounds));
        SetHitArea(Rectangle.Intersect(new Rectangle(Point.Empty, Size), new Rectangle(work.X - Left, work.Y - Top, work.Width, work.Height)));
    }

    /// <summary>The size of the panel window at the DPI: the panel is at most the work area height less the gap above and below.</summary>
    Size PanelSize()
    {
        var area = Slot.Area(PanelScreen());
        var panelHeight = Math.Min((int)((Sizes?.PanelHeight ?? 0) * Dpi), area.Height - (int)(2 * Gap * Dpi));
        return new Size((int)((Sizes?.PanelWidth ?? 0) * Dpi) + 2 * ShadowMargin, panelHeight + 2 * ShadowMargin);
    }

    /// <summary>The panel window: the saved place of the panel (or its home place above the tray), fit into the work area with the gap.</summary>
    public Rectangle PanelBounds()
    {
        var size = PanelSize();
        var area = Slot.Area(PanelScreen());
        var m = ShadowMargin;
        var g = (int)(Gap * Dpi);
        var inner = new Size(size.Width - 2 * m, size.Height - 2 * m);
        var p = panel ?? new Point(area.Right - inner.Width - g, area.Bottom - inner.Height - g);
        p = new Point(Math.Clamp(p.X, area.Left + g, Math.Max(area.Left + g, area.Right - inner.Width - g)),
            Math.Clamp(p.Y, area.Top + g, Math.Max(area.Top + g, area.Bottom - inner.Height - g)));
        return new Rectangle(p.X - m, p.Y - m, size.Width, size.Height);
    }

    void SavePanel()
    {
        var m = ShadowMargin;
        panel = new Point(Left + m, Top + m);
        Faults.Trace($"panel drag end: {panel}");
        // A drop at the edge of the work area keeps the gap, so the panel never touches the taskbar.
        Bounds = PanelBounds();
        Hits();
        Write(placeFile, System.Text.Json.JsonSerializer.Serialize(new SavedPlace(panel.Value.X, panel.Value.Y)));
    }

    /// <summary>Sets the saved place of the panel without a drag. The self-test uses it.</summary>
    public void UsePlace(Point point)
    {
        panel = point;
        Write(placeFile, System.Text.Json.JsonSerializer.Serialize(new SavedPlace(point.X, point.Y)));
    }

    internal static void Write(string? file, string text)
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

    sealed record SavedPlace(int X, int Y);

    // A monitor that goes away, or a new DPI: the window comes back onto a current screen at its place.
    void DisplayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { if (Visible) ShowAtPlace(); });
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) ShowAtPlace();
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
