using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Store;

namespace Orchestrator.Supervisor;

public enum RecoveryResult { Delivered, Resend }

public sealed class Recovery(FileStore store)
{
    public TurnMarker? Interrupted(string id) => store.ReadJson<TurnMarker>(id, StoreFiles.Turn) is { Ended: null } marker ? marker : null;

    public bool IsOldProcessAlive(string id, DateTimeOffset now) =>
        store.Lease(id).Holder == Holders.Supervisor && store.Heartbeat(id) is { } beat && now - beat < LeaseRules.LiveWindow;

    public RecoveryResult Recover(string id, TurnMarker marker)
    {
        var start = store.Events.Find(marker.Event);
        var since = (Times.Parse(marker.At) ?? DateTimeOffset.MinValue) - TimeSpan.FromSeconds(1);
        var delivered = store.Events.Since(since).Any(e => e.Kind == EventKinds.SessionInput && e.Session == id && e.Ref("turnStart") == marker.Event);
        if (delivered)
        {
            store.MarkSent(id, start?.RefList("envelopes") ?? [], Holders.Supervisor);
            store.Events.Append(EventKinds.TurnEnd, Parties.Session(id), Parties.Supervisor, Data.Of(("ok", true), ("recovered", true), ("text", "the turn ended while the supervisor was down")), Data.Of(("turnStart", marker.Event)), id);
            store.Log($"{id} recovered turn {marker.Event}: the prompt arrived, marked sent");
        }
        else
        {
            if (store.Events.Body(start ?? Placeholder(marker)) is { } body && body.TryGetProperty("isNew", out var isNew) && isNew.ValueKind == JsonValueKind.True)
                store.SaveMeta(store.Meta(id) with { Started = false });
            store.Events.Append(EventKinds.TurnFail, Parties.Session(id), Parties.Supervisor, Data.Of(("error", "supervisor restart before the prompt arrived"), ("recovered", true)), Data.Of(("turnStart", marker.Event)), id);
            store.Log($"{id} recovered turn {marker.Event}: the prompt did not arrive, its envelopes have no ack and go in the next turn");
        }
        store.WriteJson(id, marker with { Ended = store.Now }, StoreFiles.Turn);
        return delivered ? RecoveryResult.Delivered : RecoveryResult.Resend;
    }

    static Event Placeholder(TurnMarker marker) => new(marker.Event, marker.At, null, EventKinds.TurnStart, "", "", "", "", new Dictionary<string, JsonElement>());
}
