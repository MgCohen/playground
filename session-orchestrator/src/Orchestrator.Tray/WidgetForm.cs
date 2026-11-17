using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Web.Components;

namespace Orchestrator.Tray;

/// <summary>
/// The widget: 350 × 720 logical pixels (lower on a small work area), no title bar, rounded corners, a thin border, not in the taskbar.
/// It holds the BlazorWebView with the A4 views of Orchestrator.Web. A drag of the page header moves it. The saved place
/// (place.json) keeps its position for the next show and the next start; without a saved place it opens in the corner above the tray.
/// Close hides the widget. Only Exit in the tray menu stops the app. The widget grows from its bottom edge when it shows,
/// and shrinks to its bottom edge when it hides.
/// </summary>
public sealed partial class WidgetForm : Form
{
    public const int LogicalWidth = 350;
    public const int LogicalHeight = 720;

    static readonly Color Border = Color.FromArgb(0x5a, 0x58, 0x54);

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
        // The 1-pixel padding shows the back color as the thin border around the web view.
        BackColor = Border;
        Padding = new Padding(1);
        view = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        view.RootComponents.Add<Routes>("#app");
        view.RootComponents.Add<HeadOutlet>("head::after");
        Controls.Add(view);
        Size = Scaled();
        Location = Start();
        grow.Tick += (_, _) => GrowStep();
        // The first show waits for the page, so the widget does not grow over an empty web view.
        view.BlazorWebViewInitialized += (_, e) => e.WebView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            pageReady = true;
            if (growWaits) BeginGrow(open: true);
        };
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

    /// <summary>Shows the widget at its home location in the corner above the tray.</summary>
    public void ShowWidget()
    {
        if (!Visible || closing)
        {
            if (!Visible) Location = Start();
            Frame(false);
            Reveal(0);
            Show();
            if (pageReady) BeginGrow(open: true);
            else
            {
                growWaits = true;
                // A page that never reports its load still shows after 3 s.
                _ = Task.Delay(3000).ContinueWith(_ => BeginInvoke(() => { if (growWaits) BeginGrow(open: true); }), TaskScheduler.Default);
            }
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>Hides the widget: it shrinks to the bottom edge, then it hides.</summary>
    public void HideWidget()
    {
        if (!Visible || closing) return;
        BeginGrow(open: false);
    }

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
    bool growWaits;
    bool closing;

    /// <summary>The shown height of each frame of the last grow or shrink, in pixels. The self-test reads it.</summary>
    public List<int> GrowFrames { get; } = [];

    /// <summary>True while the widget grows or shrinks.</summary>
    public bool Growing => grow.Enabled;

    void BeginGrow(bool open)
    {
        growWaits = false;
        closing = !open;
        GrowFrames.Clear();
        Frame(false);
        growClock.Restart();
        grow.Start();
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
            return;
        }
        grow.Stop();
        if (closing)
        {
            closing = false;
            Hide();
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

    /// <summary>Starts a drag of the window, as a drag of a title bar. The call returns when the mouse button is up.</summary>
    public void BeginDrag()
    {
        // The mouse event comes from the page after a round trip: after a fast click the button is up again.
        if (MouseButtons != MouseButtons.Left) return;
        var before = Location;
        ReleaseCapture();
        _ = SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        if (Location == before) return;
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

    // The saved place, if the full widget is on a screen. A place on a screen that is not there now gives the home location.
    // The place moves in by the margin from the edges of its work area, so an old place on the taskbar edge gets the gap.
    Point Start() =>
        place is { } p && Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Rectangle(p, Size))) ? Fit(p) : HomeLocation();

    int Gap => (int)(12 * DeviceDpi / 96f);

    /// <summary>The point moved into the work area of its screen, with the margin on each side.</summary>
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
    /// The frame of the system: rounded corners, the border color and the shadow. The frame always has the full window size,
    /// so the grow turns it off and shows the rounded window region only.
    /// </summary>
    void Frame(bool on)
    {
        var round = on ? DwmwcpRound : DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
        var color = on ? Border.R | (Border.G << 8) | (Border.B << 16) : DwmwaColorNone;
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

    const int WmNcLButtonDown = 0xA1;
    const int HtCaption = 2;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
