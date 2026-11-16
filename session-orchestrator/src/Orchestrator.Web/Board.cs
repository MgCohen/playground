using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Store;

namespace Orchestrator.Web;

public sealed record OpenDecision(string Session, string Title, Decision Decision);

public sealed record DefaultToReview(string Session, string Title, Decision Decision, Answer Answer, StackVersion? Version);

public sealed record UpdateView(string? Step, double? Progress, string Summary, string At);

public sealed record ParentOption(string Id, string Name, string? Cwd);

public sealed record SessionNode(Meta Meta, string Holder, bool Live, bool Running, string State, string Control, Report? Report, UpdateView? Update, string? Blocked, int Open, IReadOnlyList<SessionNode> Children);

public sealed class Board(FileStore store, Supervisor.Supervisor supervisor)
{
    public const int TimelineLimit = 300;

    readonly Messages messages = new(store);
    readonly Lifecycle lifecycle = new(store);

    public string Title(string session) => store.Meta(session).Title ?? session[..Math.Min(8, session.Length)];

    public IReadOnlyList<OpenDecision> OpenDecisions() =>
        store.Sessions().SelectMany(s => store.OpenDecisions(s).Select(d => new OpenDecision(s, Title(s), d)))
            .OrderBy(d => d.Decision.Asked, StringComparer.Ordinal).ToList();

    public IReadOnlyList<DefaultToReview> DefaultsToReview()
    {
        var list = new List<DefaultToReview>();
        foreach (var session in store.AllSessions())
            foreach (var decision in store.Decisions(session))
            {
                if (store.Answer(session, decision.Id) is not { By: Parties.Policy or Parties.Docs } answer) continue;
                var version = store.Stack(session, decision.Id)?.Current;
                var versionId = StackNames.VersionId(decision.Id, version?.Version ?? 1);
                if (store.Review(session, versionId) is not null) continue;
                if (version is { State: VersionStates.Closed }) continue;
                list.Add(new DefaultToReview(session, Title(session), decision, answer, version));
            }
        return list.OrderBy(d => d.Decision.Asked, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<SessionNode> Tree()
    {
        var now = store.Clock.GetUtcNow();
        var ids = store.AllSessions();
        var metas = ids.Select(store.Meta).ToList();
        var known = ids.ToHashSet();
        SessionNode Node(Meta meta) => new(
            meta,
            store.Lease(meta.Id).Holder,
            store.IsLive(meta.Id, now),
            supervisor.IsRunning(meta.Id),
            store.Status(meta.Id)?.State ?? "new",
            lifecycle.Control(meta.Id).State,
            store.Report(meta.Id),
            store.ReadJson<UpdateView>(meta.Id, StoreFiles.Update),
            store.Blocked(meta.Id)?.Reason,
            store.OpenDecisions(meta.Id).Count,
            metas.Where(m => m.Parent == meta.Id).OrderBy(m => m.CreatedAt, StringComparer.Ordinal).Select(Node).ToList());
        return metas.Where(m => m.Parent is null || !known.Contains(m.Parent)).OrderByDescending(m => m.CreatedAt, StringComparer.Ordinal).Select(Node).ToList();
    }

    public IReadOnlyList<Event> Timeline(EventFilter filter) => store.Query(filter).Reverse().Take(TimelineLimit).ToList();

    public string? BodyText(Event e) => store.Events.Body(e) is { } body ? JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) : null;

    public string Party(string party) =>
        party.StartsWith("session:") ? Title(party["session:".Length..]) : party;

    public void Answer(string session, string decision, string text)
    {
        messages.Answer(session, decision, text);
        supervisor.Tick();
    }

    public void Review(string session, string decision, string verdict, string text)
    {
        supervisor.Review(session, decision, verdict, text);
        supervisor.Tick();
    }

    public string NewSession(NewSession input)
    {
        var id = messages.NewSession(input.ToBrief(), Parties.Supervisor);
        supervisor.Tick();
        return id;
    }

    public void Send(string session, string text)
    {
        messages.Send(session, text);
        supervisor.Tick();
    }

    public void Stop(string session) => lifecycle.Stop(session, Parties.User);

    public void Resume(string session)
    {
        lifecycle.Resume(session, Parties.User);
        supervisor.Tick();
    }

    public IReadOnlyList<string> Archive(string session, bool force) => lifecycle.Archive(session, Parties.User, force);
}
