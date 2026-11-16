using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Compose.Domain.Shared;

/// <summary>One binary check of an anchor. The grader answers Ask; code reads the typed Args. Visibility run: the run sees it; eval: only the eval.</summary>
public sealed record Rubric(string Id, string Ask, string Check, IReadOnlyDictionary<string, object?> Args, string Visibility)
{
    public bool Hidden => Visibility == "eval";
}

/// <summary>
/// A spec as an anchor file: front matter, a short context, then one rubric for each requirement.
/// The field names follow the draft of the solution-grade plan. They are provisional.
/// </summary>
public sealed record Anchor(
    string Id,
    string Kind,
    string Level,
    IReadOnlyDictionary<string, object?> Fields,
    string Context,
    IReadOnlyList<Rubric> Rubrics,
    string File,
    IReadOnlyList<string> Errors)
{
    public static readonly string[] Kinds = ["intent", "constraints", "success", "review"];

    public static readonly string[] Visibilities = ["run", "eval"];

    public string? Field(string key) => Fields.Get(key);

    public IReadOnlyList<string> List(string key) => Node.Strings(Fields.Raw(key));

    /// <summary>The rubrics that the run can see.</summary>
    public IReadOnlyList<Rubric> Visible => Rubrics.Where(r => !r.Hidden).ToList();

    public static Anchor Parse(string text, string file)
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
            errors.Add($"bad YAML in the front matter: {e.Message}");
            map = new Dictionary<string, object?>();
        }

        var body = split.Value.Body;
        var start = body.StartsWith("- ", StringComparison.Ordinal) ? 0 : body.IndexOf("\n- ", StringComparison.Ordinal);
        var context = (start < 0 ? body : body[..start]).Trim();
        var rubrics = new List<Rubric>();
        if (start >= 0)
        {
            try
            {
                rubrics = Node.List(Yaml.Parse(body[start..])).Select(Node.Map).Select(ReadRubric).ToList();
            }
            catch (YamlDotNet.Core.YamlException e)
            {
                errors.Add($"bad YAML in the rubrics: {e.Message}");
            }
        }

        var id = map.Get("id") ?? string.Empty;
        var kind = map.Get("kind") ?? string.Empty;
        if (id.Length == 0)
        {
            errors.Add("no id");
        }

        if (!Kinds.Contains(kind))
        {
            errors.Add($"kind must be one of {string.Join(", ", Kinds)}");
        }

        foreach (var r in rubrics)
        {
            if (r.Id.Length == 0 || r.Ask.Length == 0 || r.Check.Length == 0)
            {
                errors.Add($"rubric \"{r.Id}\": id, ask and check are required");
            }

            if (!Visibilities.Contains(r.Visibility))
            {
                errors.Add($"rubric \"{r.Id}\": visibility must be one of {string.Join(", ", Visibilities)}");
            }
        }

        foreach (var dup in rubrics.GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"rubric \"{dup.Key}\" is in the anchor more than once");
        }

        return new Anchor(id, kind, map.Get("level") ?? string.Empty, map, context, rubrics, file, errors);
    }

    public static Anchor Load(string file) => Parse(System.IO.File.ReadAllText(file), file);

    private static Rubric ReadRubric(IReadOnlyDictionary<string, object?> m) =>
        new(m.Get("id") ?? string.Empty, m.Get("ask") ?? string.Empty, m.Get("check") ?? string.Empty, Node.Map(m.Raw("args")), m.Get("visibility") ?? "run");
}
