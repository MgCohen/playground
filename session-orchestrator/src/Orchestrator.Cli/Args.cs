namespace Orchestrator.Cli;

public sealed class UsageException(string message) : Exception(message);

public sealed class Args
{
    readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

    public Args(IEnumerable<string> raw)
    {
        var list = raw.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (!item.StartsWith("--") || item == "--")
            {
                Positional.Add(item);
                continue;
            }
            var name = item[2..];
            var equals = name.IndexOf('=');
            if (equals >= 0) options[name[..equals]] = name[(equals + 1)..];
            else if (i + 1 < list.Count && !list[i + 1].StartsWith("--")) options[name] = list[++i];
            else options[name] = null;
        }
    }

    public List<string> Positional { get; } = [];

    public IReadOnlyDictionary<string, string?> Options => options;

    public string? Option(string name) => options.TryGetValue(name, out var value) ? value : null;

    public bool Flag(string name) => options.ContainsKey(name);

    public string Required(int index, string name) =>
        index < Positional.Count ? Positional[index] : throw new UsageException($"missing <{name}>");

    public string RequiredOption(string name) => Option(name) ?? throw new UsageException($"missing --{name}");

    public string Rest(int from) => string.Join(' ', Positional.Skip(from));

    public IReadOnlyList<string>? List(string name) =>
        Option(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
