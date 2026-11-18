using System.Text;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store.Local;

/// <summary>How one data kind maps to the text of its file.</summary>
public interface IFileCodec<T>
{
    T Decode(string text, string path);
    string Encode(T value);
}

/// <summary>
/// The local folder implementation of <see cref="IRepository{T, TId}"/> for one data kind: one file for each document.
/// The version of a document is a hash of its file text. A save writes each add with an exclusive create, and checks
/// the version of each change and remove before the write.
/// </summary>
public sealed class LocalFileRepository<T> : IRepository<Doc<T>, string>
{
    readonly Func<string, string> pathOf;
    readonly Func<IEnumerable<string>> ids;
    readonly IFileCodec<T> codec;
    readonly bool lockFiles;
    readonly Lock gate = new();
    readonly List<Doc<T>> added = [];
    readonly List<Doc<T>> removed = [];
    readonly List<Doc<T>> changed = [];

    /// <param name="pathOf">The full file path of an id.</param>
    /// <param name="ids">The ids of all files on disk, in order.</param>
    /// <param name="codec">The text of the file.</param>
    /// <param name="lockFiles">
    /// True: a save of a change holds <c>&lt;file&gt;.lock</c> for the version check and the write, so two processes
    /// cannot both pass the check. The cursors use it.
    /// </param>
    public LocalFileRepository(Func<string, string> pathOf, Func<IEnumerable<string>> ids, IFileCodec<T> codec, bool lockFiles = false)
    {
        this.pathOf = pathOf;
        this.ids = ids;
        this.codec = codec;
        this.lockFiles = lockFiles;
    }

    /// <summary>The longest wait for a lock file. After it, the save fails with an IOException.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    public Task<Doc<T>?> GetAsync(string id, CancellationToken ct) => Task.FromResult(Load(id));

    public Task<List<Doc<T>>> ListAsync(Func<Doc<T>, bool> where, CancellationToken ct) =>
        Task.FromResult(ids().Select(Load).OfType<Doc<T>>().Where(where).ToList());

    public void Add(Doc<T> entity)
    {
        lock (gate) added.Add(entity);
    }

    public void Remove(Doc<T> entity)
    {
        lock (gate) removed.Add(entity);
    }

    /// <summary>Writes the adds, then the changes, then the removes. The save stops at the first conflict; the writes before it stay.</summary>
    public Task SaveChangesAsync(CancellationToken ct)
    {
        List<Doc<T>> adds, changes, removes;
        lock (gate)
        {
            adds = [.. added];
            changes = [.. changed.Distinct().Where(d => d.IsChanged && !removed.Contains(d))];
            removes = [.. removed];
            added.Clear();
            changed.Clear();
            removed.Clear();
        }
        foreach (var doc in adds)
        {
            var text = codec.Encode(doc.Value);
            if (!Files.Create(pathOf(doc.Id), text)) throw new StoreConflictException(StoreConflict.Exists, doc.Id, $"{doc.Id} exists. Nothing changed.");
            doc.Saved(Versions.Of(text));
            doc.Track(Enlist);
        }
        foreach (var doc in changes)
        {
            var text = codec.Encode(doc.Value);
            Checked(doc, path => Files.Write(path, text));
            doc.Saved(Versions.Of(text));
        }
        foreach (var doc in removes)
        {
            Checked(doc, Files.Delete);
            doc.Saved(0);
        }
        return Task.CompletedTask;
    }

    Doc<T>? Load(string id)
    {
        var path = pathOf(id);
        if (Files.ReadText(path) is not { } text) return null;
        var doc = new Doc<T>(id, codec.Decode(text, path)) { Version = Versions.Of(text) };
        doc.Track(Enlist);
        return doc;
    }

    void Enlist(Doc<T> doc)
    {
        lock (gate) changed.Add(doc);
    }

    void Checked(Doc<T> doc, Action<string> write)
    {
        var path = pathOf(doc.Id);
        using var held = lockFiles ? Hold(path) : null;
        var current = Files.ReadText(path);
        var version = current is null ? 0 : Versions.Of(current);
        if (version != doc.Version)
            throw new StoreConflictException(StoreConflict.Stale, doc.Id, $"{doc.Id} changed after the read (version {version}, read {doc.Version}). Nothing changed.");
        write(path);
    }

    static FileStream Hold(string path)
    {
        var file = path + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var end = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (DateTime.UtcNow < end)
            {
                Thread.Sleep(10);
            }
        }
    }
}

/// <summary>The version of a local file: a 64-bit FNV-1a hash of its text. Never 0, because 0 means "not stored".</summary>
public static class Versions
{
    public static long Of(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash == 0 ? 1 : unchecked((long)hash);
    }
}

public sealed class TextCodec : IFileCodec<string>
{
    public static readonly TextCodec Instance = new();

    public string Decode(string text, string path) => text;

    public string Encode(string value) => value;
}

/// <summary>JSON in the store format. A file that is not valid JSON gives the default value.</summary>
public sealed class JsonCodec<T> : IFileCodec<T>
{
    public static readonly JsonCodec<T> Instance = new();

    public T Decode(string text, string path) => Core.Contract.StoreJson.Read<T>(text)!;

    public string Encode(T value) => Core.Contract.StoreJson.Write(value);
}

public sealed class PolicyCodec : IFileCodec<Core.Policy.Policy>
{
    public static readonly PolicyCodec Instance = new();

    public Core.Policy.Policy Decode(string text, string path) => Core.Policy.Policy.Parse(text, path);

    public string Encode(Core.Policy.Policy value) => Core.Contract.StoreJson.Write(value);
}
