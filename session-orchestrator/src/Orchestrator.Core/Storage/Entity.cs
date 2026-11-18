namespace Orchestrator.Core.Storage;

/// <summary>
/// A copy of the VSA entity (<c>vsa-template/packs/Vsa/Kernel/Domain/Entity.cs</c>), with a version.
/// The implementation sets <see cref="Version"/> at each read and save. A save of an old version fails.
/// </summary>
public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    /// <summary>The version of the stored entity. 0: the entity is not stored.</summary>
    public long Version { get; set; }
}
