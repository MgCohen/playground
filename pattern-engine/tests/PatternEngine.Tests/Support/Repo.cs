using System.Text.Json;
using System.Text.Json.Nodes;

namespace PatternEngine.Tests.Support;

public static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    public static string Parity(params string[] parts) => Path(["tests", "PatternEngine.Tests", "Parity", .. parts]);

    public static JsonNode? Json(string file) => JsonNode.Parse(File.ReadAllText(file));

    public static JsonNode? ToJson<T>(T value) => JsonSerializer.SerializeToNode(value);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "PatternEngine.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("cannot find PatternEngine.slnx");
    }
}

/// <summary>A temporary folder that a test can fill with pattern files.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string name, string text)
    {
        var file = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}
