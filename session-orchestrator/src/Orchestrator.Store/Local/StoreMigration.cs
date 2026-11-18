using System.Security.Cryptography;
using Orchestrator.Core.Contract;

namespace Orchestrator.Store.Local;

/// <summary>The result of <see cref="StoreMigration.Copy"/>.</summary>
public sealed record MigrationResult(string From, string To, int Files, long Bytes, IReadOnlyList<string> Differences, IReadOnlyList<string> LiveSessions)
{
    public bool Ok => Differences.Count == 0;
}

/// <summary>
/// Copies a local store folder to a new root (H4): the move from <c>&lt;home&gt;/store</c> to the default store root.
/// The copy goes to a temporary folder next to the target. A check then compares each file of the source with its
/// copy (size and SHA-256). Only a copy that passes the check gets the target name. The source never changes.
/// </summary>
public static class StoreMigration
{
    /// <summary>A heartbeat newer than this shows a session that still writes to the source.</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromSeconds(30);

    public static string TempOf(string to) => $"{to.TrimEnd('\\', '/')}.migrating";

    /// <summary>True when the folder is a store: it has version.json.</summary>
    public static bool IsStore(string root) => File.Exists(Path.Combine(root, StoreFiles.Json(Core.Storage.StoreIds.Version)));

    /// <summary>The checks before the copy. Returns the reason that stops the migration, or null.</summary>
    public static string? Refuse(string from, string to)
    {
        if (string.Equals(Path.GetFullPath(from).TrimEnd('\\', '/'), Path.GetFullPath(to).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return $"the source and the target are the same folder: {from}. Set the target with --to, or unset ORCH_STORE";
        if (!IsStore(from))
            return $"{from} is not a store: it has no version.json";
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any())
            return $"the target {to} exists and is not empty. The migration never writes into a store. Move that folder away first";
        if (Directory.Exists(TempOf(to)))
            return $"{TempOf(to)} exists: an earlier migration stopped. Delete that folder (it is only a copy), then run the migration again";
        return null;
    }

    /// <summary>The sessions of the source with a heartbeat in the last <see cref="LiveWindow"/>.</summary>
    public static IReadOnlyList<string> LiveSessions(string from, DateTimeOffset now)
    {
        var sessions = Path.Combine(from, StoreFiles.Sessions);
        if (!Directory.Exists(sessions)) return [];
        return Directory.EnumerateDirectories(sessions)
            .Where(dir => File.Exists(Path.Combine(dir, StoreFiles.Heartbeat)) && now - File.GetLastWriteTimeUtc(Path.Combine(dir, StoreFiles.Heartbeat)) < LiveWindow)
            .Select(Path.GetFileName).OfType<string>().Order().ToList();
    }

    /// <summary>
    /// Copies <paramref name="from"/> to <paramref name="to"/> through the temporary folder, and checks the copy. On a
    /// difference, the temporary folder stays for examination and the target does not get made.
    /// </summary>
    public static MigrationResult Copy(string from, string to, DateTimeOffset now)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (Refuse(from, to) is { } reason) throw new IOException(reason);
        var live = LiveSessions(from, now);
        var temp = TempOf(to);
        var files = SourceFiles(from).ToList();
        long bytes = 0;
        foreach (var relative in files)
        {
            var target = Path.Combine(temp, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(from, relative), target);
            bytes += new FileInfo(target).Length;
        }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(temp, Path.GetRelativePath(from, dir)));
        var differences = Check(from, temp);
        if (differences.Count == 0)
        {
            if (Directory.Exists(to)) Directory.Delete(to);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(temp, to);
        }
        return new MigrationResult(from, to, files.Count, bytes, differences, live);
    }

    /// <summary>The differences between the source and the copy: a missing file, an extra file, or other content.</summary>
    public static IReadOnlyList<string> Check(string from, string copy)
    {
        var source = SourceFiles(from).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copied = SourceFiles(copy).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var differences = new List<string>();
        foreach (var file in source.Order(StringComparer.Ordinal))
        {
            if (!copied.Contains(file)) differences.Add($"missing in the copy: {file}");
            else if (!Same(Path.Combine(from, file), Path.Combine(copy, file))) differences.Add($"other content (the file changed during the copy): {file}");
        }
        differences.AddRange(copied.Where(f => !source.Contains(f)).Order(StringComparer.Ordinal).Select(f => $"not in the source (removed during the copy): {f}"));
        return differences;
    }

    /// <summary>The files of a store, relative to its root. Temporary files of a write in progress are not part of the store.</summary>
    static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path));

    static bool Same(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var x = File.OpenRead(a);
        using var y = File.OpenRead(b);
        return SHA256.HashData(x).AsSpan().SequenceEqual(SHA256.HashData(y));
    }
}
