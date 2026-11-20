using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The start of the tray app. Usage: Orchestrator.Tray.exe [--instance &lt;name&gt;] [--hidden] [--pin] [--selftest &lt;folder&gt; [--mouse | --demo | --scenario &lt;name&gt;] [--reduced]] [--live &lt;store&gt; [--port &lt;port&gt;] [--no-supervisor]].
/// Mock mode (the default): the window shows mock data from a temporary store.
/// Live mode: the window shows a real store. With no store after --live, the tray uses the default store root (<see cref="Home.StoreRoot"/>). The supervisor process of that store (on the port) does the ticks; the tray only reads and posts.
/// If no supervisor answers on the port, the tray starts one in its own process, unless --no-supervisor.
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
        // --instance <name>: a separate tray with its own lock, WebView2 folder and place file (a test next to another tray).
        var named = Value(args, "--instance") is { } n && n.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? n : null;
        using var instance = SingleInstance.TryStart(selfTest is not null, () => tray?.ShowFromAnyThread(), named);
        if (instance is null)
        {
            Console.WriteLine("The tray app already runs. Its window comes to the front.");
            return selfTest is null ? 0 : 1;
        }

        // An installed app has the templates and the policy next to the exe.
        var home = Path.Combine(AppContext.BaseDirectory, "home");
        if (Directory.Exists(Path.Combine(home, "templates"))) Environment.SetEnvironmentVariable("ORCH_HOME", home);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator.Tray");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(data, (selfTest is null ? "WebView2" : "WebView2.selftest") + (named is null ? "" : "." + named)));

        // V19: an unhandled exception goes to the log, with no dialog of Windows Forms. The tray keeps running.
        Faults.Log = selfTest is not null ? Path.Combine(selfTest, "exceptions.log") : Path.Combine(data, named is null ? "tray.log" : $"tray.{named}.log");
        // A named instance and the demo run write the trace of the window steps.
        Faults.Tracing = (named is not null && selfTest is null) || args.Contains("--demo");
        // The demo and stress runs click with window messages; --pointer uses the real pointer (it moves the mouse of the user).
        SelfTest.NoPointer = !args.Contains("--pointer");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Faults.Add(e.Exception, "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Faults.Add(e.ExceptionObject as Exception, "process");
        TaskScheduler.UnobservedTaskException += (_, e) => { Faults.Add(e.Exception, "task"); e.SetObserved(); };
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var live = Value(args, "--live") ?? (args.Contains("--live") ? Home.StoreRoot() : null);
        using var mock = live is null ? MockStore.Create() : null;
        var store = live is null ? mock!.Store : new FileStore(live, writer: TrayWriter);
        var port = int.TryParse(Value(args, "--port") ?? Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p : 7777;
        var label = live is null ? "mock data" : $"live: {store.Root}, port {port}";
        if (live is not null) StartSupervisor(store.Root, port, start: !args.Contains("--no-supervisor"));
        Console.WriteLine($"orch tray: {label}");
        using var services = Services(store, live is null ? MockStore.MockPolicy : store.Policy, ticks: live is null);
        // A named instance shows its name and build in the header and in the icon tip.
        if (named is not null)
        {
            var build = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion is { } v && v.Contains('+')
                ? v[(v.IndexOf('+') + 1)..] is { Length: >= 7 } sha ? sha[..7] : v
                : File.GetLastWriteTime(Environment.ProcessPath ?? AppContext.BaseDirectory).ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            services.GetRequiredService<WidgetWindow>().Label = $"{named} · {build}";
            label = $"{named} · {build}, {label}";
        }
        tray = new TrayApp(services, label, show: selfTest is not null || !args.Contains("--hidden"), pinned: selfTest is not null || args.Contains("--pin"),
            placeFile: selfTest is null ? Path.Combine(data, named is null ? "place.json" : $"place.{named}.json") : SelfTest.FreshPlaceFile(selfTest));
        var code = 0;
        if (selfTest is not null)
        {
            // The self-test starts when the page is there. --mouse adds the checks with real mouse input on the tray window; --demo runs
            // the flow with the real pointer for a screen recording.
            Task.WhenAll(tray.Window.Ready.Task, tray.Badge.Ready.Task).ContinueWith(_ => tray.Window.BeginInvoke(async () =>
            {
                try
                {
                    // --reduced: both pages get prefers-reduced-motion: reduce before the first step.
                    if (args.Contains("--reduced")) await Task.WhenAll(SelfTest.Reduced(tray.Window), SelfTest.Reduced(tray.Badge));
                    code = args.Contains("--scenario") ? await SelfTest.Scenario(tray, selfTest, Value(args, "--scenario"))
                        : args.Contains("--stress") ? await SelfTest.Stress(tray, selfTest, Value(args, "--css"), args.Contains("--opens"), args.Contains("--sheets"))
                        : args.Contains("--demo") ? await SelfTest.Demo(tray, selfTest) : await SelfTest.Run(tray, selfTest, args.Contains("--mouse"));
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"FAIL self-test: {e}");
                    // A start with no console (a redirect, a shortcut) still keeps the error.
                    File.WriteAllText(Path.Combine(selfTest, "selftest-error.txt"), e.ToString());
                    code = 1;
                }
                tray.Exit();
            }), TaskScheduler.Default);
        }
        Application.Run(tray);
        tray.Dispose();
        return code;
    }

    /// <summary>The writer id of the envelopes of the tray in live mode. The supervisor of the store writes with its own id.</summary>
    public const string TrayWriter = "tray";

    static string? Value(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : null;
    }

    /// <summary>
    /// Live mode: if no supervisor answers on the port, start one for the store in its own process, in the background.
    /// The published copy next to the tray (..\supervisor) comes first. The supervisor stays when the tray exits.
    /// </summary>
    static void StartSupervisor(string store, int port, bool start)
    {
        if (!start)
        {
            if (!SupervisorHost.Answers(port))
                Console.Error.WriteLine($"No supervisor answers on port {port}. The tray shows the store, but no tick delivers the answers. Start: ORCH_STORE={store} orch supervisor start --port {port}");
            return;
        }
        var sibling = Path.Combine(AppContext.BaseDirectory, "..", "supervisor");
        _ = Task.Run(() =>
        {
            try
            {
                var (started, record) = SupervisorHost.Start(store, port, TimeSpan.FromSeconds(120), sibling);
                Console.WriteLine(started ? $"orch tray: started the supervisor (pid {record!.Pid}, log {record.Log})" : $"orch tray: a supervisor answers on port {port}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"orch tray: could not start the supervisor: {e.Message}");
            }
        });
    }

    static ServiceProvider Services(FileStore store, Core.Policy.Policy policy, bool ticks)
    {
        var services = new ServiceCollection();
        services.AddBlazorWebView();
        services.AddSingleton(store);
        // Mock mode: a tick after an action relays and applies the policy on the mock store (NoRunner starts no engine process).
        // Live mode: no tick. The supervisor process of the store does it, so the tray never takes a lease or applies a default.
        services.AddSingleton(new SupervisorOptions(1, 1, 1, policy) { Ticks = ticks });
        services.AddSingleton<ITurnRunner, NoRunner>();
        services.AddSingleton<IDocsChecker, NoDocs>();
        services.AddSingleton<Supervisor.Supervisor>();
        services.AddSingleton<Board>();
        services.AddScoped<PathTrail>();
        services.AddSingleton<StreamHub>();
        services.AddSingleton<WidgetWindow>();
        services.AddSingleton<IWidgetWindow>(p => p.GetRequiredService<WidgetWindow>());
        services.AddSingleton<Orchestrator.Web.Components.Tray.TrayFlow>();
        return services.BuildServiceProvider();
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
