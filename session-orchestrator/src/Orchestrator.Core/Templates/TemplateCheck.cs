using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;
using Orchestrator.Core.Contract;

namespace Orchestrator.Core.Templates;

/// <summary>One look of the catalog: its name, the places where a view can use it (fact, block), and the values it shows.</summary>
public sealed record LookSpec(string Name, IReadOnlyList<string> Places, string Values, string Description);

public sealed record LookCatalog(string Description, IReadOnlyList<LookSpec> Looks)
{
    public const string Fact = "fact";
    public const string Block = "block";

    public LookSpec? Find(string name) => Looks.FirstOrDefault(l => l.Name == name);
}

/// <summary>The agreed format of a template: the spec files in templates/_spec. They come with the tool, not with the store.</summary>
public sealed record TemplateSpec(JsonSchema Meta, JsonSchema View, LookCatalog Looks)
{
    public const string Folder = "_spec";

    /// <summary>Reads the spec files of a core template root.</summary>
    public static TemplateSpec Load(string coreRoot)
    {
        var folder = Path.Combine(coreRoot, Folder);
        return new TemplateSpec(
            JsonSchema.FromText(File.ReadAllText(Path.Combine(folder, "meta.schema.json"))),
            JsonSchema.FromText(File.ReadAllText(Path.Combine(folder, "view.schema.json"))),
            StoreJson.Read<LookCatalog>(File.ReadAllText(Path.Combine(folder, "looks.json"))) ?? throw new TemplateException($"{folder}/looks.json does not parse"));
    }
}

/// <summary>The checks of one template against the spec. Each error is one line that names the file.</summary>
public static partial class TemplateCheck
{
    public static IReadOnlyList<string> Check(TemplateFiles files, TemplateSpec spec)
    {
        var errors = new List<string>();
        var kind = files.Kind;
        if (!LocalTemplateStore.IsName(kind)) errors.Add($"{kind}: the name must match ^[a-z][a-z0-9-]{{1,31}}$");

        JsonSchema? schema = null;
        HashSet<string> props = [];
        try
        {
            schema = JsonSchema.FromText(files.Schema);
            var root = JsonDocument.Parse(files.Schema).RootElement;
            if (root.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object) props = p.EnumerateObject().Select(x => x.Name).ToHashSet();
            else errors.Add("schema.json: no \"properties\"");
        }
        catch (Exception e)
        {
            errors.Add($"schema.json: {e.Message}");
        }

        if (files.Meta is null) errors.Add("meta.json: missing");
        else if (Evaluate(spec.Meta, files.Meta, "meta.json", errors) is { } meta && meta.TryGetProperty("type", out var type) && type.GetString() != kind)
            errors.Add($"meta.json: type is \"{type.GetString()}\", but the folder is \"{kind}\"");

        if (files.View is null) errors.Add("view.json: missing");
        else if (Evaluate(spec.View, files.View, "view.json", errors) is not null)
        {
            var view = StoreJson.Read<ViewSpec>(files.View);
            if (view is not null) CheckView(view, props, files, spec.Looks, errors);
        }

        if (files.Example is null) errors.Add("example.json: missing");
        else if (schema is not null)
        {
            try
            {
                var result = schema.Evaluate(JsonDocument.Parse(files.Example).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
                if (!result.IsValid) errors.Add("example.json: does not match schema.json");
            }
            catch (JsonException e)
            {
                errors.Add($"example.json: {e.Message}");
            }
        }
        return errors;
    }

    static JsonElement? Evaluate(JsonSchema schema, string text, string file, List<string> errors)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException e)
        {
            errors.Add($"{file}: {e.Message}");
            return null;
        }
        var result = schema.Evaluate(root, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return root;
        var details = (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{file}{(d.InstanceLocation.ToString() is { Length: > 0 } at ? at : "")}: {e.Value}")).Distinct().ToList();
        errors.AddRange(details.Count > 0 ? details : [$"{file}: does not match the spec"]);
        return null;
    }

    static void CheckView(ViewSpec view, HashSet<string> props, TemplateFiles files, LookCatalog looks, List<string> errors)
    {
        void Field(string? name, string where)
        {
            if (name is not null && !props.Contains(name)) errors.Add($"view.json {where}: \"{name}\" is not a property of schema.json");
        }

        void Look(FieldSpec spec, string place, string where)
        {
            Field(spec.Field, where);
            Field(spec.Mark, where);
            Field(spec.Render, where);
            if (looks.Find(spec.As) is not { } look) errors.Add($"view.json {where}: look \"{spec.As}\" is not in looks.json");
            else if (!look.Places.Contains(place)) errors.Add($"view.json {where}: look \"{spec.As}\" is not allowed as a {place}");
        }

        void Texts(IReadOnlyList<string>? texts, string where)
        {
            foreach (var text in texts ?? [])
            {
                foreach (Match m in Names().Matches(text)) Field(m.Groups[1].Value, where);
            }
        }

        Field(view.ToneBy?.Field, "toneBy");
        Texts(view.Document.Title, "document.title");
        Texts(view.Document.Subtitle, "document.subtitle");
        Texts(view.Preview.Title, "preview.title");
        Texts(view.Preview.Line, "preview.line");
        for (var i = 0; i < view.Document.Facts.Count; i++) Look(view.Document.Facts[i], LookCatalog.Fact, $"document.facts[{i}]");
        for (var i = 0; i < view.Document.Blocks.Count; i++) Look(view.Document.Blocks[i], LookCatalog.Block, $"document.blocks[{i}]");
        if (view.Preview.Pill is { } pill) Look(pill, LookCatalog.Fact, "preview.pill");
        foreach (var spec in (view.Preview.Well ?? []).Append(view.Preview.Chips).Append(view.Preview.List).OfType<FieldSpec>())
        {
            Field(spec.Field, "preview");
            Field(spec.Mark, "preview");
        }
        Field(view.Preview.Progress, "preview.progress");
        foreach (var spec in view.Document.Blocks.Where(b => b.Render is not null))
        {
            if (files.Renders.Count == 0) errors.Add($"view.json: block \"{spec.Render}\" names a render, but the template has no render files");
        }
    }

    /// <summary>Each field name in a title template: {{field}}, {{#field}}, {{/field}} and {{render:field}}.</summary>
    [GeneratedRegex(@"\{\{[#/]?(?:render:)?(\w+)\}\}")]
    private static partial Regex Names();
}
