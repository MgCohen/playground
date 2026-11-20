using System.Text.Json;

namespace Orchestrator.Store;

/// <summary>
/// A session that the app started already joins the registry: its engine id, its app id and its folder. The registry
/// keeps no repository and no branch: a worktree of the app is the app's, and the app removes it at archive.
/// </summary>
public sealed record SessionJoin(string Id, string? AppId, string Cwd);

/// <summary>
/// The brief of a child that the app starts (R-F8). The app gives the session id, so the store entry cannot exist
/// before the start. The brief waits in a start file outside the store; the child runs <c>orch join &lt;file&gt;</c> as its
/// first action, and that command makes the entry with the id of the child.
/// </summary>
public sealed record AppStartFile(string Store, BriefFields Brief, string CreatedBy, string From)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The folder of the start files. <c>ORCH_START_DIR</c> moves it (tests).</summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable("ORCH_START_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Path.GetTempPath(), "orch-starts");

    /// <summary>Writes the start file and gives its path, with forward slashes, so that a shell command can use it as it is.</summary>
    public string Write()
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"start-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        return path.Replace('\\', '/');
    }

    public static AppStartFile Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"no start file {path}: it was used already, or the parent did not make it");
        return JsonSerializer.Deserialize<AppStartFile>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"the start file {path} is empty");
    }
}
