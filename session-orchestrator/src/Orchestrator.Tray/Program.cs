using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The start of the tray app. Usage: Orchestrator.Tray.exe [--hidden] [--selftest &lt;folder&gt;].
/// The window shows mock data from a temporary store. The supervisor object only serves the actions of the views: no tick service runs.
/// </summary>
public static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // A start from a terminal (tray.ps1) writes its output to that terminal. A shortcut start has no console.
        _ = AttachConsole(-1);
        var at = Array.IndexOf(args, "--selftest");
        var selfTest = at >= 0 ? Path.GetFullPath(at + 1 < args.Length ? args[at + 1] : "selftest") : null;
        TrayApp? tray = null;
        using var instance = SingleInstance.TryStart(selfTest is not null, () => tray?.ShowFromAnyThread());
        if (instance is null)
        {
            Console.WriteLine("The tray app already runs. Its window comes to the front.");
            return selfTest is null ? 0 : 1;
        }

        // An installed app has the templates and the policy next to the exe.
        var home = Path.Combine(AppContext.BaseDirectory, "home");
        if (Directory.Exists(Path.Combine(home, "templates"))) Environment.SetEnvironmentVariable("ORCH_HOME", home);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator.Tray");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(data, selfTest is null ? "WebView2" : "WebView2.selftest"));

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mock = MockStore.Create();
        using var services = Services(mock.Store);
        tray = new TrayApp(services, show: selfTest is not null || !args.Contains("--hidden"));
        var code = 0;
        if (selfTest is not null)
        {
            tray.Form.Shown += async (_, _) =>
            {
                try
                {
                    code = await SelfTest.Run(tray.Form, selfTest);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"FAIL self-test: {e}");
                    code = 1;
                }
                tray.Exit();
            };
        }
        Application.Run(tray);
        tray.Dispose();
        return code;
    }

    static ServiceProvider Services(FileStore store)
    {
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(store);
        services.AddSingleton(new SupervisorOptions(1, 1, 1, MockStore.MockPolicy));
        services.AddSingleton<ITurnRunner, NoRunner>();
        services.AddSingleton<IDocsChecker, NoDocs>();
        services.AddSingleton<Supervisor.Supervisor>();
        services.AddSingleton<Board>();
        services.AddSingleton<StreamHub>();
        return services.BuildServiceProvider();
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
