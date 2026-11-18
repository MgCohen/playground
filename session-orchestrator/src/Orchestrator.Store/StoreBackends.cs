using Orchestrator.Core.Storage;
using Orchestrator.Core.Templates;
using Orchestrator.Store.Local;

namespace Orchestrator.Store;

/// <summary>The only place that selects the store implementation. No configuration: a new backend is a code change here.</summary>
public static class StoreBackends
{
    public static IRepositories Create(string root) => new LocalRepositories(root);

    /// <summary>The core definitions of one catalog: the templates root of the tool (<c>&lt;home&gt;/templates</c>), read only by rule.</summary>
    public static DefinitionSource Core(string templatesRoot, Catalog catalog) =>
        Source(TemplateSources.Core, new LocalDefinitionRepository(templatesRoot, catalog), templatesRoot);

    /// <summary>The definitions of one catalog in a store.</summary>
    public static DefinitionSource Store(IRepositories repos, Catalog catalog) =>
        Source(TemplateSources.Store, repos.Definitions(catalog), repos.Location);

    static DefinitionSource Source(string name, IRepository<Doc<Definition>, string> repository, string location) =>
        new(name, repository, repository is LocalDefinitionRepository local ? local.FolderOf : id => $"{location}: {id}");
}
