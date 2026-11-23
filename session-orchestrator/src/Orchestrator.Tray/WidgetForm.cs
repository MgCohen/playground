using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Web.Components;

namespace Orchestrator.Tray;

/// <summary>
/// The widget: 350 × 720 logical pixels (lower on a small work area), no title bar, rounded corners, no outline, not in the taskbar.
/// It holds the BlazorWebView with the A4 views of Orchestrator.Web. A drag of the page header moves it. The saved place
/// (place.json) keeps its position for the next show and the next start; without a saved place it opens in the corner above the tray.
/// Close hides the widget. Only Exit in the tray menu stops the app. The widget grows from its bottom edge when it shows,
/// and shrinks to its bottom edge when it hides.
/// </summary>
public sealed partial class WidgetForm : Form
{
    public const int LogicalWidth = 350;
    public const int LogicalHeight = 720;

    // The canvas color of the page. It shows only before the web view paints.
    static readonly Color Canvas = Color.FromArgb(0xe6, 0xe3, 0xde);

    readonly BlazorWebView view;
    readonly string? placeFile;

    public string? PlaceFile => placeFile;
    Point? place;

    /// <param name="placeFile">The file of the saved place, or null for no saved place.</param>
    public WidgetForm(IServiceProvider services, Icon icon, bool pinned, string? placeFile)
    {
        this.placeFile = placeFile;
        place = LoadPlace(placeFile);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = pinned;
        Text = "Orchestrator";
        Icon = icon;
        // No outline: the web view fills the window. The rounded corners and the shadow of the system mark the edge.
        BackColor = Canvas;
        view = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        view.RootComponents.Add<Routes>("#app");
        view.RootComponents.Add<HeadOutlet>("head::after");
        Controls.Add(view);
        Size = Scaled();
        Location = Start();
        grow.Tick += (_, _) => GrowStep();
        dragTimer.Tick += (_, _) => DragStep();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        view.BlazorWebViewInitialized += (_, e) => e.WebView.CoreWebView2.NavigationCompleted += (_, _) => pageReady = true;
    }

    /// <summary>Keeps the widget on top of the other windows (TopMost).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Pinned
    {
        get => TopMost;
        set
        {
            if (TopMost == value) return;
            TopMost = value;
            PinnedChanged?.Invoke();
        }
    }

    public event Action? PinnedChanged;

    /// <summary>The saved place after a drag, or null when the widget uses its home location.</summary>
    public Point? Place => place;

    /// <summary>The WebView2 control after its start. The self-test uses it for the DOM checks and the screenshots.</summary>
    public CoreWebView2? Web => view.WebView.CoreWebView2;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    /// <summary>
    /// Shows the widget (G1). The home ball pops in at the place of the home button; after a short gap the widget grows out of
    /// it, the page fades in and the app bar unfolds sideways. If the page is not ready, the ball pulses for 500 ms first and the
    /// widget grows with the skeleton of the host page. At the end the home button of the page takes the place of the ball.
    /// </summary>
    public void ShowWidget()
    {
        if (!Visible || closing)
        {
            var run = ++sequence;
            minimizing = false;
            closing = false;
            grow.Stop();
            Showing?.Invoke();
            if (!Visible) Location = Start();
            Frame(false);
            Reveal(0);
            Show();
            Page("document.documentElement.classList.add('ball')");
            ball.Place(HomeCenter(), BallSize);
            starting = true;
            ball.PopIn(() =>
            {
                if (run != sequence) return;
                if (pageReady) After(GapMs, run, () => BeginGrow(open: true));
                else
                {
                    ball.Pulse(true);
                    After(PulseMs, run, () =>
                    {
                        ball.Pulse(false);
                        BeginGrow(open: true);
                    });
                }
            });
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        // The widget takes the focus and comes to the top; the ball stays above it until the handoff.
        if (ball.Visible) ball.ToTop();
    }

    /// <summary>
    /// Hides the widget (G1, "ball first"): the home ball takes the place of the home button and pops out, and the widget
    /// shrinks to its bottom edge when the pop out is half done; then it hides.
    /// </summary>
    public void HideWidget()
    {
        if (!Visible || closing) return;
        var run = ++sequence;
        closing = true;
        starting = false;
        grow.Stop();
        Page("document.documentElement.classList.add('ball')");
        ball.Place(HomeCenter(), BallSize);
        ball.PopOut(null);
        After(BallWindow.OutMs / 2, run, () => BeginGrow(open: false));
    }

    // ===== G1: the ball, the gaps and the page =====

    /// <summary>The gap from the ball in to the grow, and the pulse of a ball that waits for the page (G1 choices).</summary>
    public const int GapMs = 60, PulseMs = 500;

    readonly BallWindow ball = new();
    readonly GrowShadow shadow = new();
    int sequence;
    bool starting;

    /// <summary>The home ball of the show and the hide. The self-test reads it.</summary>
    public BallWindow Ball => ball;

    /// <summary>The shadow that grows with the widget. The self-test reads it.</summary>
    public GrowShadow GrowShadow => shadow;

    // The home button of the page: 62 CSS px, its center 54 CSS px above the bottom and in the middle of the page width less the
    // 6 px scroll bar gutter (app.css .nav2 and .nav2 .home). The page measures it after each grow, so a layout change follows.
    PointF homeCss = new(-1, -1);

    float BallSize => 62 * DeviceDpi / 96f;

    Point HomeCenter()
    {
        var scale = DeviceDpi / 96f;
        var css = homeCss.X >= 0 ? homeCss : new PointF((ClientSize.Width / scale - 6) / 2, ClientSize.Height / scale - 54);
        var client = PointToScreen(Point.Empty);
        return new Point(client.X + (int)Math.Round(css.X * scale), client.Y + (int)Math.Round(css.Y * scale));
    }

    async void MeasureHome()
    {
        if (Web is not { } web) return;
        try
        {
            var json = await web.ExecuteScriptAsync("(h => h ? (r => [r.left + r.width / 2, r.top + r.height / 2])(h.getBoundingClientRect()) : null)(document.querySelector('.nav2 .home'))");
            if (System.Text.Json.JsonSerializer.Deserialize<float[]>(json) is [var x, var y]) homeCss = new PointF(x, y);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException)
        {
        }
    }

    /// <summary>Runs a script in the page, if the page is there. The page ignores a class that it does not know.</summary>
    void Page(string script)
    {
        if (pageReady && Web is { } web) _ = web.ExecuteScriptAsync(script);
    }

    /// <summary>Runs <paramref name="then"/> after <paramref name="ms"/>, if no later show or hide started.</summary>
    void After(int ms, int run, Action then) =>
        _ = Task.Delay(ms).ContinueWith(_ => BeginInvoke(() => { if (run == sequence) then(); }), TaskScheduler.Default);

    /// <summary>T4: the widget shrinks and hides, then its minimized form (the edge tab, N1) slides in.</summary>
    public void Minimize()
    {
        if (!Visible || closing) return;
        minimizing = true;
        HideWidget();
    }

    bool minimizing;

    /// <summary>The widget is hidden after a minimize: the tray app shows the edge tab.</summary>
    public event Action? Minimized;

    /// <summary>The widget starts to show: the tray app hides the edge tab.</summary>
    public event Action? Showing;

    public void Toggle()
    {
        if (Visible && !closing) HideWidget();
        else ShowWidget();
    }

    // ===== Grow and shrink =====
    // The widget grows from height 0 at its bottom edge to its full height, and shrinks back to hide.
    // The window keeps its size and place. A window region shows only the bottom part, so the page does not
    // lay out again on each frame. At the end the region goes, and the rounded corners of the system come back.

    /// <summary>The time of the grow at show, in milliseconds.</summary>
    public const int GrowMs = 280;

    /// <summary>The time of the shrink at hide, in milliseconds.</summary>
    public const int ShrinkMs = 200;

    readonly System.Windows.Forms.Timer grow = new() { Interval = 10 };
    readonly System.Diagnostics.Stopwatch growClock = new();
    bool pageReady;
    bool closing;

    /// <summary>The shown height of each frame of the last grow or shrink, in pixels. The self-test reads it.</summary>
    public List<int> GrowFrames { get; } = [];

    /// <summary>True while the widget shows or hides: the ball, the gap, the grow or the shrink.</summary>
    public bool Growing => grow.Enabled || starting || closing;

    /// <summary>True from the start of a hide (or a minimize) to the end of the shrink.</summary>
    public bool Hiding => Visible && closing;

    /// <summary>The widget is hidden at the end of a hide or a minimize.</summary>
    public event Action? Hidden;

    /// <summary>True while the window region grows or shrinks.</summary>
    public bool Resizing => grow.Enabled;

    void BeginGrow(bool open)
    {
        closing = !open;
        GrowFrames.Clear();
        Frame(false);
        // G1: the page fades in with the grow, and the app bar unfolds sideways from the ball (tray.css, "grow-in").
        if (open) Page("(d => { d.classList.add('grow-in'); setTimeout(() => d.classList.remove('grow-in'), 400); })(document.documentElement)");
        growClock.Restart();
        grow.Start();
        if (ball.Visible) ball.ToTop();
        GrowStep();
    }

    void GrowStep()
    {
        var time = closing ? ShrinkMs : GrowMs;
        var t = Math.Min(1.0, growClock.Elapsed.TotalMilliseconds / time);
        // Grow: ease out (fast start, soft stop). Shrink: ease in (soft start, fast end).
        var part = closing ? 1 - t * t * t : 1 - Math.Pow(1 - t, 3);
        var shown = (int)Math.Round(Height * part);
        GrowFrames.Add(shown);
        if (t < 1)
        {
            Reveal(shown);
            shadow.Track(this, shown);
            return;
        }
        grow.Stop();
        if (closing)
        {
            closing = false;
            shadow.Hide();
            Hide();
            Hidden?.Invoke();
            if (minimizing)
            {
                minimizing = false;
                Minimized?.Invoke();
            }
        }
        else
        {
            // The handoff: the home button of the page takes the place of the ball; the shadow of the system comes back.
            starting = false;
            shadow.Track(this, Height);
            shadow.FadeOut();
            Page("document.documentElement.classList.remove('ball')");
            ball.HideNow();
            MeasureHome();
        }
        // No region: the full window with the rounded corners, the border and the shadow of the system.
        _ = SetWindowRgn(Handle, 0, true);
        Frame(true);
    }

    /// <summary>Shows only the bottom <paramref name="height"/> pixels of the window, with rounded corners.</summary>
    void Reveal(int height)
    {
        var radius = (int)(16 * DeviceDpi / 96f);
        var region = CreateRoundRectRgn(0, Height - height, Width + 1, Height + 1, radius, radius);
        // The system owns the region after the call.
        _ = SetWindowRgn(Handle, region, true);
    }

    // ===== Drag =====
    // The widget follows the pointer with a fixed offset: the point under the pointer at the mouse down stays under the pointer.
    // The system move loop (WM_NCLBUTTONDOWN with HTCAPTION) starts only after the drag threshold, so it landed some pixels off.

    readonly System.Windows.Forms.Timer dragTimer = new() { Interval = 8 };
    Size dragOffset;

    /// <summary>True while a drag of the header moves the widget.</summary>
    public bool Dragging => dragTimer.Enabled;

    /// <summary>
    /// Starts a drag of the window. <paramref name="x"/> and <paramref name="y"/> are the CSS pixels of the mouse down in the page,
    /// so the drag keeps that point under the pointer also when the event comes late.
    /// </summary>
    public void BeginDrag(double x, double y)
    {
        // The mouse event comes from the page after a round trip: after a fast click the button is up again.
        if (MouseButtons != MouseButtons.Left || dragTimer.Enabled) return;
        var scale = DeviceDpi / 96f;
        var client = PointToScreen(Point.Empty);
        var down = new Point(client.X + (int)Math.Round(x * scale), client.Y + (int)Math.Round(y * scale));
        // A CSS pixel is more than one screen pixel at a high DPI. If the pointer did not move since the mouse down,
        // its own position is exact; else the page point is the best value.
        var now = Cursor.Position;
        var still = Math.Abs(now.X - down.X) <= Math.Ceiling(scale) && Math.Abs(now.Y - down.Y) <= Math.Ceiling(scale);
        dragOffset = (Size)(still ? now : down) - (Size)Location;
        dragTimer.Start();
    }

    void DragStep()
    {
        if (MouseButtons == MouseButtons.Left)
        {
            Location = Cursor.Position - dragOffset;
            return;
        }
        dragTimer.Stop();
        Location = Cursor.Position - dragOffset;
        // A drop on the edge of the work area keeps the margin, so the widget never touches the taskbar.
        Location = Fit(Location);
        SavePlace(Location);
    }

    void SavePlace(Point point)
    {
        place = point;
        if (placeFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(placeFile)!);
            File.WriteAllText(placeFile, System.Text.Json.JsonSerializer.Serialize(new SavedPlace(point.X, point.Y)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
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

    // The saved place, moved into the work area of the screen nearest to it. A place on a screen that is gone (a monitor
    // that is off, or a smaller screen) comes back onto a current screen. The margin keeps a gap from each edge.
    Point Start() => place is { } p ? Fit(p) : HomeLocation();

    /// <summary>Sets the saved place without a drag. The self-test uses it for a place on a screen that is gone.</summary>
    public void UsePlace(Point point) => SavePlace(point);

    // A monitor that goes away while the widget shows: move the widget onto a current screen.
    void DisplayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (Visible && !Growing && !Dragging) Location = Fit(Location);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            ball.Dispose();
            shadow.Dispose();
        }
        base.Dispose(disposing);
    }

    int Gap => (int)(12 * DeviceDpi / 96f);

    /// <summary>The point moved into the work area of its screen (the nearest screen for a point off all screens), with the margin on each side.</summary>
    public Point Fit(Point point)
    {
        var area = Screen.FromRectangle(new Rectangle(point, Size)).WorkingArea;
        var m = Gap;
        return new Point(
            Math.Clamp(point.X, area.Left + m, Math.Max(area.Left + m, area.Right - Width - m)),
            Math.Clamp(point.Y, area.Top + m, Math.Max(area.Top + m, area.Bottom - Height - m)));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Frame(true);
    }

    /// <summary>
    /// The frame of the system: rounded corners and the shadow, with no border line. The frame always has the full window size,
    /// so the grow turns it off and shows the rounded window region only.
    /// </summary>
    void Frame(bool on)
    {
        var round = on ? DwmwcpRound : DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
        var color = DwmwaColorNone;
        _ = DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref color, sizeof(int));
        var policy = on ? DwmncrpUseWindowStyle : DwmncrpDisabled;
        _ = DwmSetWindowAttribute(Handle, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Size = Scaled();
        Location = Start();
    }

    // No maximize from Win+Up or a drag to the top edge: the widget keeps its fixed size.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScMaximize) return;
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideWidget();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>The widget size at the DPI. The height is at most the work area height less the gap above and below.</summary>
    public Size Scaled()
    {
        var scale = DeviceDpi / 96f;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var height = Math.Min((int)(LogicalHeight * scale), area.Height - 2 * Gap);
        return new Size((int)(LogicalWidth * scale), height);
    }

    /// <summary>Bottom right of the work area, above the tray, with a margin of 12 logical pixels.</summary>
    public Point HomeLocation()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return new Point(area.Right - Width - Gap, area.Bottom - Height - Gap);
    }

    const int WmSysCommand = 0x112;
    const int ScMaximize = 0xF030;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwcpRound = 2;
    const int DwmwcpDoNotRound = 1;
    const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    const int DwmwaNcRenderingPolicy = 2;
    const int DwmncrpUseWindowStyle = 0;
    const int DwmncrpDisabled = 1;

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
