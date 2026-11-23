using Orchestrator.Store.Remote;

namespace Orchestrator.Web;

/// <summary>
/// The store API for cloud workers and <c>orch</c> in HTTP mode (H5). It gives the repositories of the supervisor
/// through <see cref="StoreService"/>. The wire format is in <c>docs/store.md</c>, section "Store API".
/// <see cref="SignInMiddleware"/> checks the token of a remote caller first.
/// </summary>
public static class StoreApi
{
    public static void MapStoreApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(StoreWire.Prefix);

        api.MapGet("", (StoreService s) => Reply(s.Info()));
        api.MapPost("/prepare", (StoreService s) => Reply(s.Prepare()));
        api.MapPost("/log", (WireLine? line, StoreService s) => Reply(s.Log(line)));
        api.MapGet("/events/feed", (int? from, StoreService s) => Reply(s.Feed(from ?? 0)));
        api.MapGet("/{kind}/get", (string kind, string? id, HttpRequest request, StoreService s) => Reply(s.Get(Scope(kind, request), id)));
        api.MapGet("/{kind}/list", (string kind, HttpRequest request, StoreService s) => Reply(s.List(Scope(kind, request))));
        api.MapPost("/{kind}/save", (string kind, WireSave? save, HttpRequest request, StoreService s) => Reply(s.Save(Scope(kind, request), save)));
    }

    static WireScope Scope(string kind, HttpRequest request) =>
        new(kind, Key(request, "file"), Key(request, "session"), Key(request, "folder"), Key(request, "catalog"));

    static string? Key(HttpRequest request, string key) => request.Query.TryGetValue(key, out var value) ? value.ToString() : null;

    static IResult Reply(StoreReply reply) =>
        reply.Value is null ? Results.StatusCode(reply.Status) : Results.Json(reply.Value, Core.Contract.StoreJson.Line, statusCode: reply.Status);
}
