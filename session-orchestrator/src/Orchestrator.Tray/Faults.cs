namespace Orchestrator.Tray;

/// <summary>
/// V19: the unhandled exceptions of the tray. Each one goes to a log file (tray.log, or exceptions.log in the self-test
/// folder) with its time and thread; Windows Forms shows no dialog. The self-test fails when one came.
/// </summary>
public static class Faults
{
    static readonly object Gate = new();
    static int count;

    /// <summary>The log file, or null for no file.</summary>
    public static string? Log { get; set; }

    /// <summary>The number of exceptions since the start.</summary>
    public static int Count => Volatile.Read(ref count);

    /// <summary>The first exception, as text, or null.</summary>
    public static string? First { get; private set; }

    public static void Add(Exception? e, string where)
    {
        var text = $"{DateTimeOffset.Now:O} {where} (thread {Environment.CurrentManagedThreadId} {Thread.CurrentThread.Name}): {e}";
        lock (Gate)
        {
            count++;
            First ??= text;
            Console.Error.WriteLine($"orch tray: unhandled exception: {e?.GetType().Name}: {e?.Message}");
            if (Log is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
                File.AppendAllText(Log, text + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
