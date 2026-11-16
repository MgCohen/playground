using System.Text.Json;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Plugins.Agents;

public sealed record AgentFile(string Name, string Model, IReadOnlyList<string> Tools, JsonElement Schema, string Text, string Path)
{
    public const string StructuredOutputTool = "StructuredOutput";

    public static AgentFile Read(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = FrontMatter.Split(text) ?? throw new AgentFileException($"{path}: no front matter");
        var map = Node.Map(Yaml.Yaml.Parse(parts.Head));
        var name = map.Get("name") ?? throw new AgentFileException($"{path}: no name");
        var tools = (map.Get("tools") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!tools.Contains(StructuredOutputTool))
        {
            throw new AgentFileException($"{path}: tools must include {StructuredOutputTool}, or the schema is not enforced");
        }

        var schemaRef = map.Get("output_schema") ?? throw new AgentFileException($"{path}: no output_schema");
        var schemaPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, schemaRef);
        if (!File.Exists(schemaPath))
        {
            throw new AgentFileException($"{path}: the schema file {schemaRef} does not exist");
        }

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return new AgentFile(name, map.Get("model") ?? "haiku", tools, schema.RootElement.Clone(), text, path);
    }
}

public sealed class AgentFileException(string message) : Exception(message);

public static class FrontMatter
{
    public static (string Head, string Body)? Split(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        var after = text.IndexOf('\n', end + 4);
        var body = after < 0 ? string.Empty : text[(after + 1)..];
        return (text[4..end], body);
    }
}
