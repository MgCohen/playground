using System.Diagnostics;
using Orchestrator.Store.Local;

namespace Orchestrator.Store;

/// <summary>The record of a supervisor process that the tray or orch started: one file for each port.</summary>
public sealed record SupervisorRecord(int Pid, int Port, string Store, string Command, string Log, string Started);

/// <summary>
/// Starts, finds and stops the supervisor process of a store. The tray and `orch supervisor` use the same code.
/// The process runs on its own: it stays when the tray or orch exits, so a running turn continues.
/// </summary>
public static class SupervisorHost
{
    public const string Dll = "Orchestrator.Web.dll";

    /// <summary>The install root of the published copies. ORCH_APP overrides the default.</summary>
    public static string AppRoot() =>
        Environment.GetEnvironmentVariable("ORCH_APP") is { Length: > 0 } app ? Path.GetFullPath(app)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "Orchestrator");

    /// <summary>The folder of the records and the logs, out of the store and the install root.</summary>
    public static string DataRoot() =>
        Environment.GetEnvironmentVariable("ORCH_DATA") is { Length: > 0 } data ? Path.GetFullPath(data)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator");

    public static string RecordPath(int port) => Path.Combine(DataRoot(), $"supervisor-{port}.json");

    public static bool Answers(int port, TimeSpan? timeout = null)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
            return http.GetAsync($"http://127.0.0.1:{port}/api/sessions").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The command that runs the supervisor: the published copy (first match of the candidates, then the install root),
    /// else `dotnet run` of the source. The source start builds first and locks bin/, so it is the last choice.
    /// </summary>
    public static (string File, string[] Args) Command(params string?[] candidates)
    {
        foreach (var folder in candidates.Append(Path.Combine(AppRoot(), "supervisor")))
            if (folder is not null && File.Exists(Path.Combine(folder, Dll)))
                return ("dotnet", [Path.GetFullPath(Path.Combine(folder, Dll))]);
        var project = Path.Combine(Home.Root(), "src", "Orchestrator.Web");
        if (Directory.Exists(project)) return ("dotnet", ["run", "--project", project]);
        throw new FileNotFoundException($"no supervisor: no {Dll} in {Path.Combine(AppRoot(), "supervisor")} and no source in {project}. Run install.ps1");
    }

    public static SupervisorRecord? Record(int port)
    {
        var record = Files.ReadJson<SupervisorRecord>(RecordPath(port));
        return record is not null && Alive(record.Pid) ? record : null;
    }

    /// <summary>Starts the supervisor of the store on the port, if no supervisor answers there. Waits until it answers.</summary>
    public static (bool Started, SupervisorRecord? Record) Start(string store, int port, TimeSpan wait, params string?[] candidates)
    {
        if (Answers(port)) return (false, Record(port));
        var (file, args) = Command(candidates);
        Directory.CreateDirectory(DataRoot());
        var log = Path.Combine(DataRoot(), $"supervisor-{port}.log");
        var line = $"{Quote(file)} {string.Join(' ', args.Select(Quote))} >> {Quote(log)} 2>&1";
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", line } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", line } };
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WorkingDirectory = DataRoot();
        info.Environment["ORCH_STORE"] = Path.GetFullPath(store);
        info.Environment["ORCH_PORT"] = port.ToString();
        // The log file keeps the supervisor lines and the warnings, not one line for each HTTP request.
        info.Environment.TryAdd("Logging__LogLevel__Microsoft.AspNetCore", "Warning");
        using var process = Process.Start(info) ?? throw new IOException($"could not start {file}");
        var record = new SupervisorRecord(process.Id, port, Path.GetFullPath(store), $"{file} {string.Join(' ', args)}", log,
            Core.Leases.Times.Format(DateTimeOffset.UtcNow));
        Files.WriteJson(RecordPath(port), record);
        var until = DateTimeOffset.UtcNow + wait;
        while (DateTimeOffset.UtcNow < until)
        {
            if (Answers(port)) return (true, record);
            if (process.HasExited) throw new IOException($"the supervisor stopped at once (exit {process.ExitCode}). See {log}");
            Thread.Sleep(500);
        }
        throw new TimeoutException($"the supervisor did not answer on port {port} in {wait.TotalSeconds:0} s. See {log}");
    }

    /// <summary>Stops the supervisor that the record of the port names, with its child processes.</summary>
    public static SupervisorRecord? Stop(int port)
    {
        var record = Record(port);
        if (record is not null)
        {
            using var process = Process.GetProcessById(record.Pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        File.Delete(RecordPath(port));
        return record;
    }

    static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    /// <summary>The install record that install.ps1 writes next to the published copies.</summary>
    public sealed record InstallRecord(string Commit, string Store, int Port, string Installed);

    public static InstallRecord? Installed(string? root = null) => Files.ReadJson<InstallRecord>(Path.Combine(root ?? AppRoot(), "install.json"));
}
