using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Storage;
using Orchestrator.Store.Local;
using Orchestrator.Store.Remote;
using Orchestrator.Web;

namespace Orchestrator.Tests;

public static class Backends
{
    public const string Local = "local";
    public const string Http = "http";
}

/// <summary>
/// A local HTTP store API for the tests (H5): a test server with only the store endpoints on a local folder store.
/// Each root gets one server. Each <see cref="Open"/> is a new client, as a second <c>orch</c> process.
/// </summary>
public static class TestStoreApi
{
    static readonly ConcurrentDictionary<string, WebApplication> servers = new();

    public static IRepositories Open(string root, string? token = null) => new HttpRepositories(Server(root).GetTestClient(), token);

    public static WebApplication Server(string root) => servers.GetOrAdd(Path.GetFullPath(root), folder =>
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new StoreService(new LocalRepositories(folder)));
        var app = builder.Build();
        app.MapStoreApi();
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    });
}
