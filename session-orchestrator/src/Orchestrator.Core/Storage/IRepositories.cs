using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;

namespace Orchestrator.Core.Storage;

/// <summary>
/// The repositories of the store: one for each data kind of the port map in <c>docs/plan.md</c> (H1).
/// <c>StoreBackends.Create</c> selects the implementation. All ids are strings.
/// </summary>
public interface IRepositories
{
    /// <summary>Where the store is, for messages and checks. For the local folder: the full path of the folder.</summary>
    string Location { get; }

    /// <summary>The envelopes. Id: the envelope id.</summary>
    IRepository<Doc<Event>, string> Events { get; }

    /// <summary>The envelope bodies. Id: the body path of the envelope, for example <c>bodies/2026-11-18/&lt;id&gt;.json</c>.</summary>
    IRepository<Doc<JsonElement>, string> Bodies { get; }

    /// <summary>The policy. Id: <see cref="StoreIds.Policy"/>.</summary>
    IRepository<Doc<Policy.Policy>, string> Policies { get; }

    /// <summary>The store settings. Id: <see cref="StoreIds.Version"/>.</summary>
    IRepository<Doc<StoreVersion>, string> Settings { get; }

    /// <summary>The claims: the first writer wins. Id: <c>&lt;kind&gt;/&lt;key&gt;</c>, for example <c>answer/&lt;thread&gt;</c>. Value: the writer.</summary>
    IRepository<Doc<string>, string> Claims { get; }

    /// <summary>The cursors of the subscriptions. Id: <c>&lt;party folder&gt;/&lt;name&gt;</c>.</summary>
    IRepository<Doc<CursorState>, string> Cursors { get; }

    /// <summary>The nudges. Id: the party folder. Value: the envelope id, or empty.</summary>
    IRepository<Doc<string>, string> Nudges { get; }

    /// <summary>The sessions that have run state. Id and value: the session id.</summary>
    IRepository<Doc<string>, string> Sessions { get; }

    /// <summary>
    /// One run state file of each session, for example the lease (<see cref="StoreFiles.Lease"/>). Id: the session id.
    /// A <see cref="string"/> value is the text of the file. Any other value is JSON.
    /// </summary>
    IRepository<Doc<T>, string> SessionFile<T>(string file);

    /// <summary>The items of one folder of one session, for example the stack entries. Id: the item name with no <c>.json</c>.</summary>
    IRepository<Doc<T>, string> SessionItems<T>(string session, string folder);

    /// <summary>The supervisor log.</summary>
    ILogSink Log { get; }

    /// <summary>True when the store exists.</summary>
    bool Exists();

    /// <summary>Makes the store ready for the first writes.</summary>
    void Prepare();

    /// <summary>A new push of the envelopes and the nudges.</summary>
    IStreamSignal Watch();
}

public static class StoreIds
{
    public const string Policy = "policy";
    public const string Version = "version";

    public static string AnswerClaim(string thread) => $"answer/{thread}";
}
