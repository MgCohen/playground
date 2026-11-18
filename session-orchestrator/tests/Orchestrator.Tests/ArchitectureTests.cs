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
        ("src/Orchestrator.Core/Templates/TemplateCheck.cs", [], "the spec files of the template system in the repository (meta, view, mode and workflow schemas, looks.json)"),
        ("src/Orchestrator.Store/SupervisorHost.cs", [], "the supervisor process records and the install record of the app, not the store"),
        ("src/Orchestrator.Providers/ClaudeCode/AppRecords.cs", [], "the records of the desktop app, not the store"),
        ("src/Orchestrator.Providers/Fake/FakeProvider.cs", [], "the behavior file of the fake provider, not the store"),
        ("src/Orchestrator.Providers/ClaudeCode/ClaudeCodeProvider.cs", ["File.ReadAllText(settings)", "plugin.json"], "the settings of Claude Code for orch doctor"),
        ("src/Orchestrator.Tray/SelfTest.cs", [], "the screenshots of the tray self test"),
        ("src/Orchestrator.Tray/SelfTest.Motion.cs", [], "the screenshots of the motion checks of the tray self test"),
        ("src/Orchestrator.Tray/SelfTest.Cards.cs", [], "the pictures of the cards of the tray self test (V7)"),
        ("src/Orchestrator.Tray/SelfTest.Overlap.cs", [], "the test place of the edge tab on the screen (V3)"),
        ("src/Orchestrator.Tray/WidgetForm.cs", [], "the place of the tray widget on the screen"),
        ("src/Orchestrator.Tray/EdgeTab.cs", [], "the place of the edge tab on the screen"),
        ("src/Orchestrator.Tray/Program.cs", ["\"templates\"", "selftest-error.txt"], "the check of the home folder, and the error file of the self test"),
        ("src/Orchestrator.Tray/MockStore.cs", ["CreateTempSubdirectory", "Directory.CreateDirectory(cwd)", "Directory.Delete(Folder"], "the mock store makes and deletes its own temp folder and the cwd of its sessions"),
        ("src/Orchestrator.Store/Home.cs", ["Solution", "File.ReadAllText(source)", "Directory.Exists(DefaultStoreRoot())", "File.Exists(Path.Combine(folder, \"orch"], "the home folder, config/policy.default.json of the repository, the choice of the orch store (H5), and the orch tool of the supervisor (C3-F5)"),
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

    /// <summary>
    /// S2: the session data goes only through the registry. Only the slice <c>SessionRegistry</c> writes the entries, and
    /// only the store upgrade reads the files of store version 2.
    /// </summary>
    [Test]
    public void Only_the_registry_touches_the_session_entries_and_only_the_upgrade_reads_meta_and_control()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["SessionEntries"] = ["src/Orchestrator.Core/Storage/IRepositories.cs", "src/Orchestrator.Store/Local/LocalRepositories.cs", "src/Orchestrator.Store/Remote/HttpRepositories.cs", "src/Orchestrator.Store/Remote/StoreService.cs", "src/Orchestrator.Store/Remote/StoreWire.cs", "src/Orchestrator.Store/FileStore.cs", "src/Orchestrator.Store/StoreUpgrade.cs"],
            ["StoreFiles.Meta"] = ["src/Orchestrator.Store/StoreUpgrade.cs"],
            ["StoreFiles.Control"] = ["src/Orchestrator.Store/StoreUpgrade.cs"],
        };
        var src = Path.Combine(Repo(), "src");
        var refused = new List<string>();
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var file = Path.GetRelativePath(Repo(), path).Replace('\\', '/');
            if (file.Contains("/obj/") || file.Contains("/bin/")) continue;
            var text = File.ReadAllText(path);
            foreach (var (name, files) in allowed)
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b") && !files.Contains(file)) refused.Add($"{file}: {name}");
        }
        Assert.That(refused, Is.Empty, "Read and write sessions through FileStore.Registry (SessionRegistry).");
    }

    [Test]
    public void The_scan_finds_store_IO()
    {
        Assert.That(Io.IsMatch("Files.WriteJson(store.PathOf(id, StoreFiles.Lease), lease);"), Is.True);
        Assert.That(Io.IsMatch("File.Exists(Path.Combine(store.Root, StoreFiles.Policy))"), Is.True);
        Assert.That(Io.IsMatch("var to = r.File.State;"), Is.False);
    }
}
