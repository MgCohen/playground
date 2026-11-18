using Orchestrator.Core.Contract;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;

namespace Orchestrator.Store;

public static class Home
{
    public const string Solution = "Orchestrator.slnx";

    public static string Root() =>
        Environment.GetEnvironmentVariable("ORCH_HOME") is { Length: > 0 } home ? home : FindUp(AppContext.BaseDirectory) ?? Built() ?? Environment.CurrentDirectory;

    static string? Built() =>
        typeof(Home).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false).OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OrchHome")?.Value is { } path && File.Exists(Path.Combine(path, Solution)) ? Path.GetFullPath(path) : null;

    /// <summary>The store root: <c>ORCH_STORE</c>, else <see cref="DefaultStoreRoot"/>.</summary>
    public static string StoreRoot() =>
        Environment.GetEnvironmentVariable("ORCH_STORE") is { Length: > 0 } store ? Path.GetFullPath(store) : DefaultStoreRoot();

    /// <summary>The default port of the supervisor.</summary>
    public const int DefaultPort = 7777;

    /// <summary>
    /// The store of the <c>orch</c> command (H5). <c>ORCH_URL</c>: the store API of that supervisor (HTTP mode). With no
    /// <c>ORCH_URL</c>, no <c>ORCH_STORE</c> and no folder at the default store root, the store API on 127.0.0.1
    /// (<c>ORCH_PORT</c>, else 7777), if <paramref name="fallback"/>. Else the local folder. A <paramref name="local"/>
    /// command (the supervisor start, the store migration) always uses the local folder.
    /// </summary>
    public static string CliStoreRoot(bool local = false, bool fallback = true)
    {
        if (local) return StoreRoot();
        if (Environment.GetEnvironmentVariable("ORCH_URL") is { Length: > 0 } url) return url.TrimEnd('/');
        if (!fallback || Environment.GetEnvironmentVariable("ORCH_STORE") is { Length: > 0 } || Directory.Exists(DefaultStoreRoot())) return StoreRoot();
        var port = int.TryParse(Environment.GetEnvironmentVariable("ORCH_PORT"), out var p) ? p : DefaultPort;
        return $"http://127.0.0.1:{port}";
    }

    /// <summary>
    /// The default store root (H1): <c>%LOCALAPPDATA%\orch\store</c>. It is out of the checkout, so a <c>git clean -fdx</c>
    /// cannot delete it. The plugin (<c>backend.ts</c> <c>storeRoot</c>) and <c>install.ps1 -Store</c> use the same default.
    /// </summary>
    public static string DefaultStoreRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "orch", "store");

    /// <summary>The default store root before H4: <c>&lt;home&gt;/store</c> in the checkout. Only <c>orch store migrate</c> uses it.</summary>
    public static string OldStoreRoot() => Path.Combine(Root(), "store");

    public static string Config(string name) => Path.Combine(Root(), "config", name);

    public static string Templates() => Path.Combine(Root(), "templates");

    /// <summary>The spec of the template system: the files of templates/_spec in the home folder.</summary>
    public static TemplateSpec Spec() => TemplateSpec.Load(Templates());

    /// <summary>
    /// One catalog of the template system: the core definitions of the tool, and the definitions of the store that add
    /// to them or replace them. No store root: the core definitions only.
    /// </summary>
    public static LayeredDefinitions Definitions(Catalog catalog, string? storeRoot) =>
        DefinitionsOf(catalog, storeRoot is null ? null : StoreBackends.Create(storeRoot));

    /// <summary>One catalog of the template system on the repositories of a store (a local folder or the store API).</summary>
    public static LayeredDefinitions DefinitionsOf(Catalog catalog, IRepositories? store) =>
        new(catalog, StoreBackends.Core(Templates(), catalog), store is null ? null : StoreBackends.Store(store, catalog));

    /// <summary>The message templates of a store root.</summary>
    public static TemplateStore TemplateStore(string? storeRoot) => new(Definitions(Catalog.Templates, storeRoot), Spec);

    public static TemplateSet LoadTemplates(string? storeRoot) => new(TemplateStore(storeRoot), Templates());

    /// <summary>The message templates of the repositories of a store.</summary>
    public static TemplateSet LoadTemplatesOf(IRepositories store) => new(new TemplateStore(DefinitionsOf(Catalog.Templates, store), Spec), Templates());

    /// <summary>The session modes (S1) of a store root.</summary>
    public static DefinitionCatalog<ModeDefinition> Modes(string? storeRoot)
    {
        var spec = new Lazy<TemplateSpec>(Spec);
        return new(Definitions(Catalog.Modes, storeRoot), (name, d) => ModeDefinition.Check(name, d, spec.Value));
    }

    /// <summary>The workflow definitions (C1) of a store root. <paramref name="templates"/> checks the message kinds that a step names.</summary>
    public static DefinitionCatalog<WorkflowDefinition> Workflows(string? storeRoot, TemplateSet? templates = null)
    {
        var spec = new Lazy<TemplateSpec>(Spec);
        return new(Definitions(Catalog.Workflows, storeRoot), (name, d) => WorkflowDefinition.Check(name, d, spec.Value, templates is null ? null : templates.Has));
    }

    public static Policy LoadPolicy(string storeRoot) => LoadPolicy(StoreBackends.Create(storeRoot));

    /// <summary>The policy of the store. A store with no policy gets a copy of config/policy.default.json.</summary>
    public static Policy LoadPolicy(IRepositories repos)
    {
        if (repos.Policies.Read(StoreIds.Policy) is { } stored) return stored;
        var source = Config("policy.default.json");
        var policy = Policy.Parse(File.ReadAllText(source), source);
        repos.Policies.TryCreate(StoreIds.Policy, policy);
        return policy;
    }

    public static StoreVersion CheckVersion(string storeRoot, DateTimeOffset now) => CheckVersion(StoreBackends.Create(storeRoot), now);

    public static StoreVersion CheckVersion(IRepositories repos, DateTimeOffset now)
    {
        var storeRoot = repos.Location;
        if (repos.StoreVersions.Read(StoreIds.Version) is { } found)
        {
            if (found.Version > StoreVersion.Current)
                throw new InvalidDataException($"store {storeRoot} has version {found.Version}, newer than this supervisor ({StoreVersion.Current}). Update the orchestrator.");
            if (found.Version < StoreVersion.Current)
                throw new InvalidDataException($"store {storeRoot} has version {found.Version}, older than this supervisor ({StoreVersion.Current}). There is no migration: move the old store to another folder and start with a new store.");
            return found;
        }
        var version = new StoreVersion(StoreVersion.Current, Core.Leases.Times.Format(now));
        repos.StoreVersions.Put(StoreIds.Version, version);
        return version;
    }

    static string? FindUp(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Solution))) return dir.FullName;
        return null;
    }
}
