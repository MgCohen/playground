using System.Text.RegularExpressions;

namespace Orchestrator.Tests;

/// <summary>
/// The provider boundary (AGENTS.md): only <c>src/Orchestrator.Providers/ClaudeCode/</c> knows the names of Claude Code.
/// The scan covers the source of each project and the core templates. <c>plugin/</c> is the Claude Code side of the
/// provider and is out of the scan.
/// </summary>
public sealed class ProviderBoundaryTests
{
    const string Provider = "src/Orchestrator.Providers/ClaudeCode/";

    /// <summary>
    /// The names of Claude Code: the executable and the product name, its environment variables, its app data folder,
    /// its app tools, the app id prefix, and the flags of the claude CLI.
    /// </summary>
    static readonly Regex Names = new(
        @"claude|mcp__ccd_|\blocal_\{|(?<![\w-])(-p|--session-id|--resume|--output-format|--desktop|--remote-control|--plugin-dir|--cloud|--setting-sources|--permission-mode|--no-session-persistence|--max-turns|--allowedTools|--disallowedTools)(?![\w-])",
        RegexOptions.IgnoreCase);

    static readonly string[] Extensions = [".cs", ".razor", ".csproj", ".js", ".json", ".md", ".css", ".html"];

    static string Repo() => Orchestrator.Store.Home.Root();

    static IEnumerable<(string File, int Line, string Text)> Hits()
    {
        foreach (var folder in new[] { "src", "templates" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(Repo(), folder), "*", SearchOption.AllDirectories))
            {
                var file = Path.GetRelativePath(Repo(), path).Replace('\\', '/');
                if (file.Contains("/obj/") || file.Contains("/bin/") || file.StartsWith(Provider) || !Extensions.Contains(Path.GetExtension(file))) continue;
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                    if (Names.IsMatch(lines[i])) yield return (file, i + 1, lines[i].Trim());
            }
    }

    [Test]
    public void Only_the_provider_folder_names_Claude_Code()
    {
        var refused = Hits().Select(h => $"{h.File}:{h.Line}: {h.Text}").ToList();
        Assert.That(refused, Is.Empty, $"Move the code behind IProvider into {Provider}, or use a neutral word in a comment.");
    }

    [Test]
    public void The_scan_finds_the_names_of_Claude_Code()
    {
        string[] found =
        [
            "Start-Process -FilePath 'claude.exe'",
            "Environment.GetEnvironmentVariable(\"CLAUDE_CODE_SESSION_ID\")",
            "Path.Combine(root, \"Claude-Data\")",
            "Call mcp__ccd_session_mgmt__archive_session",
            "var appId = $\"local_{id}\";",
            "[\"-p\", \"--output-format\", \"stream-json\"]",
            "arguments.AddRange([\"--resume\", id]);",
            "\"--cloud\"",
        ];
        string[] clean = ["git rebase --abort", "the git worktree of a session", "\"--porcelain\"", "--resume-later", "mcp__orch__report"];
        Assert.That(found.Where(t => !Names.IsMatch(t)), Is.Empty);
        Assert.That(clean.Where(t => Names.IsMatch(t)), Is.Empty);
    }

    [Test]
    public void The_provider_folder_exists_and_names_Claude_Code()
    {
        var folder = Path.Combine(Repo(), Provider);
        Assert.That(Directory.EnumerateFiles(folder, "*.cs").Any(f => Names.IsMatch(File.ReadAllText(f))), Is.True, "the scan path is wrong");
    }
}
