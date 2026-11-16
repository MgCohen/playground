using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Packs.Domain.Shared;
using PatternEngine.Plugins.Settings;

namespace PatternEngine.Features.Packs.Domain;

/// <summary>A loaded pack: its code, its folder, its merged settings and its catalog.</summary>
public sealed record PackContext(IDomainPack Pack, string Dir, Settings Settings, PatternCatalog Catalog)
{
    public string Instructions => File.Exists(Path.Combine(Dir, "instructions.md")) ? File.ReadAllText(Path.Combine(Dir, "instructions.md")) : string.Empty;

    public string AgentsDir => Path.Combine(Dir, "agents");
}

public sealed record EngineRoot(string Dir)
{
    public const string SettingsFile = "pe.settings.yaml";

    public static EngineRoot Find(string? start = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable("PE_ROOT");
        foreach (var origin in new[] { fromEnv, start, Directory.GetCurrentDirectory(), AppContext.BaseDirectory }.OfType<string>())
        {
            for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, SettingsFile)))
                {
                    return new EngineRoot(dir.FullName);
                }
            }
        }

        throw new InvalidOperationException($"cannot find {SettingsFile}; set PE_ROOT");
    }

    public string Path(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Dir, relative));
}

public sealed class PackRegistry(IEnumerable<IDomainPack> packs, EngineRoot root, Settings settings)
{
    private readonly Dictionary<string, IDomainPack> byName = packs.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public IEnumerable<string> Names => byName.Keys;

    public Settings Settings => settings;

    public EngineRoot Root => root;

    public string DirOf(IDomainPack pack) => Path.Combine(root.Path(settings.Section("paths").Text("domains")), pack.Folder);

    /// <summary>Loads a pack. A patterns folder can replace the catalog, for example the folder of a sandbox with proposed patterns.</summary>
    public PackContext Load(string? name = null, string? patternsDir = null)
    {
        name ??= settings.Text("default_domain");
        var pack = byName.GetValueOrDefault(name) ?? throw new InvalidOperationException($"no pack \"{name}\" (packs: {string.Join(", ", byName.Keys)})");
        var dir = DirOf(pack);
        var merged = Settings.Load(Path.Combine(root.Dir, EngineRoot.SettingsFile), Path.Combine(dir, "pack.settings.yaml"));
        return new PackContext(pack, dir, merged, PatternCatalog.Load(patternsDir ?? Path.Combine(dir, "patterns")));
    }
}
