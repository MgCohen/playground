using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Web.Components;

namespace Orchestrator.Tray;

/// <summary>
/// The widget: 350 × 560 logical pixels, no title bar, rounded corners, a thin border, not in the taskbar.
/// It opens in the corner above the tray, and it holds the BlazorWebView with the A4 views of Orchestrator.Web.
/// A drag of the window is not possible. Close hides the widget. Only Exit in the tray menu stops the app.
/// </summary>
public sealed partial class WidgetForm : Form
{
    public const int LogicalWidth = 350;
    public const int LogicalHeight = 560;

    static readonly Color Border = Color.FromArgb(0x5a, 0x58, 0x54);

    readonly BlazorWebView view;

    public WidgetForm(IServiceProvider services, Icon icon, bool pinned)
    {
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
        Location = HomeLocation();
    }

    /// <summary>The WebView2 control after its start. The self-test uses it for the DOM checks and the screenshots.</summary>
    public CoreWebView2? Web => view.WebView.CoreWebView2;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    /// <summary>Shows the widget at its home location in the corner above the tray.</summary>
    public void ShowWidget()
    {
        if (!Visible)
        {
            Location = HomeLocation();
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
        Location = HomeLocation();
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

    const int WmSysCommand = 0x112;
    const int ScMaximize = 0xF030;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwcpRound = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
