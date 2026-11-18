using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Policy;
using Orchestrator.Store;

namespace Orchestrator.Web;

public sealed record NewSession(string? Title, string? Cwd, string? Brief, string? Parent)
{
    public string? Goal { get; init; }
    public string? Scope { get; init; }
    public string? DoneWhen { get; init; }
    public List<string>? Skills { get; init; }
    public string? Agent { get; init; }
    public bool? Worktree { get; init; }
    public bool? AllowDefaults { get; init; }
    public bool? Background { get; init; }
    public string? Where { get; init; }

    /// <summary>The work stream (C1). Null: the stream of the parent.</summary>
    public string? Stream { get; init; }

    /// <summary>The session type: a custom agent (C1). Null: none.</summary>
    public string? Type { get; init; }

    /// <summary>The workflow of the session (C1). Null: none.</summary>
    public string? Workflow { get; init; }

    public BriefFields ToBrief() => new()
    {
        Title = Title, Goal = Goal ?? Brief, Scope = Scope, DoneWhen = DoneWhen, Skills = Skills,
        Agent = Agent, Cwd = Cwd, Worktree = Worktree, Parent = Parent, AllowDefaults = AllowDefaults, Background = Background, Where = Where,
        Stream = string.IsNullOrWhiteSpace(Stream) ? null : Stream,
        Type = string.IsNullOrWhiteSpace(Type) ? null : Type,
        Workflow = string.IsNullOrWhiteSpace(Workflow) ? null : Workflow,
    };
}

public sealed record SendText(string? Text);
public sealed record PostBody(string? Kind, string? From, string? To, System.Text.Json.JsonElement? Body)
{
    public string? Thread { get; init; }
    public string? Needs { get; init; }
    public string? Reply { get; init; }
    public string? Via { get; init; }
}
public sealed record NudgeBody(string? To, string? Id);
public sealed record CardText(string? Text);
public sealed record AnswerText(string? Answer);
public sealed record ReviewInput(string? Verdict, string? Text);

public sealed record SessionView(string Id, string? Title, string? Parent, string Holder, bool Live, bool Running, string? Status, Report? Report, string? Blocked, int OpenDecisions)
{
    /// <summary>The lifecycle state of state.json, or unknown.</summary>
    public string State { get; init; } = Core.Lifecycle.States.Unknown;

    /// <summary>Why an idle session waits: decision, child, blocked or none.</summary>
    public string? WaitReason { get; init; }

    /// <summary>The state of an open requested transition, for example archived while the archive waits.</summary>
    public string? Requested { get; init; }

    public string? Fault { get; init; }

    public bool Inferred { get; init; }
}

public static class OrchestratorApi
{
    public static SessionView View(Supervisor.Supervisor supervisor, string id, DateTimeOffset now)
    {
        var store = supervisor.Store;
        var meta = store.Session(id);
        var state = new StateReader(store).State(id).File;
        return new SessionView(id, meta.Title, meta.Parent, store.Lease(id).Holder, store.IsLive(id, now), supervisor.IsRunning(id),
            store.Status(id)?.State, store.Report(id), store.Blocked(id)?.Reason, store.OpenDecisions(id).Count)
        {
            State = state?.State ?? Core.Lifecycle.States.Unknown,
            WaitReason = state?.WaitReason,
            Requested = state?.Requested?.To,
            Fault = state?.Fault,
            Inferred = state?.Inferred ?? false,
        };
    }

    static bool InStream(FileStore store, string id, string? stream) =>
        string.IsNullOrEmpty(stream) || string.Equals(store.Session(id).Stream, stream, StringComparison.OrdinalIgnoreCase);

    public static void MapOrchestratorApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // A stream parameter keeps only the sessions of that work stream (the registry entry).
        api.MapGet("/sessions", (Supervisor.Supervisor s, FileStore store, string? stream) =>
        {
            var now = store.Clock.GetUtcNow();
            return store.Sessions().Where(id => InStream(store, id, stream)).Select(id => View(s, id, now));
        });

        api.MapGet("/decisions", (FileStore store, string? stream) =>
        {
            var enabled = store.Sessions().ToHashSet();
            return store.OpenDecisions().Where(d => enabled.Contains(d.Session) && InStream(store, d.Session, stream));
        });

        api.MapPost("/sessions", (NewSession input, Board board) => Guard(() =>
        {
            var id = board.NewSession(input);
            return Results.Created($"/api/sessions/{id}", new { id });
        }));

        api.MapPost("/sessions/{id}/send", (string id, SendText input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var e = new Messages(store).Send(id, input.Text ?? "");
            s.Tick();
            return Results.Accepted(value: new { @event = e.Id });
        }));

        api.MapPost("/sessions/{id}/stop", (string id, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            new Lifecycle(store).Stop(id, Parties.User);
            s.Tick();
            return Results.Accepted(value: new { stopped = id });
        }));

        api.MapPost("/sessions/{id}/resume", (string id, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            new Lifecycle(store).Resume(id, Parties.User);
            s.Tick();
            return Results.Accepted(value: new { resumed = id });
        }));

        api.MapPost("/sessions/{id}/archive", (string id, bool? force, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var lines = new Lifecycle(store).Archive(id, Parties.User, force ?? false);
            s.Tick();
            return Results.Accepted(value: new { archived = id, lines });
        }));

        // The typed query of the stream: GET /api/stream and GET /api/events take the keys of StreamQueryText.Keys.
        api.MapGet("/stream", (HttpRequest request, FileStore store) => Guard(() => Results.Ok(store.Query(Query(request, store)))));
        api.MapGet("/events", (HttpRequest request, FileStore store) => Guard(() => Results.Ok(store.Query(Query(request, store)))));

        // The named queries: the envelopes of one view. The other query keys add filters.
        foreach (var view in new[] { "inbox", "decisions", "timeline", "ledger" })
            api.MapGet($"/stream/{view}", (HttpRequest request, FileStore store) => Guard(() => Results.Ok(store.Query(Query(request, store, view)))));
        api.MapGet("/stream/threads/{thread}", (string thread, HttpRequest request, FileStore store) => Guard(() => Results.Ok(store.Query(Query(request, store, "thread", thread)))));

        api.MapGet("/events/{id}", (string id, string? @for, string? render, FileStore store) =>
            store.Events.Find(id) is { } e
                ? Results.Ok(new { envelope = e, body = store.Events.Body(e), text = @for is null && render is null ? null : store.Renderer.Text(e, render) })
                : Results.NotFound(new { error = $"no envelope {id}" }));

        api.MapPost("/stream", (PostBody input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            if (input.Kind is null || input.To is null) return Results.BadRequest(new { error = "kind and to are necessary" });
            var body = input.Body is { } b ? Core.Templates.Render.Fields(b) : [];
            var refs = new Dictionary<string, object?> { [RefKeys.Reply] = input.Reply, [RefKeys.Via] = input.Via };
            var e = store.Events.Post(new PostInput(input.Kind, input.From ?? Parties.User, input.To, body)
            {
                Session = Parties.SessionId(input.To) ?? Parties.SessionId(input.From ?? ""),
                Thread = input.Thread, Needs = input.Needs, Refs = refs,
            });
            s.Tick();
            return Results.Created($"/api/events/{e.Id}", e);
        }));

        api.MapGet("/stream/live", async (HttpContext http, FileStore store, StreamHub hub, string? after, CancellationToken cancel) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            var cursor = after ?? (store.Clock.GetUtcNow() - TimeSpan.FromSeconds(2)).ToUnixTimeMilliseconds().ToString();
            var filter = Query(http.Request, store) with { Newest = false, Limit = null, ThreadHeads = false };
            var seen = new HashSet<string>();
            while (!cancel.IsCancellationRequested)
            {
                foreach (var e in store.Query(filter with { After = cursor }))
                {
                    if (!seen.Add(e.Id)) continue;
                    await http.Response.WriteAsync($"id: {e.Id}\nevent: {e.Kind}\ndata: {System.Text.Json.JsonSerializer.Serialize(e, StoreJson.Line)}\n\n", cancel);
                }
                await http.Response.Body.FlushAsync(cancel);
                await hub.Wait(TimeSpan.FromSeconds(15), cancel);
                if (!cancel.IsCancellationRequested) await http.Response.WriteAsync(": sweep\n\n", cancel);
            }
        });

        api.MapPost("/nudge", (NudgeBody input, FileStore store) => Guard(() =>
        {
            if (input.To is null) return Results.BadRequest(new { error = "to is necessary" });
            store.Events.Nudge(input.To, input.Id);
            return Results.Accepted(value: new { nudged = input.To });
        }));

        api.MapGet("/inbox", (FileStore store) => new Inbox(store).Cards().Select(c => new { id = c.Envelope.Id, c.Kind, c.Needs, c.Session, c.Title, envelope = c.Envelope, fields = c.Fields, defaultAt = c.DefaultAt }));

        api.MapPost("/inbox/{id}/accept", (string id, CardText? input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var e = new Inbox(store).Accept(id, input?.Text);
            s.Tick();
            return Results.Accepted(value: e);
        }));

        api.MapPost("/inbox/{id}/refuse", (string id, CardText? input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var e = new Inbox(store).Refuse(id, input?.Text ?? "");
            s.Tick();
            return Results.Accepted(value: e);
        }));

        api.MapGet("/ledger", (FileStore store) => store.Ledger());

        api.MapPost("/decisions/{session}/{decision}/review", (string session, string decision, ReviewInput input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            if (!Verdict.IsValid(input.Verdict)) return Results.BadRequest(new { error = "verdict is approve, change or refuse" });
            var review = new Messages(store).Review(session, decision, input.Verdict!, input.Text ?? "");
            s.Tick();
            return Results.Accepted(value: new { @event = review.Id });
        }));

        api.MapPost("/decisions/{session}/{decision}", (string session, string decision, AnswerText input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            new Messages(store).Answer(session, decision, input.Answer ?? "");
            s.Tick();
            return Results.Accepted(value: new { answered = decision });
        }));
    }

    /// <summary>The typed query of the query string. <paramref name="view"/> names a named query; the query string adds to it.</summary>
    public static StreamQuery Query(HttpRequest request, FileStore store, string? view = null, string? argument = null) =>
        StreamQueryText.Parse(key => key == "view" && view is not null ? (true, view)
            : request.Query.TryGetValue(key, out var value) ? (true, value.ToString()) : (false, null), store.Clock.GetUtcNow(), argument);

    static IResult Guard(Func<IResult> work)
    {
        try
        {
            return work();
        }
        catch (Exception e) when (e is MessageException or RegistryException or FormatException)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}
