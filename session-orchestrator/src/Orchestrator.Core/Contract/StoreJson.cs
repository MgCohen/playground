using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.Contract;

public static class StoreJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One JSON object on one line, for a line protocol (orch watch, SSE).</summary>
    public static readonly JsonSerializerOptions Line = new(Options) { WriteIndented = false };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
