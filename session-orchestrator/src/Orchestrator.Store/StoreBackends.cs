using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;

namespace Orchestrator.Store;

/// <summary>The only place that selects the store implementation. No configuration: a new backend is a code change here.</summary>
public static class StoreBackends
{
    public static IRepositories Create(string root) => new LocalRepositories(root);
}
