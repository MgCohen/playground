using Microsoft.Extensions.Logging;
using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Tray;

namespace Orchestrator.Web.Mobile;

/// <summary>
/// The start of the Android host. The services are the same as in the tray mock mode (Orchestrator.Tray/Program.cs, Services).
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        HomeFolder.Write(FileSystem.AppDataDirectory);
        var mock = MockStore.Create();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton(mock);
        builder.Services.AddSingleton(mock.Store);
        // Mock mode: a tick after an action relays and applies the policy on the mock store. NoRunner starts no engine process.
        builder.Services.AddSingleton(new SupervisorOptions(1, 1, 1, MockStore.MockPolicy) { Ticks = true });
        builder.Services.AddSingleton<ITurnRunner, NoRunner>();
        builder.Services.AddSingleton<IDocsChecker, NoDocs>();
        builder.Services.AddSingleton<Supervisor.Supervisor>();
        builder.Services.AddSingleton<Board>();
        builder.Services.AddScoped<PathTrail>();
        builder.Services.AddSingleton<StreamHub>();
        // No IWidgetWindow: the components work with no desktop window (the header shows no pin).
        return builder.Build();
    }
}
