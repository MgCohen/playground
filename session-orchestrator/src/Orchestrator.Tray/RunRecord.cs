using System.Text.Json;

namespace Orchestrator.Tray;

/// <summary>
/// The record of one test run: one new file for each run in %LOCALAPPDATA%\Orchestrator.Tray\runs. The file name has the start
/// time and the process id, so two runs or two sessions never write the same file. tools/runs.sh reads the files.
/// </summary>
public static class RunRecord
{
    /// <summary>The folder of the run records.</summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator.Tray", "runs");

    /// <summary>The id of this run: the start time and the process id.</summary>
    public static string Id { get; } = $"{DateTime.Now:yyyyMMdd-HHmmss.fff}-pid{Environment.ProcessId}";

    public static DateTime Start { get; } = DateTime.Now;

    /// <summary>Writes the record of this run. A failure to write does not change the result of the run.</summary>
    public static void Write(string kind, string name, string code, int? slot, string build, int result)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, Id + ".json");
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new
            {
                run = Id, source = "tray", kind, name, code, slot, build,
                start = Start.ToString("yyyy-MM-ddTHH:mm:ss.fff"), seconds = Math.Round((DateTime.Now - Start).TotalSeconds, 1), result,
            }));
            File.Move(file + ".tmp", file, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"orch tray: warning: no run record: {e.Message}");
        }
    }
}
