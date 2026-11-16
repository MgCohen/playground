using Orchestrator.Core.Contract;

namespace Orchestrator.Store;

public static class Files
{
    const int Attempts = 20;

    public static string? ReadText(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    public static T? ReadJson<T>(string path) => ReadText(path) is { } text ? StoreJson.Read<T>(text) : default;

    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid().ToString()[..8]}.tmp";
        File.WriteAllText(temp, text);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    public static void WriteJson<T>(string path, T value) => Write(path, StoreJson.Write(value));

    public static void Append(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.AppendAllText(path, line + "\n");
                return;
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    public static IReadOnlyList<string> Names(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Where(name => !name.EndsWith(".tmp")).Order(Core.Ordering.Names.Order).ToList()
            : [];
}
