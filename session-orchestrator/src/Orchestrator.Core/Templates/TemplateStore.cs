using Orchestrator.Core.Storage;

namespace Orchestrator.Core.Templates;

/// <summary>The source of a definition: the core set of the tool, or the store of the user.</summary>
public static class TemplateSources
{
    public const string Core = "core";
    public const string Store = "store";
}

/// <summary>One row of a definition list. <c>Location</c> is a folder, a URL, or another address of the implementation.</summary>
public sealed record TemplateInfo(string Kind, string Source, bool ReplacesCore, string Location);

/// <summary>
/// The agreed format of one message template: the text of each file. Renders: the key "" is render.md, each other
/// key is render/&lt;key&gt;.md. <see cref="From"/> and <see cref="ToDefinition"/> map it to the stored <see cref="Definition"/>.
/// </summary>
public sealed record TemplateFiles(string Kind, string Schema, string? Meta, string? View, string? Example, IReadOnlyDictionary<string, string> Renders)
{
    public const string SchemaFile = "schema.json";
    public const string MetaFile = "meta.json";
    public const string ViewFile = "view.json";
    public const string ExampleFile = "example.json";
    public const string RenderFile = "render.md";
    public const string RenderFolder = "render";

    /// <summary>The template of a stored definition. The name of the definition is the kind.</summary>
    public static TemplateFiles From(string kind, Definition definition)
    {
        var renders = new Dictionary<string, string>();
        if (definition[RenderFile] is { } main) renders[""] = main;
        foreach (var (path, text) in definition.Files)
            if (path.StartsWith(RenderFolder + "/") && path.EndsWith(".md") && path.Count(c => c == '/') == 1)
                renders[path[(RenderFolder.Length + 1)..^3]] = text;
        return new TemplateFiles(kind, definition[SchemaFile] ?? throw new TemplateException($"{kind} has no {SchemaFile}"),
            definition[MetaFile], definition[ViewFile], definition[ExampleFile], renders);
    }

    public Definition ToDefinition()
    {
        var files = new Dictionary<string, string> { [SchemaFile] = Schema };
        if (Meta is not null) files[MetaFile] = Meta;
        if (View is not null) files[ViewFile] = View;
        if (Example is not null) files[ExampleFile] = Example;
        foreach (var (name, text) in Renders) files[name.Length == 0 ? RenderFile : $"{RenderFolder}/{name}.md"] = text;
        return new Definition(files);
    }
}

public sealed class TemplateException(string message) : Exception(message);

/// <summary>
/// One place of definitions: a repository and the address of each definition in it, for messages.
/// </summary>
public sealed record DefinitionSource(string Name, IRepository<Doc<Definition>, string> Repository, Func<string, string> LocationOf);

/// <summary>
/// The definitions of one catalog in two layers: the core set of the tool (read only) and the store of the user.
/// A store definition with a core name replaces the core definition. The local folder, a later Notion database or a
/// cloud bucket is only the repository: this rule stays the same.
/// </summary>
public sealed class LayeredDefinitions(Catalog catalog, DefinitionSource core, DefinitionSource? store)
{
    public Catalog Catalog { get; } = catalog;

    /// <summary>The definitions that apply: one row for each name. A store definition wins over a core definition.</summary>
    public IReadOnlyList<TemplateInfo> List()
    {
        var coreNames = Names(core);
        var storeNames = store is null ? [] : Names(store);
        return coreNames.Union(storeNames).Order(StringComparer.Ordinal).Select(name => storeNames.Contains(name)
            ? new TemplateInfo(name, TemplateSources.Store, coreNames.Contains(name), store!.LocationOf(name))
            : new TemplateInfo(name, TemplateSources.Core, false, core.LocationOf(name))).ToList();
    }

    /// <summary>The definition that applies, or null.</summary>
    public Definition? Get(string name)
    {
        if (!Catalog.IsName(name)) return null;
        return (store is null ? null : store.Repository.Read(name)) ?? core.Repository.Read(name);
    }

    public bool IsCore(string name) => Catalog.IsName(name) && core.Repository.Exists(name);

    /// <summary>Adds a definition to the store. A name that is in the store already needs <paramref name="replace"/>.</summary>
    public TemplateInfo Add(string name, Definition definition, bool replace)
    {
        if (store is null) throw new TemplateException($"this {Catalog.Name} catalog has no store");
        var repository = store.Repository;
        if (repository.Get(name) is { } existing)
        {
            if (!replace) throw new TemplateException($"{name} is in the store already. Use --replace.");
            existing.Value = definition;
        }
        else repository.Add(new Doc<Definition>(name, definition));
        repository.Save();
        return List().First(t => t.Kind == name);
    }

    /// <summary>Removes a store definition. A core definition cannot be removed; the removal of its replacement makes the core definition apply again.</summary>
    public void Remove(string name)
    {
        var doc = store is null || !Catalog.IsName(name) ? null : store.Repository.Get(name);
        if (doc is null)
            throw new TemplateException(IsCore(name) ? $"{name} is a core {Single()}. Only a store {Single()} can be removed." : $"no {Single()} {name} in the store");
        store!.Repository.Remove(doc);
        store.Repository.Save();
    }

    string Single() => Catalog == Catalog.Templates ? "template" : Catalog.Name[..^1];

    static HashSet<string> Names(DefinitionSource source) =>
        source.Repository.List(_ => true).Select(d => d.Id).Where(Catalog.IsName).ToHashSet();
}

/// <summary>
/// The library contract of the message templates. The CLI and the supervisor read and change templates only through it.
/// <see cref="TemplateStore"/> is the only implementation: the storage is the <c>IRepository</c> under it.
/// </summary>
public interface ITemplateStore
{
    /// <summary>The templates that apply: one row for each kind. A store template wins over a core template with the same name.</summary>
    IReadOnlyList<TemplateInfo> List();

    /// <summary>The files of the template that applies, or null.</summary>
    TemplateFiles? Get(string kind);

    /// <summary>Checks <paramref name="files"/> against the spec, then adds it to the store. A kind that is in the store already needs <paramref name="replace"/>.</summary>
    TemplateInfo Add(TemplateFiles files, bool replace = false);

    /// <summary>Removes a store template. A core template cannot be removed; the removal of its replacement makes the core template apply again.</summary>
    void Remove(string kind);
}

/// <summary>The message templates: a thin slice on the template catalog of <see cref="LayeredDefinitions"/>, with the spec check.</summary>
public sealed class TemplateStore(LayeredDefinitions definitions, Func<TemplateSpec> spec) : ITemplateStore
{
    public TemplateStore(DefinitionSource core, DefinitionSource? store, Func<TemplateSpec> spec)
        : this(new LayeredDefinitions(Catalog.Templates, core, store), spec)
    {
    }

    public IReadOnlyList<TemplateInfo> List() => definitions.List().Where(t => IsName(t.Kind)).ToList();

    public TemplateFiles? Get(string kind) => IsName(kind) && definitions.Get(kind) is { } d ? TemplateFiles.From(kind, d) : null;

    public TemplateInfo Add(TemplateFiles files, bool replace = false)
    {
        var errors = TemplateCheck.Check(files, spec());
        if (errors.Count > 0) throw new TemplateException($"{files.Kind} is not a valid template: {string.Join("; ", errors)}");
        return definitions.Add(files.Kind, files.ToDefinition(), replace);
    }

    public void Remove(string kind) => definitions.Remove(kind);

    /// <summary>A kind name: a definition name (<see cref="Catalog.IsName"/>) that is not a reserved word.</summary>
    public static bool IsName(string kind) => Catalog.IsName(kind) && !Reserved.Contains(kind);

    /// <summary>The words of the CLI command "orch template", and the folders of the other catalogs: not kind names.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "list", "check", "add", "remove", Catalog.Modes.Folder, Catalog.Workflows.Folder };
}
