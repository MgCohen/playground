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
}

public sealed record SendText(string? Text);
public sealed record AnswerText(string? Answer);
public sealed record ReviewInput(string? Verdict, string? Text);

public sealed record SessionView(string Id, string? Title, string? Parent, string Holder, bool Live, bool Running, string? Status, Report? Report, string? Blocked, int OpenDecisions);

public static class OrchestratorApi
{
    public static SessionView View(Supervisor.Supervisor supervisor, string id, DateTimeOffset now)
    {
        var store = supervisor.Store;
        var meta = store.Meta(id);
        return new SessionView(id, meta.Title, meta.Parent, store.Lease(id).Holder, store.IsLive(id, now), supervisor.IsRunning(id),
            store.Status(id)?.State, store.Report(id), store.Blocked(id)?.Reason, store.OpenDecisions(id).Count);
    }

    public static void MapOrchestratorApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/sessions", (Supervisor.Supervisor s, FileStore store) =>
        {
            var now = store.Clock.GetUtcNow();
            return store.Sessions().Select(id => View(s, id, now));
        });

        api.MapGet("/decisions", (FileStore store) => store.Sessions().SelectMany(store.OpenDecisions));

        api.MapPost("/sessions", (NewSession input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var brief = new BriefFields
            {
                Title = input.Title, Goal = input.Goal ?? input.Brief, Scope = input.Scope, DoneWhen = input.DoneWhen, Skills = input.Skills,
                Agent = input.Agent, Cwd = input.Cwd, Worktree = input.Worktree, Parent = input.Parent,
            };
            var id = new Messages(store).NewSession(brief, Parties.Supervisor);
            s.Tick();
            return Results.Created($"/api/sessions/{id}", new { id });
        }));

        api.MapPost("/sessions/{id}/send", (string id, SendText input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            var (inbox, e) = new Messages(store).Send(id, input.Text ?? "");
            s.Tick();
            return Results.Accepted(value: new { inbox, @event = e });
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

        api.MapGet("/events",(FileStore store, string? since, string? kind, string? session, string? from, bool? open, bool? ledger) => Guard(() =>
            Results.Ok(store.Query(new EventFilter
            {
                Since = since is null ? null : Durations.Since(since, store.Clock.GetUtcNow()),
                Kinds = kind?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(),
                Session = session, From = from, Open = open ?? false, Ledger = ledger ?? false,
            }).ToList())));

        api.MapGet("/ledger", (FileStore store) => store.Ledger());

        api.MapPost("/decisions/{session}/{decision}/review", (string session, string decision, ReviewInput input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            if (!Verdict.IsValid(input.Verdict)) return Results.BadRequest(new { error = "verdict is approve, change or refuse" });
            var name = new Messages(store).Review(session, decision, input.Verdict!, input.Text ?? "");
            s.Tick();
            return Results.Accepted(value: new { inbox = name });
        }));

        api.MapPost("/decisions/{session}/{decision}", (string session, string decision, AnswerText input, Supervisor.Supervisor s, FileStore store) => Guard(() =>
        {
            new Messages(store).Answer(session, decision, input.Answer ?? "");
            s.Tick();
            return Results.Accepted(value: new { answered = decision });
        }));
    }

    static IResult Guard(Func<IResult> work)
    {
        try
        {
            return work();
        }
        catch (Exception e) when (e is MessageException or FormatException)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}
