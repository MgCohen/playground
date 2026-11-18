using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;

namespace Orchestrator.Store.Remote;

/// <summary>
/// The wire format of the store API (H5): <c>/api/store</c> of the supervisor. Each repository of
/// <see cref="IRepositories"/> is one kind. A document goes as its text: the same text as the file of the local store,
/// so the version (the hash of the text) is the same on both sides. The layout is in <c>docs/store.md</c>.
/// </summary>
public static class StoreWire
{
    public const string Prefix = "/api/store";

    public const string Events = "events";
    public const string Bodies = "bodies";
    public const string Policies = "policies";
    public const string StoreVersions = "versions";
    public const string Claims = "claims";
    public const string Cursors = "cursors";
    public const string Nudges = "nudges";
    public const string Sessions = "sessions";

    /// <summary>The session registry (S2): one entry for each session.</summary>
    public const string SessionEntries = "session-entries";

    /// <summary>The workflow runs (C2).</summary>
    public const string WorkflowRuns = "workflow-runs";

    /// <summary>One run state file of each session. Query key <c>file</c>.</summary>
    public const string SessionFile = "session-file";

    /// <summary>The items of one folder of one session. Query keys <c>session</c> and <c>folder</c>.</summary>
    public const string SessionItems = "session-items";

    /// <summary>The definitions of one catalog. Query key <c>catalog</c>.</summary>
    public const string Definitions = "definitions";

    public static readonly IReadOnlyList<string> Kinds = [Events, Bodies, Policies, StoreVersions, Claims, Cursors, Nudges, Sessions, SessionEntries, WorkflowRuns, SessionFile, SessionItems, Definitions];

    /// <summary>
    /// True for an id that stays in the store: one or more parts with "/" between them, and no empty part, ".", "..",
    /// "\", ":" or control character. The API refuses each other id, so a remote caller cannot name a path outside the store.
    /// </summary>
    public static bool IsId(string? id) =>
        id is { Length: > 0 and <= 400 } && id.Split('/').All(IsPart);

    /// <summary>True for one part of a path: a file name, a folder name or a session id.</summary>
    public static bool IsPart(string? part) =>
        part is { Length: > 0 and <= 200 } and not ("." or "..") && part.All(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'));
}

/// <summary>The place of one repository: the kind and its query keys.</summary>
public sealed record WireScope(string Kind, string? File = null, string? Session = null, string? Folder = null, string? Catalog = null)
{
    public string Query()
    {
        var keys = new List<string>();
        if (File is not null) keys.Add($"file={Uri.EscapeDataString(File)}");
        if (Session is not null) keys.Add($"session={Uri.EscapeDataString(Session)}");
        if (Folder is not null) keys.Add($"folder={Uri.EscapeDataString(Folder)}");
        if (Catalog is not null) keys.Add($"catalog={Uri.EscapeDataString(Catalog)}");
        return string.Join('&', keys);
    }
}

/// <summary>One document on the wire. <c>Text</c>: the text of the document, as in its local file.</summary>
public sealed record WireDoc(string Id, long Version, string Text);

/// <summary>One save: the adds, the changes and the removes, as <see cref="IRepository{T, TId}.SaveChangesAsync"/>.</summary>
public sealed record WireSave(List<WireDoc> Adds, List<WireDoc> Changes, List<WireDoc> Removes);

/// <summary>The result of a save: the new version of each id. A removed id has version 0.</summary>
public sealed record WireSaved(Dictionary<string, long> Versions);

/// <summary>A refused save (HTTP 409): one of the two rules of <see cref="IRepository{T, TId}"/>.</summary>
public sealed record WireConflict(string Conflict, string Id, string Message);

/// <summary>The new envelopes after a position of the feed of the supervisor. <c>Epoch</c> changes when the feed starts again.</summary>
public sealed record WireFeed(string Epoch, int Position, List<WireDoc> Docs);

/// <summary>The store facts for a remote caller.</summary>
public sealed record WireInfo(string Location, bool Exists, int Version);

public sealed record WireLine(string Line);

/// <summary>A definition on the wire: the JSON of its files map.</summary>
public sealed class DefinitionCodec : IFileCodec<Definition>
{
    public static readonly DefinitionCodec Instance = new();

    public Definition Decode(string text, string path) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(text, StoreJson.Options) ?? []);

    public string Encode(Definition value) => StoreJson.Write(value.Files);
}
