using System.Security.Cryptography;
using System.Text;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Yaml;

using PatternEngine.Features.Catalog.Domain.Shared;

namespace PatternEngine.Features.Catalog.Domain;

/// <summary>Reads one pattern file: YAML front matter plus "## Section" blocks. A bad value becomes a parse error, not an exception.</summary>
public static class PatternParser
{
    private static readonly HashSet<string> Known =
    [
        "id", "kind", "level", "status", "phrase", "primitive", "use_when", "do_not_use_when", "variants", "parts", "params",
        "related", "asserts", "inputs", "outputs",
    ];

    public static Pattern Parse(string text, string file)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var errors = new List<string>();
        var split = FrontMatter.Split(text);
        if (split is null)
        {
            errors.Add("no front matter");
            split = (string.Empty, text);
        }

        IReadOnlyDictionary<string, object?> map;
        try
        {
            map = Node.Map(Yaml.Parse(split.Value.Head));
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            errors.Add($"bad YAML: {e.Message}");
            map = new Dictionary<string, object?>();
        }

        var kind = Enum.TryParse<PatternKind>(map.Get("kind"), true, out var k) ? k : (PatternKind)(-1);
        if (!Enum.IsDefined(kind))
        {
            errors.Add("kind must be one of leaf, object, group");
        }

        var status = Enum.TryParse<PatternStatus>(map.Get("status"), true, out var s) ? s : (PatternStatus)(-1);
        if (!Enum.IsDefined(status))
        {
            errors.Add("status must be one of proposed, trial, stable");
        }

        var level = int.TryParse(map.Get("level"), out var l) ? l : int.MinValue;
        if (level == int.MinValue)
        {
            errors.Add("level must be an integer");
        }

        return new Pattern(
            Id: map.Get("id") ?? string.Empty,
            Kind: kind,
            Level: level,
            Status: status,
            Phrase: map.Get("phrase"),
            Primitive: map.Get("primitive"),
            UseWhen: Node.Strings(map.Raw("use_when")),
            DoNotUseWhen: Node.List(map.Raw("do_not_use_when")).Select(Node.Map).Select(m => new InsteadRule(m.Get("when") ?? string.Empty, m.Get("use") ?? string.Empty)).ToList(),
            Variants: Node.List(map.Raw("variants")).Select(Node.Map).Select(m => new Variant(m.Get("id") ?? string.Empty, m.Get("when") ?? string.Empty, m.Get("phrase"))).ToList(),
            Parts: Node.List(map.Raw("parts")).Select(Node.Map).Select(m => new PartSpec(m.Get("ref") ?? string.Empty, m.Get("count") ?? "1", m.Get("only"))).ToList(),
            Params: Node.Map(map.Raw("params")).Select(e => ReadParam(e.Key, Node.Map(e.Value))).ToList(),
            Related: Node.Strings(map.Raw("related")),
            Asserts: Node.Strings(map.Raw("asserts")),
            Inputs: ReadPorts(map.Raw("inputs")),
            Outputs: ReadPorts(map.Raw("outputs")),
            Sections: ReadSections(split.Value.Body),
            Extra: map.Where(e => !Known.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
            File: file,
            Hash: Hash(text),
            ParseErrors: errors);
    }

    public static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    private static ParamDef ReadParam(string key, IReadOnlyDictionary<string, object?> m) =>
        new(key, m.Get("part"), m.Get("attr"), m.Get("default"), m.Get("phrase"), Node.Strings(m.Raw("values")), string.Equals(m.Get("required"), "true", StringComparison.OrdinalIgnoreCase));

    private static List<Port> ReadPorts(object? node) =>
        Node.List(node).Select(Node.Map).Select(m => new Port(m.Get("name") ?? string.Empty, m.Get("type") ?? string.Empty)).ToList();

    private static Dictionary<string, string> ReadSections(string body)
    {
        var sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        StringBuilder? current = null;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = new StringBuilder();
                sections[line[3..].Trim()] = current;
                continue;
            }

            current?.Append(line).Append('\n');
        }

        return sections.ToDictionary(e => e.Key, e => e.Value.ToString().Trim(), StringComparer.Ordinal);
    }
}
