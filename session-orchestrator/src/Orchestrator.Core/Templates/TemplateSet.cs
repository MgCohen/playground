using System.Text.Json;
using Json.Schema;

namespace Orchestrator.Core.Templates;

public static class Kinds
{
    public const string Decision = "decision";
    public const string Answer = "answer";
    public const string Review = "review";
    public const string Report = "report";
    public const string Update = "update";
    public const string Brief = "brief";
    public const string Notice = "notice";
    public static readonly string[] All = [Decision, Answer, Review, Report, Update, Brief, Notice];
}

public sealed record Template(string Kind, string Folder, JsonSchema Schema, string SchemaText)
{
    public string RenderPath(string? name = null) => name is null ? Path.Combine(Folder, "render.md") : Path.Combine(Folder, "render", $"{name}.md");

    public IReadOnlyList<string> RenderNames() =>
        Directory.Exists(Path.Combine(Folder, "render"))
            ? Directory.GetFiles(Path.Combine(Folder, "render"), "*.md").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];
}

public sealed record Validation(bool IsValid, IReadOnlyList<string> Errors)
{
    public string Message => string.Join("; ", Errors);
}

public sealed class TemplateSet
{
    readonly Dictionary<string, Template> templates;
    readonly Dictionary<string, string> renders = [];

    public TemplateSet(string root)
    {
        Root = root;
        templates = Kinds.All.Where(kind => File.Exists(Path.Combine(root, kind, "schema.json"))).ToDictionary(kind => kind, kind =>
        {
            var text = File.ReadAllText(Path.Combine(root, kind, "schema.json"));
            return new Template(kind, Path.Combine(root, kind), JsonSchema.FromText(text), text);
        });
    }

    public string Root { get; }

    public IEnumerable<Template> All => templates.Values;

    public Template this[string kind] => templates.TryGetValue(kind, out var t) ? t : throw new KeyNotFoundException($"no template {kind} in {Root}");

    public Validation Validate(string kind, JsonElement fields)
    {
        var result = this[kind].Schema.Evaluate(fields, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return new Validation(true, []);
        var errors = (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{(d.InstanceLocation.ToString() is { Length: > 0 } at ? at : "/")}: {e.Value}"))
            .Distinct().ToList();
        return new Validation(false, errors.Count > 0 ? errors : ["the fields do not match the schema"]);
    }

    public Validation Validate(string kind, IReadOnlyDictionary<string, object?> fields) =>
        Validate(kind, JsonSerializer.SerializeToElement(fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value)));

    public string Render(string kind, IReadOnlyDictionary<string, object?> data, string? name = null)
    {
        var path = this[kind].RenderPath(name);
        string text;
        lock (renders)
        {
            if (!renders.TryGetValue(path, out text!)) renders[path] = text = File.ReadAllText(path).ReplaceLineEndings("\n");
        }
        return Templates.Render.Text(text, data);
    }
}
