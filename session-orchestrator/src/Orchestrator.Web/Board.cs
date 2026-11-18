using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Stack;
using Orchestrator.Core.Templates;
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
    readonly Inbox inbox = new(store);
    readonly Lifecycle lifecycle = new(store);

    public string Title(string session) => store.Meta(session).Title ?? session[..Math.Min(8, session.Length)];

    /// <summary>The named query <see cref="StreamQueries.OpenDecisions"/>, for the enabled sessions.</summary>
    public IReadOnlyList<OpenDecision> OpenDecisions()
    {
        var enabled = store.Sessions().ToHashSet();
        return store.OpenDecisions().Where(d => enabled.Contains(d.Session)).Select(d => new OpenDecision(d.Session, Title(d.Session), d)).ToList();
    }

    /// <summary>The cards of the user inbox: a query of the stream.</summary>
    public IReadOnlyList<Card> Inbox() => inbox.Cards();

    /// <summary>The inbox cards of the answers by the docs or the policy, which need a review.</summary>
    public IReadOnlyList<DefaultToReview> DefaultsToReview()
    {
        var list = new List<DefaultToReview>();
        foreach (var card in inbox.Cards().Where(c => c.Kind == EventKinds.Answer && c.Needs == Needs.Review))
        {
            var thread = card.Envelope.Thread ?? "";
            if (store.Decision(thread) is not { } decision || store.Answer(decision.Session, thread) is not { } answer) continue;
            var version = store.Stack(decision.Session, decision.Id)?.Current;
            if (version is { State: VersionStates.Closed }) continue;
            list.Add(new DefaultToReview(decision.Session, Title(decision.Session), decision, answer, version));
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
            Update(meta.Id),
            store.Blocked(meta.Id)?.Reason,
            store.OpenDecisions(meta.Id).Count,
            metas.Where(m => m.Parent == meta.Id).OrderBy(m => m.CreatedAt, StringComparer.Ordinal).Select(Node).ToList());
        return metas.Where(m => m.Parent is null || !known.Contains(m.Parent)).OrderByDescending(m => m.CreatedAt, StringComparer.Ordinal).Select(Node).ToList();
    }

    UpdateView? Update(string id)
    {
        if (store.LastPost(id, EventKinds.Update) is not { } e) return null;
        var f = store.Events.Fields(e);
        return new UpdateView(f.GetValueOrDefault("step") as string, f.GetValueOrDefault("progress") as double?, f.GetValueOrDefault("summary") as string ?? "", e.At);
    }

    /// <summary>The named query <see cref="StreamQueries.Timeline"/>: the newest envelopes first.</summary>
    public IReadOnlyList<Event> Timeline(StreamQuery filter) => store.Query(StreamQueries.Timeline(filter, TimelineLimit));

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

    // ===== Inbox UI (A4) =====

    public DateTimeOffset Now => store.Clock.GetUtcNow();

    /// <summary>The cards of the pile, in arrival order, with the actions of their type.</summary>
    public IReadOnlyList<CardView> Cards() => inbox.Cards().Select(c => Ui.Classify(c) with { Preview = Preview(c.Kind, c.Fields, c.Envelope.Subject) })
        .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

    // ===== Render model (R2): templates/<kind>/view.json =====

    /// <summary>The full document of an envelope, from the view.json of its kind.</summary>
    public DocumentModel Document(Event e) => Document(e.Kind, Fields(e), e.Subject);

    /// <summary>The preview of an envelope (the card and the stream row), from the view.json of its kind.</summary>
    public PreviewModel Preview(Event e) => Preview(e.Kind, Fields(e), e.Subject);

    public DocumentModel Document(string kind, IReadOnlyDictionary<string, object?> fields, string subject) =>
        HasView(kind) ? Views.Document(store.Templates, kind, fields)
        : new DocumentModel(kind, Ui.KindLabel(kind, fields), Ui.Look(kind).Icon, Ui.Look(kind).Color, subject, null, [],
            // A kind with no template (for example blocked or pr.open) has no view.json: show each field as text.
            fields.Where(f => Ui.List(f.Value).Count > 0).Select(f => new Block(f.Key, char.ToUpperInvariant(f.Key[0]) + f.Key[1..], f.Value is string ? Looks.Text : Looks.List, Ui.Text(f.Value), Ui.List(f.Value), null)).ToList());

    public PreviewModel Preview(string kind, IReadOnlyDictionary<string, object?> fields, string subject) =>
        HasView(kind) ? Views.Preview(store.Templates, kind, fields)
        : new PreviewModel(kind, Ui.KindLabel(kind, fields), Ui.Look(kind).Icon, Ui.Look(kind).Color, subject, null, null, [], null, [], null, null);

    /// <summary>The icon and the color token of the ring of a kind: from its view.json, or the fixed look of a kind with no template.</summary>
    public (string Icon, string Color) Look(string kind, string? state)
    {
        if (!HasView(kind)) return Ui.Look(kind, state);
        var (tone, icon) = Views.Look(store.Templates, kind, new Dictionary<string, object?> { ["state"] = state });
        return (icon, tone);
    }

    bool HasView(string kind) => store.Templates.Has(kind) && store.Templates[kind].View is not null;

    /// <summary>Accept a card: the default, a fixed text (yes), an approve, or an ack.</summary>
    public void Accept(string id, string? text = null)
    {
        inbox.Accept(id, text);
        supervisor.Tick();
    }

    /// <summary>Refuse a card with a text: an answer, a refuse, or a message and an ack.</summary>
    public void Refuse(string id, string text)
    {
        inbox.Refuse(id, text);
        supervisor.Tick();
    }

    /// <summary>The text of the answer sheet. A default to review gets a change; the other cards get the text as a refuse.</summary>
    public void SendSheet(CardView card, string text)
    {
        if (card.Type == "review" && card.Card.Envelope.Thread is { } thread)
        {
            supervisor.Review(card.Session, thread, Verdicts.Change, text);
            supervisor.Tick();
            return;
        }
        Refuse(card.Id, text);
    }

    public Event? Find(string id) => store.Events.Find(id);

    public IReadOnlyDictionary<string, object?> Fields(Event e) => store.Events.Fields(e);

    public bool IsOpen(Event e) => EventQuery.IsOpen(store, e);

    public bool IsAcked(Event e) => store.Events.IsAcked(e.Id);

    /// <summary>The envelopes after <paramref name="e"/> that answer, review or acknowledge it.</summary>
    public IReadOnlyList<Event> Results(Event e)
    {
        var thread = e.Thread is { } t ? store.Events.Thread(t) : [];
        return thread.Where(x => string.CompareOrdinal(x.Id, e.Id) > 0 && !x.IsForward && x.Kind is EventKinds.Answer or EventKinds.Review)
            .Concat(store.Events.RepliesTo(e.Id)).Concat(store.Events.AcksOf(e.Id))
            .DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The stream, newest first. With sessions: only the envelopes of these sessions.</summary>
    public IReadOnlyList<Event> Stream(StreamQuery filter, IReadOnlyCollection<string>? sessions = null)
    {
        var events = store.Query(StreamQueries.Timeline(filter, sessions is null ? TimelineLimit : TimelineLimit * 4));
        if (sessions is null) return events;
        var matchers = sessions.Select(s => new StreamQuery { Session = s }).ToList();
        return events.Where(e => matchers.Any(m => m.Matches(e))).Take(TimelineLimit).ToList();
    }

    /// <summary>The number of envelopes in each of the last <paramref name="hours"/> hours, oldest first.</summary>
    public int[] Activity(int hours = 24)
    {
        var now = Now;
        var counts = new int[hours];
        foreach (var e in store.Query(new StreamQuery { Since = now.AddHours(-hours) }))
        {
            if (Ui.Time(e) is not { } at) continue;
            var slot = hours - 1 - (int)Math.Floor((now - at).TotalHours);
            if (slot >= 0 && slot < hours) counts[slot]++;
        }
        return counts;
    }

    public int EventCount(string session) => store.Query(new StreamQuery { Session = session }).Count;

    /// <summary>The links in the reports and the briefs of a session, newest first, each one time.</summary>
    public IReadOnlyList<string> Links(string session) =>
        store.Query(new StreamQuery { Session = session, Kinds = new HashSet<string> { EventKinds.Report, EventKinds.Brief }, Forwards = false, Newest = true })
            .SelectMany(e => Ui.List(store.Events.Fields(e).GetValueOrDefault("links"))).Distinct().ToList();

    /// <summary>The time of the newest envelope of a session.</summary>
    public DateTimeOffset? LastActive(string session) =>
        store.Query(new StreamQuery { Session = session, Newest = true, Limit = 1 }).FirstOrDefault() is { } e ? Ui.Time(e) : null;

    public string StoreRoot => store.Root;
}
