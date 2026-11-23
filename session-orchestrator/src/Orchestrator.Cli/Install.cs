using System.Reflection;
using Orchestrator.Store;

namespace Orchestrator.Cli;

public static class Install
{
    public static readonly string[] Sources = ["src/Orchestrator.Cli", "src/Orchestrator.Store", "src/Orchestrator.Core"];

    public const string UpdateCommand = "dotnet pack src/Orchestrator.Cli -c Release -o <folder>, then dotnet tool update -g Orchestrator.Cli --add-source <folder>";

    public static string? Commit =>
        typeof(Install).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+') is [_, { Length: > 0 } commit] ? commit : null;

    /// <summary>The installed tray and supervisor (install.ps1): the commit of the copy against the source of the home.</summary>
    public static (bool Ok, string Detail) CheckApp(Shell home, string appRoot)
    {
        if (SupervisorHost.Installed(appRoot) is not { } app) return (true, $"not installed in {appRoot}");
        var shortCommit = app.Commit[..Math.Min(8, app.Commit.Length)];
        var newer = home.Run("git", ["log", "--format=%h", $"{app.Commit}..HEAD", "--", "src"]);
        if (!newer.Ok) return (false, $"{appRoot}: commit {shortCommit} is not in {home.Cwd}. Update: install.ps1");
        var count = newer.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return count == 0
            ? (true, $"{appRoot}: built from {shortCommit}, store {app.Store}, port {app.Port}")
            : (false, $"{appRoot}: built from {shortCommit}, {count} newer commits change src/. Update: install.ps1");
    }

    public static (bool Ok, string Detail) Check(Shell home, string? commit)
    {
        if (commit is null) return (false, $"the build has no source commit. Update: {UpdateCommand}");
        var shortCommit = commit[..Math.Min(8, commit.Length)];
        var newer = home.Run("git", ["log", "--format=%h", $"{commit}..HEAD", "--", .. Sources]);
        if (!newer.Ok) return (false, $"commit {shortCommit} is not in {home.Cwd}. Update: {UpdateCommand}");
        var count = newer.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return count == 0
            ? (true, $"built from {shortCommit}, no newer change in the source")
            : (false, $"built from {shortCommit}, {count} newer commits change the source. Update: {UpdateCommand}");
    }
}
