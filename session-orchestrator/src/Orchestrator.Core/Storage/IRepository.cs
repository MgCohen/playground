namespace Orchestrator.Core.Storage;

/// <summary>
/// A copy of the VSA repository contract (<c>vsa-template/packs/Vsa/Kernel/Infrastructure/IRepository.cs</c>).
/// <see cref="Add"/> and <see cref="Remove"/> wait for <see cref="SaveChangesAsync"/>. A save also writes each changed
/// entity that a get or a list returned. Each implementation follows two rules, and the contract tests check them:
/// a save of an <see cref="Add"/> with an id that exists fails, and a save of an entity with an old version fails.
/// Both failures are a <see cref="StoreConflictException"/>.
/// </summary>
public interface IRepository<T, TId> where T : Entity<TId> where TId : notnull
{
    Task<T?> GetAsync(TId id, CancellationToken ct);
    Task<List<T>> ListAsync(Func<T, bool> where, CancellationToken ct);
    void Add(T entity);
    void Remove(T entity);
    Task SaveChangesAsync(CancellationToken ct);
}
