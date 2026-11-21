namespace Orchestrator.Tray;

/// <summary>
/// The badge window: a strip at the right edge of the work area of the panel screen, at a saved height. It is always on top.
/// The window keeps the left button for itself: a click opens the panel, a drag moves the window up and down. A hover makes
/// the click area wide, so the tab can extend to the left; the peek bubble makes it the full window. The page (BadgeRoot) has
/// the look and the motion.
/// </summary>
public sealed class BadgeWindow : ClearHost
{
    /// <summary>The badge window: the bubble, the gap and the tab at its hover width, with a margin for the shadow (tray.css).</summary>
    public const int BadgeWidth = 332, BadgeHeight = 96, TabRest = 68, TabWide = 280, TabMargin = 14;

    readonly Func<Screen> screen;
    readonly string? topFile;
    int? top;
    bool hover, peek;

    // The fold of the hover: the token --t-badge-fold of motion.css. The badge page reads it (BadgeRoot) and gives it here.
    TimeSpan fold;

    public void UseFold(TimeSpan time) => fold = time;

    public BadgeWindow(IServiceProvider services, Icon icon, Func<Screen> screen, string? topFile) : base(services, typeof(Orchestrator.Web.Components.Tray.BadgeRoot))
    {
        this.screen = screen;
        this.topFile = topFile;
        top = LoadTop(topFile);
        Text = "Orchestrator badge";
        Icon = icon;
        TopMost = true;
        Bounds = PlaceBounds();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    /// <summary>A click on the badge (no drag).</summary>
    public event Action? Clicked;

    float Dpi => DeviceDpi / 96f;

    /// <summary>Shows the badge window at its place, with no input from the last time (no hover, no press).</summary>
    public void ShowAtPlace()
    {
        over = false;
        down = null;
        dragging = false;
        hover = peek = false;
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
        var size = new Size((int)(BadgeWidth * Dpi), (int)(BadgeHeight * Dpi));
        var y = Math.Clamp(top ?? area.Top + (int)(area.Height * .42) - (int)(TabMargin * Dpi), area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Rectangle(area.Right - size.Width, y, size.Width, size.Height);
    }

    /// <summary>The click area from the right edge: the tab at rest or at its hover width, or the full window at a peek.</summary>
    void Hits()
    {
        var wide = peek ? BadgeWidth : hover ? TabWide + TabMargin : TabRest + TabMargin;
        var w = (int)(wide * Dpi);
        SetHitArea(new Rectangle(Width - w, 0, w, Height));
    }

    /// <summary>
    /// Test seam: holds the hover of the badge (true) or ends it (false), with no real pointer and no posted message. The window
    /// and the page both see the hover, as with a real pointer on the ring.
    /// </summary>
    internal void HoldHover(bool on)
    {
        over = on;
        PointerAt(on ? new Point(Width - (int)(34 * Dpi), (int)((TabMargin + 34) * Dpi)) : null);
        Hover(on);
    }

    void Hover(bool on)
    {
        hover = on;
        if (on) Hits();
        // The fold of the hover ends before the click area gets small again.
        else _ = Task.Delay(fold).ContinueWith(_ => BeginInvoke(() => { if (!hover) Hits(); }), TaskScheduler.Default);
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
