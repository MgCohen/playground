using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatternEngine.Features.Compose.Domain;

/// <summary>
/// The stamp of the last grader fixture run of one pack (G2, B8): the model and the hash of each grader file.
/// When the model or a grader file changes, the stamp is old, and the fixtures must run again before the grades are trusted.
/// </summary>
public sealed record GraderStamp(
    [property: JsonPropertyName("pack")] string Pack,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, string> Files,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("at")] string At)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The stamp file of a pack, in the results folder.</summary>
    public static string FileOf(string resultsDir, string pack) => Path.Combine(resultsDir, "grader-check", $"{pack}.json");

    /// <summary>The hash of each grader file. The key is the path relative to the root, with forward slashes.</summary>
    public static IReadOnlyDictionary<string, string> Hashes(string root, IEnumerable<string> files) =>
        files.Order(StringComparer.Ordinal).ToDictionary(
            f => Path.GetRelativePath(root, f).Replace('\\', '/'),
            f => File.Exists(f) ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(f).Replace("\r\n", "\n", StringComparison.Ordinal))))[..12] : "missing",
            StringComparer.Ordinal);

    public void Write(string resultsDir)
    {
        var file = FileOf(resultsDir, Pack);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Null if the stamp is current. Else the reason why the fixtures must run again.</summary>
    public static string? Old(string resultsDir, string pack, string model, IReadOnlyDictionary<string, string> files)
    {
        var file = FileOf(resultsDir, pack);
        if (!File.Exists(file))
        {
            return $"no grader fixture run for {pack}; run: pe grader-check {pack} --model {model}";
        }

        var stamp = JsonSerializer.Deserialize<GraderStamp>(File.ReadAllText(file))!;
        if (stamp.Model != model)
        {
            return $"the grader fixtures of {pack} ran with {stamp.Model}, not {model}; run: pe grader-check {pack} --model {model}";
        }

        var changed = files.Where(f => stamp.Files.GetValueOrDefault(f.Key) != f.Value).Select(f => f.Key).ToList();
        return changed.Count == 0 ? null : $"grader files changed after the last fixture run of {pack} ({string.Join(", ", changed)}); run: pe grader-check {pack} --model {model}";
    }
}
