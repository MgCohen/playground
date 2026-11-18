namespace Orchestrator.Core.Storage;

/// <summary>
/// Short synchronous units of work on a repository: read, put, create and delete one document. The store code is
/// synchronous, so these calls wait for the task. The local folder implementation completes each task at once.
/// </summary>
public static class RepositoryExtensions
{
    const int Attempts = 20;

    public static Doc<T>? Get<T>(this IRepository<Doc<T>, string> repository, string id) =>
        repository.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult();

    public static List<Doc<T>> List<T>(this IRepository<Doc<T>, string> repository, Func<Doc<T>, bool> where) =>
        repository.ListAsync(where, CancellationToken.None).GetAwaiter().GetResult();

    public static void Save<T>(this IRepository<Doc<T>, string> repository) =>
        repository.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>The value, or default when no document has this id.</summary>
    public static T? Read<T>(this IRepository<Doc<T>, string> repository, string id) =>
        repository.Get(id) is { } doc ? doc.Value : default;

    public static bool Exists<T>(this IRepository<Doc<T>, string> repository, string id) => repository.Get(id) is not null;

    /// <summary>
    /// Writes the value, new or not. The last writer wins, as with a plain file write: on a conflict with another
    /// writer, the put reads the new version and writes again.
    /// </summary>
    public static void Put<T>(this IRepository<Doc<T>, string> repository, string id, T value)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (repository.Get(id) is { } doc) doc.Value = value;
                else repository.Add(new Doc<T>(id, value));
                repository.Save();
                return;
            }
            catch (StoreConflictException) when (attempt < Attempts)
            {
            }
        }
    }

    /// <summary>Adds the value only when no document has this id. False: another writer was first.</summary>
    public static bool TryCreate<T>(this IRepository<Doc<T>, string> repository, string id, T value)
    {
        try
        {
            repository.Add(new Doc<T>(id, value));
            repository.Save();
            return true;
        }
        catch (StoreConflictException e) when (e.Conflict == StoreConflict.Exists)
        {
            return false;
        }
    }

    /// <summary>Removes the document when it exists. The last writer wins.</summary>
    public static void Delete<T>(this IRepository<Doc<T>, string> repository, string id)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (repository.Get(id) is not { } doc) return;
                repository.Remove(doc);
                repository.Save();
                return;
            }
            catch (StoreConflictException) when (attempt < Attempts)
            {
            }
        }
    }
}
