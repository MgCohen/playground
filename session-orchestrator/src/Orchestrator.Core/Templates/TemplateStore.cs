using System.Text.RegularExpressions;

namespace Orchestrator.Core.Templates;

/// <summary>The source of a template: the core set of the tool, or the store of the user.</summary>
public static class TemplateSources
{
    public const string Core = "core";
    public const string Store = "store";
}

/// <summary>One row of <see cref="ITemplateStore.List"/>. <c>Location</c> is a folder, a URL, or another address of the implementation.</summary>
public sealed record TemplateInfo(string Kind, string Source, bool ReplacesCore, string Location);

/// <summary>
/// The agreed format of one template: the text of each file. An implementation of <see cref="ITemplateStore"/>
/// reads and writes this shape. Renders: the key "" is render.md, each other key is render/&lt;key&gt;.md.
/// </summary>
public sealed record TemplateFiles(string Kind, string Schema, string? Meta, string? View, string? Example, IReadOnlyDictionary<string, string> Renders)
{
    public const string SchemaFile = "schema.json";
    public const string MetaFile = "meta.json";
    public const string ViewFile = "view.json";
    public const string ExampleFile = "example.json";
    public const string RenderFile = "render.md";
    public const string RenderFolder = "render";

    /// <summary>Reads the files of one template folder. The folder name is the kind.</summary>
    public static TemplateFiles ReadFolder(string folder)
    {
        var kind = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        var schema = Path.Combine(folder, SchemaFile);
        if (!File.Exists(schema)) throw new TemplateException($"{folder} has no {SchemaFile}");
        var renders = new Dictionary<string, string>();
        if (File.Exists(Path.Combine(folder, RenderFile))) renders[""] = Read(Path.Combine(folder, RenderFile))!;
        if (Directory.Exists(Path.Combine(folder, RenderFolder)))
            foreach (var file in Directory.GetFiles(Path.Combine(folder, RenderFolder), "*.md"))
                renders[Path.GetFileNameWithoutExtension(file)] = Read(file)!;
        return new TemplateFiles(kind, Read(schema)!, Read(Path.Combine(folder, MetaFile)), Read(Path.Combine(folder, ViewFile)), Read(Path.Combine(folder, ExampleFile)), renders);
    }

    /// <summary>Writes the files into <paramref name="folder"/>. The folder must not exist.</summary>
    public void WriteFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SchemaFile), Schema);
        if (Meta is not null) File.WriteAllText(Path.Combine(folder, MetaFile), Meta);
        if (View is not null) File.WriteAllText(Path.Combine(folder, ViewFile), View);
        if (Example is not null) File.WriteAllText(Path.Combine(folder, ExampleFile), Example);
        foreach (var (name, text) in Renders)
        {
            var path = name.Length == 0 ? Path.Combine(folder, RenderFile) : Path.Combine(folder, RenderFolder, $"{name}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
}

public sealed class TemplateException(string message) : Exception(message);

/// <summary>
/// The library contract of the templates. The CLI and the supervisor read and change templates only through it.
/// Today: <see cref="LocalTemplateStore"/> (two folders). Later: another implementation (Notion, a cloud bucket) that returns the same shapes.
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

/// <summary>Two folders: the core templates of the tool (read only) and the templates of the store. A store folder with a core name replaces the core template.</summary>
public sealed partial class LocalTemplateStore(string coreRoot, string? storeRoot, TemplateSpec? spec = null) : ITemplateStore
{
    /// <summary>The folder in the store root that keeps the removed templates.</summary>
    public const string Removed = ".removed";

    public string CoreRoot { get; } = coreRoot;
    public string? StoreRoot { get; } = storeRoot;

    public IReadOnlyList<TemplateInfo> List()
    {
        var core = Kinds(CoreRoot);
        var store = StoreRoot is null ? [] : Kinds(StoreRoot);
        return core.Union(store).Order(StringComparer.Ordinal).Select(kind => store.Contains(kind)
            ? new TemplateInfo(kind, TemplateSources.Store, core.Contains(kind), Path.Combine(StoreRoot!, kind))
            : new TemplateInfo(kind, TemplateSources.Core, false, Path.Combine(CoreRoot, kind))).ToList();
    }

    public TemplateFiles? Get(string kind)
    {
        if (!IsName(kind)) return null;
        var info = List().FirstOrDefault(t => t.Kind == kind);
        return info is null ? null : TemplateFiles.ReadFolder(info.Location);
    }

    public TemplateInfo Add(TemplateFiles files, bool replace = false)
    {
        if (StoreRoot is null) throw new TemplateException("this template store has no store folder");
        var errors = TemplateCheck.Check(files, spec ?? TemplateSpec.Load(CoreRoot));
        if (errors.Count > 0) throw new TemplateException($"{files.Kind} is not a valid template: {string.Join("; ", errors)}");
        var folder = Path.Combine(StoreRoot, files.Kind);
        if (Directory.Exists(folder))
        {
            if (!replace) throw new TemplateException($"{files.Kind} is in the store already. Use --replace.");
            Archive(folder, files.Kind);
        }
        var temp = Path.Combine(StoreRoot, $".add-{files.Kind}-{Guid.NewGuid():N}");
        files.WriteFolder(temp);
        Directory.Move(temp, folder);
        return List().First(t => t.Kind == files.Kind);
    }

    public void Remove(string kind)
    {
        var folder = StoreRoot is null || !IsName(kind) ? null : Path.Combine(StoreRoot, kind);
        if (folder is null || !Directory.Exists(folder))
            throw new TemplateException(Kinds(CoreRoot).Contains(kind) ? $"{kind} is a core template. Only a store template can be removed." : $"no template {kind} in the store");
        Archive(folder, kind);
    }

    void Archive(string folder, string kind)
    {
        var target = Path.Combine(StoreRoot!, Removed, $"{kind}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(folder, target);
    }

    /// <summary>The kinds in a root: each folder with a valid name and a schema.json. Folders that start with "_" or "." are not kinds.</summary>
    static HashSet<string> Kinds(string root) =>
        Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>()
                .Where(name => IsName(name) && File.Exists(Path.Combine(root, name, TemplateFiles.SchemaFile))).ToHashSet()
            : [];

    /// <summary>A kind name: not a reserved word; lower case, digits and "-", 2 to 32 characters, a letter first. No dot: dots are for the system kinds (pr.opened).</summary>
    public static bool IsName(string kind) => NameRule().IsMatch(kind) && !Reserved.Contains(kind);

    /// <summary>The words of the CLI command "orch template": not kind names.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "list", "check", "add", "remove" };

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$")]
    private static partial Regex NameRule();
}
