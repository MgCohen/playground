using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Ordering;
using Orchestrator.Core.Policy;

namespace Orchestrator.Store;

public sealed class FileStore
{
    public FileStore(string root, TimeProvider? clock = null)
    {
        Root = Path.GetFullPath(root);
        Clock = clock ?? TimeProvider.System;
        Events = new EventStream(Root, Clock);
    }

    public string Root { get; }
    public TimeProvider Clock { get; }
    public EventStream Events { get; }

    string Now => Times.Format(Clock.GetUtcNow());

    public string Dir(string id) => Path.Combine(Root, StoreFiles.Sessions, id);

    public string PathOf(string id, params string[] parts) => Path.Combine([Dir(id), .. parts]);

    public IReadOnlyList<string> Sessions() =>
        Files.Names(Path.Combine(Root, StoreFiles.Sessions)).Where(id => File.Exists(PathOf(id, StoreFiles.Enabled))).ToList();

    public Meta Meta(string id) => Files.ReadJson<Meta>(PathOf(id, StoreFiles.Meta)) ?? new Meta(id);

    public void SaveMeta(Meta meta) => Files.WriteJson(PathOf(meta.Id, StoreFiles.Meta), meta);

    public Lease Lease(string id) => Files.ReadJson<Lease>(PathOf(id, StoreFiles.Lease)) ?? new Lease(Holders.None, "");

    public void SaveLease(string id, Lease lease) => Files.WriteJson(PathOf(id, StoreFiles.Lease), lease);

    public DateTimeOffset? Heartbeat(string id) => Times.Parse(Files.ReadText(PathOf(id, StoreFiles.Heartbeat)));

    public bool IsLive(string id, DateTimeOffset now) => LeaseRules.IsLive(Lease(id), Heartbeat(id), now);

    public string? Cwd(string id)
    {
        if (Meta(id).Cwd is { } cwd) return cwd;
        var reason = Lease(id).Reason;
        return reason is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Object && start.TryGetProperty("cwd", out var c)
            ? c.GetString()
            : null;
    }

    public string Create(Meta meta, string brief)
    {
        var id = meta.Id.Length > 0 ? meta.Id : Guid.NewGuid().ToString();
        SaveMeta(meta with { Id = id, CreatedAt = Now });
        Files.Write(PathOf(id, StoreFiles.Inbox, "1.txt"), brief);
        Files.Write(PathOf(id, StoreFiles.Enabled), "");
        return id;
    }

    public string Send(string id, string text)
    {
        var name = Names.NewInboxName(Clock.GetUtcNow());
        Files.Write(PathOf(id, StoreFiles.Inbox, name), text);
        return name;
    }

    public bool IsAnswered(string id, string decision) => File.Exists(PathOf(id, StoreFiles.Answers, StoreFiles.Json(decision)));

    public Answer? Answer(string id, string decision) => Files.ReadJson<Answer>(PathOf(id, StoreFiles.Answers, StoreFiles.Json(decision)));

    public void SaveAnswer(string id, string decision, Answer answer) => Files.WriteJson(PathOf(id, StoreFiles.Answers, StoreFiles.Json(decision)), answer);

    public void Ledger(IReadOnlyDictionary<string, object?> entry)
    {
        var line = new Dictionary<string, object?> { ["at"] = Now };
        foreach (var (key, value) in entry) line[key] = value;
        Files.Append(Path.Combine(Root, StoreFiles.Ledger), JsonSerializer.Serialize(line, new JsonSerializerOptions(StoreJson.Options) { WriteIndented = false }));
    }

    public IReadOnlyList<JsonElement> LedgerLines() =>
        (Files.ReadText(Path.Combine(Root, StoreFiles.Ledger)) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToList();

    public Decision? Decision(string id, string decision) => Files.ReadJson<Decision>(PathOf(id, StoreFiles.Decisions, StoreFiles.Json(decision)));

    public void SaveDecision(Decision decision) => Files.WriteJson(PathOf(decision.Session, StoreFiles.Decisions, StoreFiles.Json(decision.Id)), decision);

    public IReadOnlyList<Decision> Decisions(string id) =>
        Files.Names(PathOf(id, StoreFiles.Decisions)).Select(name => Files.ReadJson<Decision>(PathOf(id, StoreFiles.Decisions, name))).OfType<Decision>().ToList();

    public bool IsDelivered(string id, string decision) => File.Exists(PathOf(id, StoreFiles.Delivered, StoreFiles.Json(decision)));

    public IReadOnlyList<Decision> OpenDecisions(string id) =>
        Decisions(id).Where(d => !IsDelivered(id, d.Id) && !IsAnswered(id, d.Id)).ToList();

    public Report? Report(string id) => Files.ReadJson<Report>(PathOf(id, StoreFiles.Report));

    public Status? Status(string id) => Files.ReadJson<Status>(PathOf(id, StoreFiles.Status));

    public Blocked? Blocked(string id) => Files.ReadJson<Blocked>(PathOf(id, StoreFiles.Blocked));

    public void SaveBlocked(string id, string? reason) => Files.WriteJson(PathOf(id, StoreFiles.Blocked), new Blocked(reason, Now));

    public Review? Review(string id, string decision) => Files.ReadJson<Review>(PathOf(id, StoreFiles.Reviews, StoreFiles.Json(decision)));

    public void SaveReview(string id, string decision, string verdict, string text) =>
        Files.WriteJson(PathOf(id, StoreFiles.Reviews, StoreFiles.Json(decision)), new Review(verdict, text, Now));

    public Pending Pending(string id)
    {
        var cursor = (Files.ReadText(PathOf(id, StoreFiles.Cursor)) ?? "").Trim();
        var inbox = Files.Names(PathOf(id, StoreFiles.Inbox)).Where(name => cursor.Length == 0 || Names.Order.Compare(name, cursor) > 0).ToList();
        var texts = inbox.Select(name => InboxText(id, name)).ToList();
        var answers = new List<string>();
        foreach (var d in Decisions(id))
        {
            if (d.Mode != DecisionModes.Pending || IsDelivered(id, d.Id)) continue;
            if (Answer(id, d.Id) is not { } answer) continue;
            answers.Add(d.Id);
            texts.Add(Texts.AnswerTurn(d.Id, d.Question, answer.Body, answer.Note));
        }
        return new Pending(string.Join("\n\n---\n\n", texts), inbox, answers);
    }

    string InboxText(string id, string name)
    {
        var raw = Files.ReadText(PathOf(id, StoreFiles.Inbox, name)) ?? "";
        return name.EndsWith(".json") ? StoreJson.Read<Answer>(raw)?.Body ?? "" : raw;
    }

    public void MarkSent(string id, Pending pending)
    {
        if (pending.Inbox.Count > 0) Files.Write(PathOf(id, StoreFiles.Cursor), pending.Inbox[^1]);
        foreach (var decision in pending.Answers) Files.WriteJson(PathOf(id, StoreFiles.Delivered, StoreFiles.Json(decision)), new Delivered(Holders.Supervisor, Now));
    }

    public T? ReadJson<T>(string id, params string[] parts) => Files.ReadJson<T>(PathOf(id, parts));

    public void WriteJson<T>(string id, T value, params string[] parts) => Files.WriteJson(PathOf(id, parts), value);

    public void Log(string line) => Files.Append(Path.Combine(Root, StoreFiles.SupervisorLog), $"{Now} {line}");
}
