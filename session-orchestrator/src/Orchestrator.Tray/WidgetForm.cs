using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Web.Components;

namespace Orchestrator.Tray;

/// <summary>
/// The widget: 350 × 560 logical pixels, no title bar, rounded corners, a thin border, not in the taskbar.
/// It holds the BlazorWebView with the A4 views of Orchestrator.Web. A drag of the page header moves it. The saved place
/// (place.json) keeps its position for the next show and the next start; without a saved place it opens in the corner above the tray.
/// Close hides the widget. Only Exit in the tray menu stops the app.
/// </summary>
public sealed partial class WidgetForm : Form
{
    public const int LogicalWidth = 350;
    public const int LogicalHeight = 560;

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
        if (!Visible)
        {
            Location = Start();
            Show();
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    public void Toggle()
    {
        if (Visible) Hide();
        else ShowWidget();
    }

    /// <summary>Starts a drag of the window, as a drag of a title bar. The call returns when the mouse button is up.</summary>
    public void BeginDrag()
    {
        // The mouse event comes from the page after a round trip: after a fast click the button is up again.
        if (MouseButtons != MouseButtons.Left) return;
        var before = Location;
        ReleaseCapture();
        _ = SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        if (Location != before) SavePlace(Location);
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
    Point Start() =>
        place is { } p && Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Rectangle(p, Size))) ? p : HomeLocation();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = DwmwcpRound;
        _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
        var color = Border.R | (Border.G << 8) | (Border.B << 16);
        _ = DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref color, sizeof(int));
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
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    Size Scaled()
    {
        var scale = DeviceDpi / 96f;
        return new Size((int)(LogicalWidth * scale), (int)(LogicalHeight * scale));
    }

    /// <summary>Bottom right of the work area, above the tray, with a margin of 12 logical pixels.</summary>
    public Point HomeLocation()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var margin = (int)(12 * DeviceDpi / 96f);
        return new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }

    const int WmNcLButtonDown = 0xA1;
    const int HtCaption = 2;
    const int WmSysCommand = 0x112;
    const int ScMaximize = 0xF030;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwcpRound = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
