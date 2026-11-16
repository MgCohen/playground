using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Plugins.Settings;

public sealed class Settings(IReadOnlyDictionary<string, object?> root)
{
    public IReadOnlyDictionary<string, object?> Root { get; } = root;

    public static Settings Load(string file, string? overrideFile = null)
    {
        var baseMap = Read(file);
        var merged = overrideFile is null ? baseMap : Merge(baseMap, Read(overrideFile));
        return new Settings(Node.Map(merged));
    }

    public Settings Section(params string[] path)
    {
        object? node = Root;
        foreach (var key in path)
        {
            node = Node.Map(node).Raw(key);
            if (node is null)
            {
                throw new SettingsException($"The setting \"{string.Join('.', path)}\" is missing.");
            }
        }

        return new Settings(Node.Map(node));
    }

    public bool Has(string key) => Root.ContainsKey(key);

    public string Text(string key) => Root.Get(key) ?? throw new SettingsException($"The setting \"{key}\" is missing.");

    public int Int(string key) => int.Parse(Text(key), System.Globalization.CultureInfo.InvariantCulture);

    public double Number(string key) => double.Parse(Text(key), System.Globalization.CultureInfo.InvariantCulture);

    public bool Flag(string key) => bool.Parse(Text(key));

    public IReadOnlyList<string> List(string key) =>
        Root.TryGetValue(key, out var v) ? Node.Strings(v) : throw new SettingsException($"The setting \"{key}\" is missing.");

    public object? Raw(string key) => Root.Raw(key);

    public Settings With(Settings over) => new(Node.Map(Merge(Root, over.Root)));

    private static object? Read(string file) =>
        File.Exists(file) ? Yaml.Yaml.Parse(File.ReadAllText(file)) ?? new Dictionary<string, object?>() : new Dictionary<string, object?>();

    internal static object? Merge(object? a, object? b)
    {
        if (b is null)
        {
            return a;
        }

        if (a is IReadOnlyDictionary<string, object?> ma && b is IReadOnlyDictionary<string, object?> mb)
        {
            var output = ma.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            foreach (var (key, value) in mb)
            {
                output[key] = Merge(ma.Raw(key), value);
            }

            return output;
        }

        return b;
    }
}

public sealed class SettingsException(string message) : Exception(message);
