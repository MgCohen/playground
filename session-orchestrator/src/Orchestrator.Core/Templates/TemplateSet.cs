using System.Text.Json;
using Json.Schema;
using Orchestrator.Core.Contract;

namespace Orchestrator.Core.Templates;

public static class Kinds
{
    public const string Brief = "brief";
    public const string Message = "message";
    public const string Question = "question";
    public const string Plan = "plan";
    public const string Answer = "answer";
    public const string Review = "review";
    public const string Report = "report";
    public const string Update = "update";
    public const string Notice = "notice";
    public const string Ack = "ack";
    public static readonly string[] All = [Brief, Message, Question, Plan, Answer, Review, Report, Update, Notice, Ack];
}

/// <summary>The meta.json of a type: its purpose, who may send it to whom, and the types of its replies.</summary>
public sealed record TemplateMeta(string Type, int Version, string Purpose, IReadOnlyList<string> From, IReadOnlyList<string> To, string? Needs, IReadOnlyList<string> Replies)
{
    public JsonElement? Card { get; init; }

    /// <summary>meta.json "turn": a receiver session gets the message as a turn. Default: true when "to" allows a session.</summary>
    public bool? Turn { get; init; }

    public bool IsTurn => Turn ?? (To.Contains("*") || To.Contains("session"));

    public bool AllowsFrom(string partyClass) => From.Contains("*") || From.Contains(partyClass);

    public bool AllowsTo(string partyClass) => To.Contains("*") || To.Contains(partyClass);
}

public sealed record Template(string Kind, string Folder, JsonSchema Schema, string SchemaText, TemplateMeta? Meta)
{
    /// <summary>The view.json of the type: the full document and the preview in the UI. See <see cref="Views"/>.</summary>
    public ViewSpec? View { get; init; }

    /// <summary>core or store. See <see cref="TemplateSources"/>.</summary>
    public string Source { get; init; } = TemplateSources.Core;

    /// <summary>True when a store template replaces a core template with the same name.</summary>
    public bool ReplacesCore { get; init; }

    /// <summary>The text of example.json, or null.</summary>
    public string? Example { get; init; }

    /// <summary>The render texts. The key "" is render.md; each other key is render/&lt;key&gt;.md.</summary>
    public IReadOnlyDictionary<string, string> Renders { get; init; } = new Dictionary<string, string>();

    public bool HasRender(string? name = null) => Renders.ContainsKey(name ?? "");

    public IReadOnlyList<string> RenderNames() => Renders.Keys.Where(k => k.Length > 0).Order(StringComparer.Ordinal).ToList();

    /// <summary>Parses the files of the agreed format into a template.</summary>
    public static Template From(TemplateFiles files, TemplateInfo info) =>
        new(files.Kind, info.Location, JsonSchema.FromText(files.Schema), files.Schema, files.Meta is null ? null : StoreJson.Read<TemplateMeta>(files.Meta))
        {
            View = files.View is null ? null : StoreJson.Read<ViewSpec>(files.View),
            Source = info.Source,
            ReplacesCore = info.ReplacesCore,
            Example = files.Example,
            Renders = files.Renders,
        };
}

public sealed record Validation(bool IsValid, IReadOnlyList<string> Errors)
{
    public string Message => string.Join("; ", Errors);
}

/// <summary>The templates that apply, loaded once from an <see cref="ITemplateStore"/>. A change in the store applies at the next load.</summary>
public sealed class TemplateSet
{
    readonly Dictionary<string, Template> templates;

    public TemplateSet(ITemplateStore store, string root)
    {
        Root = root;
        Store = store;
        templates = [];
        foreach (var info in store.List())
        {
            try
            {
                templates[info.Kind] = Template.From(store.Get(info.Kind) ?? throw new TemplateException("it is in the list, but it does not load"), info);
            }
            catch (Exception e)
            {
                errors.Add($"{info.Kind} ({info.Location}): {e.Message}");
            }
        }
    }

    readonly List<string> errors = [];

    /// <summary>The templates that did not load. The others still apply; orch template and orch doctor show these errors.</summary>
    public IReadOnlyList<string> LoadErrors => errors;

    public ITemplateStore Store { get; }

    public string Root { get; }

    public IEnumerable<Template> All => templates.Values;

    public bool Has(string kind) => templates.ContainsKey(kind);

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
        Validate(kind, JsonSerializer.SerializeToElement(fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value), StoreJson.Options));

    public string Render(string kind, IReadOnlyDictionary<string, object?> data, string? name = null)
    {
        var template = this[kind];
        if (!template.Renders.TryGetValue(name ?? "", out var text)) throw new KeyNotFoundException($"template {kind} has no render {(name is null ? "render.md" : $"render/{name}.md")}");
        return Templates.Render.Text(text, data);
    }
}
