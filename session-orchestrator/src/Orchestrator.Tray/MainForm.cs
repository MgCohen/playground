using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Web.Components;

namespace Orchestrator.Tray;

/// <summary>
/// The desktop window: a normal resizable window with the BlazorWebView. It shows the Routes of Orchestrator.Web.
/// Close hides the window. Only Exit in the tray menu stops the app.
/// </summary>
public sealed class MainForm : Form
{
    public static readonly Size LogicalSize = new(1280, 800);

    readonly BlazorWebView view;

    public MainForm(IServiceProvider services, Icon icon)
    {
        Text = "Orchestrator (mock data)";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = Scaled(new Size(480, 400));
        Size = Scaled(LogicalSize);
        BackColor = Color.FromArgb(0x11, 0x16, 0x1c);
        view = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        view.RootComponents.Add<Routes>("#app");
        view.RootComponents.Add<HeadOutlet>("head::after");
        Controls.Add(view);
    }

    /// <summary>The WebView2 control after its start. The self-test uses it for the DOM checks and the screenshots.</summary>
    public CoreWebView2? Web => view.WebView.CoreWebView2;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    public void ShowWindow()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
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

    Size Scaled(Size logical)
    {
        var scale = DeviceDpi / 96f;
        return new Size((int)(logical.Width * scale), (int)(logical.Height * scale));
    }
}
