using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;

namespace PingPong.Win;

/// <summary>A normal window with a BlazorWebView (windowed hosting).</summary>
public class PageForm : Form
{
    readonly BlazorWebView view;

    public CoreWebView2? Web => view.WebView.CoreWebView2;

    public PageForm(IServiceProvider services, Type root, string selector = "#app")
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        view = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        view.RootComponents.Add(new RootComponent(selector, root, null));
        Controls.Add(view);
    }
}
