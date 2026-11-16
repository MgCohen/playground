using YamlDotNet.Serialization;

namespace PatternEngine.Plugins.Yaml;

public static class Yaml
{
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static object? Parse(string text) => Normalize(Reader.Deserialize<object?>(text));

    public static string Write(object? value) => Writer.Serialize(value);

    private static object? Normalize(object? node) => node switch
    {
        IDictionary<object, object?> map => map.ToDictionary(e => e.Key.ToString()!, e => Normalize(e.Value), StringComparer.Ordinal),
        IList<object?> list => list.Select(Normalize).ToList(),
        _ => node,
    };
}

public static class Node
{
    public static IReadOnlyDictionary<string, object?> Map(object? node) =>
        node as IReadOnlyDictionary<string, object?> ?? (node as Dictionary<string, object?>) ?? new Dictionary<string, object?>();

    public static IReadOnlyList<object?> List(object? node) => node as List<object?> ?? [];

    public static string? Text(object? node) => node switch
    {
        null => null,
        string s => s,
        IDictionary<string, object?> or IList<object?> => null,
        _ => node.ToString(),
    };

    public static string? Get(this IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) ? Text(v) : null;

    public static object? Raw(this IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) ? v : null;

    public static IReadOnlyList<string> Strings(object? node) => List(node).Select(Text).OfType<string>().ToList();
}
