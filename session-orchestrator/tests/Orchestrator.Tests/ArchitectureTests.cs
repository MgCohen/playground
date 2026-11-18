using System.Text.RegularExpressions;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// Store IO is only in the store implementations (<c>src/Orchestrator.Store/Local/</c>). All other C# code uses the
/// repositories of <c>IRepositories</c>. File IO that is not store IO is in the list below, each with its reason.
/// </summary>
public sealed class ArchitectureTests
{
    const string Implementations = "src/Orchestrator.Store/Local/";

    /// <summary>A file call, a stream on a file, a file watcher, a store helper, or a store path.</summary>
    static readonly Regex Io = new(@"(?<![\w.])(File|Directory|Files|FileInfo|DirectoryInfo)\.|\bnew FileStream\b|\bFileSystemWatcher\b|\.PathOf\(|Path\.Combine\([^;]*StoreFiles\.");

    /// <summary>File IO that is not store IO: the file, the text of each allowed line (empty: all lines), and the reason.</summary>
    static readonly (string File, string[] Lines, string Reason)[] Allowed =
    [
        ("src/Orchestrator.Core/Templates/TemplateStore.cs", [], "known exception: the F1 template store reads <home>/templates and <store>/templates directly. It moves to IRepository in F2"),
        ("src/Orchestrator.Core/Templates/TemplateCheck.cs", [], "the spec files of the core templates in the repository (meta.schema.json, view.schema.json, looks.json)"),
        ("src/Orchestrator.Store/SupervisorHost.cs", [], "the supervisor process records and the install record of the app, not the store"),
        ("src/Orchestrator.Providers/ClaudeCode/AppRecords.cs", [], "the records of the desktop app, not the store"),
        ("src/Orchestrator.Providers/ClaudeCode/ClaudeCodeProvider.cs", ["File.ReadAllText(settings)", "plugin.json"], "the settings of Claude Code for orch doctor"),
        ("src/Orchestrator.Tray/SelfTest.cs", [], "the screenshots of the tray self test"),
        ("src/Orchestrator.Tray/WidgetForm.cs", [], "the place of the tray widget on the screen"),
        ("src/Orchestrator.Tray/Program.cs", ["\"templates\""], "the check of the home folder"),
        ("src/Orchestrator.Tray/MockStore.cs", ["CreateTempSubdirectory", "Directory.CreateDirectory(cwd)", "Directory.Delete(Folder"], "the mock store makes and deletes its own temp folder and the cwd of its sessions"),
        ("src/Orchestrator.Store/Home.cs", ["Solution", "File.ReadAllText(source)"], "the home folder and config/policy.default.json of the repository"),
        ("src/Orchestrator.Store/Home.cs", ["StoreFiles.Templates"], "known exception: the root of the F1 template store in the store. It moves to IRepository in F2"),
        ("src/Orchestrator.Store/Lifecycle.cs", ["worktree", "Directory.Exists(path)", "Path.GetDirectoryName(path)"], "the git worktree of a session"),
        ("src/Orchestrator.Store/FileStore.cs", ["(Local.LocalRepositories)Repositories).PathOf"], "the path helper for tests, which delegates to the implementation"),
        ("src/Orchestrator.Cli/Commands/EventCommands.cs", ["File.ReadAllText(json)"], "a JSON file of the user"),
        ("src/Orchestrator.Cli/Commands/SessionCommands.cs", ["File.ReadAllText(file)"], "the goal file of the user"),
        ("src/Orchestrator.Cli/Commands/ToolCommands.cs", ["File.ReadAllText(check)", "File.ReadAllText(render)", "Home.Solution", "example.json"], "the files of the user and the templates"),
        ("src/Orchestrator.Cli/Git/StackOps.cs", ["bodyFile"], "the temp file of a PR body for gh"),
    ];

    static string Repo() => Orchestrator.Store.Home.Root();

    static IEnumerable<(string File, int Line, string Text)> IoLines()
    {
        var src = Path.Combine(Repo(), "src");
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var file = Path.GetRelativePath(Repo(), path).Replace('\\', '/');
            if (file.Contains("/obj/") || file.Contains("/bin/") || file.StartsWith(Implementations)) continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                if (!lines[i].TrimStart().StartsWith("//") && Io.IsMatch(lines[i])) yield return (file, i + 1, lines[i].Trim());
        }
    }

    [Test]
    public void Only_the_store_implementations_do_store_IO()
    {
        var refused = IoLines()
            .Where(l => !Allowed.Any(a => a.File == l.File && (a.Lines.Length == 0 || a.Lines.Any(l.Text.Contains))))
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();
        Assert.That(refused, Is.Empty, "Use the repositories of IRepositories (FileStore.Repositories), or add the line to Allowed with its reason if it is not store IO.");
    }

    [Test]
    public void Each_allowed_entry_is_still_in_use()
    {
        var lines = IoLines().ToList();
        var unused = Allowed.Where(a => !lines.Any(l => l.File == a.File && (a.Lines.Length == 0 || a.Lines.Any(l.Text.Contains)))).Select(a => a.File).ToList();
        Assert.That(unused, Is.Empty, "Remove the entries that match no line.");
    }

    [Test]
    public void The_scan_finds_store_IO()
    {
        Assert.That(Io.IsMatch("Files.WriteJson(store.PathOf(id, StoreFiles.Lease), lease);"), Is.True);
        Assert.That(Io.IsMatch("File.Exists(Path.Combine(store.Root, StoreFiles.Policy))"), Is.True);
        Assert.That(Io.IsMatch("var to = r.File.State;"), Is.False);
    }
}
