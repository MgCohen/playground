namespace Orchestrator.Store;

public static class AppRecords
{
    public static string? Root { get; set; } =
        Environment.GetEnvironmentVariable("ORCH_APP_DATA") is { Length: > 0 } custom ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");

    public static bool Exists(string appId) => Find(appId) is not null;

    public static bool IsArchived(string appId) =>
        Find(appId) is { } path && System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), "\"isArchived\"\\s*:\\s*true");

    public static string? Find(string appId)
    {
        if (Root is not { } root || !Directory.Exists(root)) return null;
        foreach (var account in Directory.EnumerateDirectories(root))
        {
            var sessions = Path.Combine(account, "claude-code-sessions");
            if (!Directory.Exists(sessions)) continue;
            foreach (var file in Directory.EnumerateFiles(sessions, $"{appId}.json", SearchOption.AllDirectories)) return file;
        }
        return null;
    }
}
