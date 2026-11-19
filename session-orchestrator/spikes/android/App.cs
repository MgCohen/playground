using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Orchestrator.Web.Mobile;

/// <summary>The MAUI application: one window with one page.</summary>
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage()) { Title = "Orchestrator" };
}

/// <summary>
/// The page with the BlazorWebView. The same root components as the tray (Orchestrator.Tray/WidgetForm.cs).
/// </summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(AndroidRoutes) });
        view.RootComponents.Add(new RootComponent { Selector = "head::after", ComponentType = typeof(HeadOutlet) });
        Content = view;
    }
}
