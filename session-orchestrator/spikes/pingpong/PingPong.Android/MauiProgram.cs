using Microsoft.AspNetCore.Components.WebView.Maui;
using PingPong.Core;
using PingPong.Ui;

namespace PingPong.Mobile;

public static class MauiProgram
{
    public static Head Host { get; } = new();

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        // 10.0.2.2 is the PC, seen from the emulator.
        builder.Services.AddSingleton(new GameClient("http://10.0.2.2:7931/", "android"));
        builder.Services.AddSingleton(Host);
        return builder.Build();
    }
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        return new Window(new ContentPage { Content = view, SafeAreaEdges = SafeAreaEdges.All }) { Title = "Ping pong" };
    }
}
