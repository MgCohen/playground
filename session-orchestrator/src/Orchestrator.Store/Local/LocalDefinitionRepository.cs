using System.Text;
using Orchestrator.Core.Storage;

namespace Orchestrator.Store.Local;

/// <summary>
/// The local folder implementation of one definition catalog: one folder for each definition, with the marker file of
/// the catalog (<see cref="Catalog.Marker"/>). The version is a hash of all the files of the folder. An add and a change
/// write a temp folder, then move it in. A change and a remove move the old folder to <see cref="Removed"/>, so no file
/// is lost.
/// </summary>
public sealed class LocalDefinitionRepository : IRepository<Doc<Definition>, string>
{
    /// <summary>The folder in the catalog root that keeps the removed and replaced definitions.</summary>
    public const string Removed = ".removed";

    readonly string root;
    readonly Catalog catalog;
    readonly Lock gate = new();
    readonly List<Doc<Definition>> added = [];
    readonly List<Doc<Definition>> removed = [];
    readonly List<Doc<Definition>> changed = [];

    /// <param name="templatesRoot">The templates root: <c>&lt;home&gt;/templates</c> (core) or <c>&lt;store&gt;/templates</c>.</param>
    public LocalDefinitionRepository(string templatesRoot, Catalog catalog)
    {
        this.catalog = catalog;
        root = Path.GetFullPath(catalog.Folder.Length == 0 ? templatesRoot : Path.Combine(templatesRoot, catalog.Folder));
    }

    /// <summary>The folder of the catalog.</summary>
    public string Root => root;

    /// <summary>The folder of one definition.</summary>
    public string FolderOf(string id) => Path.Combine(root, id);

    public Task<Doc<Definition>?> GetAsync(string id, CancellationToken ct) => Task.FromResult(Load(id));

    public Task<List<Doc<Definition>>> ListAsync(Func<Doc<Definition>, bool> where, CancellationToken ct) =>
        Task.FromResult(Ids().Select(Load).OfType<Doc<Definition>>().Where(where).ToList());

    public void Add(Doc<Definition> entity)
    {
        lock (gate) added.Add(entity);
    }

    public void Remove(Doc<Definition> entity)
    {
        lock (gate) removed.Add(entity);
    }

    /// <summary>Writes the adds, then the changes, then the removes. The save stops at the first conflict; the writes before it stay.</summary>
    public Task SaveChangesAsync(CancellationToken ct)
    {
        List<Doc<Definition>> adds, changes, removes;
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
            Check(doc.Id);
            var temp = WriteTemp(doc.Id, doc.Value);
            try
            {
                Directory.Move(temp, FolderOf(doc.Id));
            }
            catch (IOException) when (Directory.Exists(FolderOf(doc.Id)))
            {
                Directory.Delete(temp, true);
                throw new StoreConflictException(StoreConflict.Exists, doc.Id, $"{doc.Id} exists. Nothing changed.");
            }
            doc.Saved(VersionOf(doc.Value));
            doc.Track(Enlist);
        }
        foreach (var doc in changes)
        {
            Stale(doc);
            var temp = WriteTemp(doc.Id, doc.Value);
            Archive(doc.Id);
            Directory.Move(temp, FolderOf(doc.Id));
            doc.Saved(VersionOf(doc.Value));
        }
        foreach (var doc in removes)
        {
            Stale(doc);
            Archive(doc.Id);
            doc.Saved(0);
        }
        return Task.CompletedTask;
    }

    /// <summary>The files of a folder outside the store, for example a new template of the user.</summary>
    public static Definition ReadFolder(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"{folder} does not exist");
        return new Definition(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".tmp"))
            .Select(f => new KeyValuePair<string, string>(Path.GetRelativePath(folder, f).Replace('\\', '/'), Files.ReadText(f)!)));
    }

    /// <summary>Writes the files of a definition into a folder outside the store.</summary>
    public static void WriteFolder(string folder, Definition definition)
    {
        foreach (var (path, text) in definition.Files) Files.Write(Path.Combine(folder, path), text);
    }

    /// <summary>The version of a definition: the FNV-1a hash of each path and text, in path order.</summary>
    public static long VersionOf(Definition definition)
    {
        var all = new StringBuilder();
        foreach (var (path, text) in definition.Files) all.Append(path).Append('\0').Append(text).Append('\0');
        return Versions.Of(all.ToString());
    }

    IEnumerable<string> Ids() =>
        Files.Names(root).Where(name => name[0] != '.' && name[0] != '_' && File.Exists(Path.Combine(root, name, catalog.Marker)));

    Doc<Definition>? Load(string id)
    {
        if (!IsId(id)) return null;
        var folder = FolderOf(id);
        if (!File.Exists(Path.Combine(folder, catalog.Marker))) return null;
        var definition = ReadFolder(folder);
        var doc = new Doc<Definition>(id, definition) { Version = VersionOf(definition) };
        doc.Track(Enlist);
        return doc;
    }

    void Enlist(Doc<Definition> doc)
    {
        lock (gate) changed.Add(doc);
    }

    void Stale(Doc<Definition> doc)
    {
        var current = Load(doc.Id);
        var version = current?.Version ?? 0;
        if (version != doc.Version)
            throw new StoreConflictException(StoreConflict.Stale, doc.Id, $"{doc.Id} changed after the read (version {version}, read {doc.Version}). Nothing changed.");
    }

    string WriteTemp(string id, Definition definition)
    {
        var temp = Path.Combine(root, $".add-{id}-{Guid.NewGuid():N}");
        WriteFolder(temp, definition);
        Directory.CreateDirectory(temp);
        return temp;
    }

    void Archive(string id)
    {
        var folder = FolderOf(id);
        if (!Directory.Exists(folder)) return;
        var target = Path.Combine(root, Removed, $"{id}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid().ToString()[..4]}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(folder, target);
    }

    /// <summary>An id is one folder name: no path parts, no dot or "_" first.</summary>
    static bool IsId(string id) => id.Length > 0 && id[0] is not ('.' or '_') && id.IndexOfAny(['/', '\\', ':']) < 0;

    static void Check(string id)
    {
        if (!IsId(id))
            throw new ArgumentException($"\"{id}\" is not a definition id", nameof(id));
    }
}
