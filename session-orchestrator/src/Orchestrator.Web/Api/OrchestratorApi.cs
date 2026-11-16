using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Web;

public sealed record NewSession(string? Title, string? Cwd, string? Brief, string? Parent);
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

        api.MapPost("/sessions", (NewSession input, Supervisor.Supervisor s, FileStore store) =>
        {
            if (string.IsNullOrEmpty(input.Brief) || string.IsNullOrEmpty(input.Cwd)) return Results.BadRequest(new { error = "brief and cwd are necessary" });
            var id = Guid.NewGuid().ToString();
            store.Events.Append("brief", Parties.User, Parties.Session(id), Data(("title", input.Title), ("cwd", input.Cwd), ("text", input.Brief)), null, id);
            store.Create(new Meta(id) { Title = input.Title, Cwd = input.Cwd, Parent = input.Parent, CreatedBy = Parties.Supervisor }, input.Brief);
            s.Tick();
            return Results.Created($"/api/sessions/{id}", new { id });
        });

        api.MapPost("/sessions/{id}/send", (string id, SendText input, Supervisor.Supervisor s, FileStore store) =>
        {
            var text = input.Text ?? "";
            var e = store.Events.Append("message", Parties.User, Parties.Session(id), Data(("text", text)), null, id);
            var name = store.Send(id, text);
            s.Tick();
            return Results.Accepted(value: new { inbox = name, @event = e.Id });
        });

        api.MapGet("/events", (FileStore store) => store.Events.All());

        api.MapGet("/ledger", (FileStore store) => store.LedgerLines());

        api.MapPost("/decisions/{session}/{decision}/review", (string session, string decision, ReviewInput input, Supervisor.Supervisor s) =>
        {
            if (input.Verdict is null || !Verdicts.All.Contains(input.Verdict)) return Results.BadRequest(new { error = "verdict is approve, change or refuse" });
            var name = s.Review(session, decision, input.Verdict, input.Text ?? "");
            s.Tick();
            return Results.Accepted(value: new { inbox = name });
        });

        api.MapPost("/decisions/{session}/{decision}", (string session, string decision, AnswerText input, Supervisor.Supervisor s, FileStore store) =>
        {
            var text = input.Answer ?? "";
            store.Events.Append("answer", Parties.User, Parties.Session(session), Data(("text", text)), Data(("decision", decision)), session);
            store.SaveAnswer(session, decision, new Answer { Value = text, By = Parties.User });
            s.Tick();
            return Results.Accepted(value: new { answered = decision });
        });
    }

    static Dictionary<string, object?> Data(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}
