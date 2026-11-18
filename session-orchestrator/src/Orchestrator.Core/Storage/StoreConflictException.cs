namespace Orchestrator.Core.Storage;

public enum StoreConflict
{
    /// <summary>A save of an add found an entity with the same id.</summary>
    Exists,

    /// <summary>A save found a newer version of the entity than the version that the caller read.</summary>
    Stale,
}

/// <summary>A save that broke one of the two rules of <see cref="IRepository{T, TId}"/>. The save wrote nothing for this entity.</summary>
public sealed class StoreConflictException(StoreConflict conflict, string id, string message) : Exception(message)
{
    public StoreConflict Conflict { get; } = conflict;
    public string Id { get; } = id;
}
