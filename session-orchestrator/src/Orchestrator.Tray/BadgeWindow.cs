namespace Orchestrator.Tray;

/// <summary>
/// The badge window: a strip at the right edge of the work area of the panel screen, at a saved height. It is always on top.
/// The window keeps the left button for itself: a click opens the panel, a drag moves the window up and down. The page
/// (BadgeRoot) has the look, the motion and all sizes (R2): it reports the shadow box of the tab at rest and at the hover, and of
/// the peek bubble. The window is as large as all boxes together; the click area is the box of the current state. The window
/// region is the click area, and Windows clips the drawing to it, so each drawn part must be inside its box.
/// </summary>
public sealed class BadgeWindow : ClearHost
{
    readonly Func<Screen> screen;
    readonly string? topFile;
    int? top;
    bool hover, peek;

    public BadgeWindow(IServiceProvider services, Icon icon, Func<Screen> screen, string? topFile) : base(services, typeof(Orchestrator.Web.Components.Tray.BadgeRoot))
    {
        this.screen = screen;
        this.topFile = topFile;
        top = LoadTop(topFile);
        Text = "Orchestrator badge";
        Icon = icon;
        TopMost = true;
        // Before the first size report the page has no size yet: the window stays hidden (ShowAtPlace needs a report).
        Bounds = new Rectangle(Slot.Area(screen()).Right - 1, Slot.Area(screen()).Top, 1, 1);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    /// <summary>A click on the badge (no drag).</summary>
    public event Action? Clicked;

    float Dpi => DeviceDpi / 96f;

    /// <summary>R2: the last sizes that the page of this window measured (orchTray.sizes), in CSS px.</summary>
    public Orchestrator.Web.Features.Tray.Slices.Sizes.TraySizesResult? Sizes { get; private set; }

    /// <summary>R2: new sizes from the page (a new text of the counts or of the bubble). The window keeps its right edge and top.</summary>
    public void UseSizes(Orchestrator.Web.Features.Tray.Slices.Sizes.TraySizesResult sizes)
    {
        if (sizes.Rest is null || sizes.Hover is null) return;
        Sizes = sizes;
        if (!Visible) return;
        var size = WindowSize();
        Bounds = new Rectangle(Right - size.Width, Top, size.Width, size.Height);
        Hits();
    }

    /// <summary>Shows the badge window at its place, with no input from the last time (no hover, no press).</summary>
    public void ShowAtPlace()
    {
        over = false;
        down = null;
        dragging = false;
        hover = peek = false;
        if (Sizes is null)
        {
            Faults.Add(new InvalidOperationException("The badge page gave no sizes (orchTray.sizes)."), "badge window");
            return;
        }
        Bounds = PlaceBounds();
        Hits();
        if (!Visible)
        {
            Faults.Trace("badge window show");
            Show();
        }
    }

    /// <summary>The badge shows its peek bubble: the click area takes the bubble in.</summary>
    public void Peek(bool on)
    {
        peek = on;
        Hits();
    }

    /// <summary>The badge window: at the right edge of the work area, at the saved height (N1: else the tab top at 42 % of the height).</summary>
    public Rectangle PlaceBounds()
    {
        var area = Slot.Area(screen());
        var size = WindowSize();
        var tabTop = (int)((Sizes?.TabTop ?? 0) * Dpi);
        var y = Math.Clamp(top ?? area.Top + (int)(area.Height * .42) - tabTop, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Rectangle(area.Right - size.Width, y, size.Width, size.Height);
    }

    /// <summary>The window size: all boxes of the page together, from the right edge, at the DPI, at most the work area.</summary>
    Size WindowSize()
    {
        if (Sizes is not { Rest: { } rest, Hover: { } wide } s) return Size;
        var all = s.Bubble is { } bubble ? Union(Union(rest, wide), bubble) : Union(rest, wide);
        var area = Slot.Area(screen());
        return new Size(Math.Min(area.Width, (int)Math.Ceiling((s.Width - all[0]) * Dpi)), Math.Min(area.Height, (int)Math.Ceiling(all[3] * Dpi)));
    }

    static double[] Union(double[] a, double[] b) => [Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Max(a[2], b[2]), Math.Max(a[3], b[3])];

    /// <summary>
    /// The click area of a state, in window pixels: the tab box at rest or at the hover, with the bubble box at a peek. The right
    /// side is the window edge (the tab is flush with the screen edge).
    /// </summary>
    public Rectangle AreaOf(bool hovered, bool peeked)
    {
        if (Sizes is not { Rest: { } rest, Hover: { } wide } s) return new Rectangle(Point.Empty, Size);
        var box = hovered ? wide : rest;
        if (peeked && s.Bubble is { } bubble) box = Union(box, bubble);
        var left = Math.Max(0, Width - (int)Math.Ceiling((s.Width - box[0]) * Dpi));
        var topPx = Math.Max(0, (int)Math.Floor(box[1] * Dpi));
        var bottom = Math.Min(Height, (int)Math.Ceiling(box[3] * Dpi));
        return Rectangle.FromLTRB(left, topPx, Width, bottom);
    }

    /// <summary>The center of the ring in window pixels: the middle of the tab at rest (the ring is at the left side of the tab).</summary>
    public Point RingPoint()
    {
        var tab = Sizes?.Tab ?? 0;
        return new Point(Width - (int)(tab / 2 * Dpi), (int)(((Sizes?.TabTop ?? 0) + tab / 2) * Dpi));
    }

    void Hits() => SetHitArea(AreaOf(hover, peek));

    /// <summary>
    /// Test seam: holds the hover of the badge (true) or ends it (false), with no real pointer and no posted message (I8: with a
    /// posted move, Windows ends the hover at once). It takes the same path as a real hover.
    /// </summary>
    internal void HoldHover(bool on)
    {
        over = on;
        Hover(on);
    }

    /// <summary>The hover of the window starts (true) or ends (false). The page draws it (the class is-hover of the tab).</summary>
    public event Action<bool>? Hovered;

    // The window owns the hover (Q1 of R2): one source for the click area and the look. At the start of a hover the click area
    // gets wide at once; at the end it stays wide until the page reports the end of the fold (HoverEnded).
    void Hover(bool on)
    {
        hover = on;
        if (on) Hits();
        Hovered?.Invoke(on);
    }

    /// <summary>The page reports that the fold of the hover ended (step badge-fold): the click area gets small again.</summary>
    public void HoverEnded()
    {
        if (!hover) Hits();
    }

    Point? down;
    int downTop;
    bool dragging, over;

    protected override bool HostInput(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x201: // WM_LBUTTONDOWN: the window keeps it; the page gets no click.
                down = At(m);
                downTop = Top;
                dragging = false;
                Capture = true;
                return true;
            case 0x200 when down is { } d && (m.WParam & 1) != 0: // MK_LBUTTON
                {
                    var dy = At(m).Y - d.Y;
                    // N1: a move of 5 px starts the drag; a smaller move is still a click.
                    if (!dragging && Math.Abs(dy) < 5 * DeviceDpi / 96) return true;
                    dragging = true;
                    var area = Slot.Area(Screen.FromHandle(Handle));
                    Top = Math.Clamp(downTop + dy, area.Top, Math.Max(area.Top, area.Bottom - Height));
                    return true;
                }
            case 0x200 when !over:
                over = true;
                Hover(true);
                return false;
            case 0x2A3: // WM_MOUSELEAVE
                over = false;
                Hover(false);
                return false;
            case 0x202 when down is not null: // WM_LBUTTONUP
                Capture = false;
                down = null;
                if (dragging) SaveTop();
                else Clicked?.Invoke();
                dragging = false;
                return true;
        }
        return false;
    }

    // The screen point of a mouse message. The point and the button come from the message, not from the real pointer, so a
    // posted message (the demo, the self-test) and a real one take the same path.
    Point At(Message m) => PointToScreen(new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff)));

    void SaveTop()
    {
        top = Top;
        Faults.Trace($"badge drag end: top {Top}");
        TrayWindow.Write(topFile, Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    static int? LoadTop(string? file)
    {
        try
        {
            return file is not null && File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var t) ? t : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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

    // The badge never closes by itself: Alt+F4 on it does nothing. Exit in the tray menu stops the app.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !Exiting)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        base.Dispose(disposing);
    }
}
