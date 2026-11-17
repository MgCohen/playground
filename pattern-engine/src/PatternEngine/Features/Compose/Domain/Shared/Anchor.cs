using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Yaml;

namespace PatternEngine.Features.Compose.Domain.Shared;

/// <summary>
/// One binary check of an anchor. Check grader: the grader agent answers Ask. Check code: code answers it from the typed Args,
/// when the pack turns code checks on. Visibility run: the run sees it; eval: only the eval, never the run.
/// </summary>
public sealed record Rubric(string Id, string Ask, string Check, IReadOnlyDictionary<string, object?> Args, string Visibility)
{
    public const string Grader = "grader";

    public const string Code = "code";

    public bool Hidden => Visibility == "eval";

    public bool ByCode => Check == Code;
}

/// <summary>The score rule of an anchor: all rubrics true, or a fraction of them (PassAt, 0 to 1).</summary>
public sealed record ScoreRule(string Rule, double PassAt)
{
    public static readonly string[] Rules = ["all", "percent"];

    public bool Passes(int ok, int total) => Rule == "all" ? ok == total : total == 0 || (double)ok / total >= PassAt;

    public override string ToString() => Rule == "all" ? "all" : $"percent {PassAt:0.##}";
}

/// <summary>
/// A spec as an anchor file: front matter (id, kind, level, score, domain), a short context, then one rubric for each requirement.
/// The block "domain" belongs to the pack. All other field names are the same for each pack.
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

    public static readonly string[] Levels = ["engine", "pack", "case"];

    public static readonly string[] Visibilities = ["run", "eval"];

    public static readonly string[] Checks = [Rubric.Grader, Rubric.Code];

    public string? Field(string key) => Fields.Get(key);

    public IReadOnlyList<string> List(string key) => Node.Strings(Fields.Raw(key));

    /// <summary>The fields that only the domain uses, in the block "domain". A rename of a generic field does not touch them.</summary>
    public IReadOnlyDictionary<string, object?> Domain => Node.Map(Fields.Raw("domain"));

    /// <summary>The score rule from the front matter. With no score, all rubrics must be true.</summary>
    public ScoreRule Score
    {
        get
        {
            var s = Node.Map(Fields.Raw("score"));
            var passAt = double.TryParse(s.Get("pass_at"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            return new ScoreRule(s.Get("rule") ?? "all", passAt);
        }
    }

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

        var level = map.Get("level") ?? string.Empty;
        if (!Levels.Contains(level))
        {
            errors.Add($"level must be one of {string.Join(", ", Levels)}");
        }

        var score = Node.Map(map.Raw("score"));
        if (score.Get("rule") is { } rule && !ScoreRule.Rules.Contains(rule))
        {
            errors.Add($"score.rule must be one of {string.Join(", ", ScoreRule.Rules)}");
        }

        var hasPassAt = double.TryParse(score.Get("pass_at"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var passAt);
        if (score.Get("rule") == "percent" && (!hasPassAt || passAt is < 0 or > 1))
        {
            errors.Add("score.pass_at must be a number from 0 to 1 when the rule is percent");
        }

        foreach (var r in rubrics)
        {
            if (r.Id.Length == 0 || r.Ask.Length == 0 || r.Check.Length == 0)
            {
                errors.Add($"rubric \"{r.Id}\": id, ask and check are required");
            }

            if (r.Check.Length > 0 && !Checks.Contains(r.Check))
            {
                errors.Add($"rubric \"{r.Id}\": check must be one of {string.Join(", ", Checks)}");
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

        return new Anchor(id, kind, level, map, context, rubrics, file, errors);
    }

    public static Anchor Load(string file) => Parse(System.IO.File.ReadAllText(file), file);

    private static Rubric ReadRubric(IReadOnlyDictionary<string, object?> m) =>
        new(m.Get("id") ?? string.Empty, m.Get("ask") ?? string.Empty, m.Get("check") ?? string.Empty, Node.Map(m.Raw("args")), m.Get("visibility") ?? "run");
}
