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
    public const string Notion = "notion";
}

/// <summary>
/// The Notion store of the tests (N1 spike). It runs only with <c>NOTION_TOKEN</c> and <c>ORCH_NOTION_TEST_PAGE</c> (a page
/// that the integration can write). Each test root gets a new child page with its own databases. The teardown archives it.
/// </summary>
public static class NotionTestStore
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Pages = new();

    public static string? Token => Environment.GetEnvironmentVariable("NOTION_TOKEN") is { Length: > 0 } t ? t : null;

    public static string? Parent => Environment.GetEnvironmentVariable("ORCH_NOTION_TEST_PAGE") is { Length: > 0 } p ? p : null;

    public static void RequireOrIgnore()
    {
        if (Token is null || Parent is null) Assert.Ignore("the Notion store tests need NOTION_TOKEN and ORCH_NOTION_TEST_PAGE");
    }

    public static Orchestrator.Core.Storage.IRepositories Open(string root)
    {
        var page = Pages.GetOrAdd(root, r =>
        {
            var api = new Orchestrator.Store.Remote.Notion.NotionClient(Token!);
            var id = api.CreatePage(Orchestrator.Store.Remote.Notion.NotionTransport.PageOf("notion:" + Parent), $"test {Path.GetFileName(r)}");
            var store = new Orchestrator.Store.Remote.Notion.NotionTransport("notion:" + id, Token!);
            store.Prepare();
            return id;
        });
        return new Orchestrator.Store.Remote.RemoteRepositories(new Orchestrator.Store.Remote.Notion.NotionTransport("notion:" + page, Token!));
    }

    public static void Close(string root)
    {
        if (Pages.TryRemove(root, out var page)) new Orchestrator.Store.Remote.Notion.NotionClient(Token!).Archive(page);
    }
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
