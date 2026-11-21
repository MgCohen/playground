using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Store;
using Orchestrator.Supervisor;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The start of the tray app. Usage: Orchestrator.Tray.exe [--instance &lt;name&gt;] [--slot &lt;n&gt;] [--hidden] [--pin] [--selftest &lt;folder&gt; [--mouse | --demo | --scenario &lt;name&gt;] [--reduced] [--label &lt;code&gt;] [--slot-wait &lt;s&gt;] [--wait-recorder]] [--live &lt;store&gt; [--port &lt;port&gt;] [--no-supervisor]].
/// A self-test takes a free test slot (<see cref="Slot"/>): its own lock, WebView2 folder and strip of the screen. It writes &lt;folder&gt;/instance.json with the slot and the area to record.
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
        // --slot <n>: the strip of the screen for this tray (Slot). A self-test takes the first free slot when there is no --slot.
        var wanted = int.TryParse(Value(args, "--slot"), out var ws) && ws >= 0 ? ws : (int?)null;
        // The slot size uses the screen in real pixels, so the DPI mode comes before the slot.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        SingleInstance? instance;
        if (selfTest is not null)
        {
            // I17: each self-test has its own slot: its own lock, WebView2 folder and place on the screen. Two sessions can test at the same time.
            Directory.CreateDirectory(selfTest);
            ConsoleCopy.Start(Path.Combine(selfTest, "console.txt"));
            var wait = TimeSpan.FromSeconds(int.TryParse(Value(args, "--slot-wait"), out var sw) && sw >= 0 ? sw : 120);
            if (Slot.Take(() => tray?.ShowFromAnyThread(), wanted, wait) is not { } taken)
            {
                var error = $"orch tray: no free test slot after {wait.TotalSeconds:0} s. Other self-test trays use all slots.";
                Console.Error.WriteLine(error);
                File.WriteAllText(Path.Combine(selfTest, "selftest-error.txt"), error);
                return 1;
            }
            (Slot.Index, instance) = (taken.Index, taken.Lock);
            Slot.WriteInstanceFile(selfTest, taken.Index);
            Console.WriteLine($"orch tray: test slot {taken.Index}, area {Slot.RecordArea()}");
            // --wait-recorder: a recorder with --area-file writes instance.json.ready when it records. Then the windows show.
            if (args.Contains("--wait-recorder")) WaitForRecorder(Path.Combine(selfTest, "instance.json.ready"), TimeSpan.FromSeconds(15));
        }
        else
        {
            instance = SingleInstance.TryStart(false, () => tray?.ShowFromAnyThread(), named);
            if (instance is null)
            {
                Console.WriteLine("The tray app already runs. Its window comes to the front.");
                return 0;
            }
            Slot.Index = wanted;
        }
        using var held = instance;

        // An installed app has the templates and the policy next to the exe.
        var home = Path.Combine(AppContext.BaseDirectory, "home");
        if (Directory.Exists(Path.Combine(home, "templates"))) Environment.SetEnvironmentVariable("ORCH_HOME", home);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator.Tray");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(data,
            selfTest is not null ? $"WebView2.selftest.slot{Slot.Index}" : "WebView2" + (named is null ? "" : "." + named)));

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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var live = Value(args, "--live") ?? (args.Contains("--live") ? Home.StoreRoot() : null);
        using var mock = live is null ? MockStore.Create() : null;
        var store = live is null ? mock!.Store : new FileStore(live, writer: TrayWriter);
        // A named instance or a self-test with no port gets a free port, so its supervisor does not meet the supervisor of another tray.
        var port = int.TryParse(Value(args, "--port") ?? Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p
            : named is not null || selfTest is not null ? FreePort() : 7777;
        var label = live is null ? "mock data" : $"live: {store.Root}, port {port}";
        if (live is not null) StartSupervisor(store.Root, port, start: !args.Contains("--no-supervisor"));
        Console.WriteLine($"orch tray: {label}");
        using var services = Services(store, live is null ? MockStore.MockPolicy : store.Policy, ticks: live is null);
        // Each tray shows its code and build in the corner of the panel and in the icon tip, at all times: "I17 · 3da7df8".
        var build = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion is { } v && v.Contains('+')
            ? v[(v.IndexOf('+') + 1)..] is { Length: >= 7 } sha ? sha[..7] : v
            : File.GetLastWriteTime(Environment.ProcessPath ?? AppContext.BaseDirectory).ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var runCode = RunCode(args, named, selfTest is not null);
        var corner = $"{runCode} · {build}";
        services.GetRequiredService<WidgetWindow>().Label = corner;
        SelfTest.ExpectedLabel = corner;
        label = $"{corner}, {label}";
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
                    // --reduced (or a scenario name that ends with -reduced): both pages get prefers-reduced-motion: reduce before the first step.
                    if (args.Contains("--reduced") || Value(args, "--scenario")?.EndsWith("-reduced", StringComparison.Ordinal) == true) await Task.WhenAll(SelfTest.Reduced(tray.Window), SelfTest.Reduced(tray.Badge));
                    code = args.Contains("--scenario") ? await SelfTest.Scenario(tray, selfTest, Value(args, "--scenario"))
                        : args.Contains("--stress") ? await SelfTest.Stress(tray, selfTest, Value(args, "--css"), args.Contains("--opens"), args.Contains("--sheets"))
                        : args.Contains("--demo") ? await SelfTest.Demo(tray, selfTest) : await SelfTest.Run(tray, selfTest, args.Contains("--mouse"), args.Contains("--reduced"));
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
        if (selfTest is not null)
        {
            var kind = args.Contains("--scenario") ? "scenario" : args.Contains("--stress") ? "stress" : args.Contains("--demo") ? "demo" : args.Contains("--mouse") ? "selftest-mouse" : "selftest";
            RunRecord.Write(kind, Value(args, "--scenario") ?? Path.GetFileName(selfTest), runCode, Slot.Index, build, code);
        }
        return code;
    }

    /// <summary>
    /// The code of the run in the label: --label, else the --instance name, else the RUN_CODE environment value, else "tray" for
    /// the normal tray and "test" for a self-test. Only letters, digits, "-" and "_", at most 24 characters.
    /// </summary>
    static string RunCode(string[] args, string? named, bool selfTest)
    {
        var given = Value(args, "--label") ?? named ?? Environment.GetEnvironmentVariable("RUN_CODE");
        var clean = given is null ? "" : new string(given.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(24).ToArray());
        return clean.Length > 0 ? clean : selfTest ? "test" : "tray";
    }

    /// <summary>Waits for the file of the recorder. With no file at the end of the wait, the run continues with a warning.</summary>
    static void WaitForRecorder(string file, TimeSpan wait)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(file) && clock.Elapsed < wait) Thread.Sleep(50);
        if (!File.Exists(file)) Console.Error.WriteLine($"orch tray: warning: no recorder after {wait.TotalSeconds:0} s ({file}). The run continues.");
    }

    /// <summary>A free TCP port on the loopback address, from the system.</summary>
    static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
        // The Umpire modules of Orchestrator.Web, as in the web server. The tray has no configuration of its own.
        Orchestrator.Web.App.Host.OrchHost.Add(services, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
