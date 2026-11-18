using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;
using Orchestrator.Store;
using Orchestrator.Store.Local;
using Orchestrator.Store.Remote;

namespace Orchestrator.Tests;

/// <summary>The store API for cloud workers, its token check, and <c>orch</c> in HTTP mode (H5).</summary>
[NonParallelizable]
public sealed class StoreApiTests
{
    const string Token = "test-token-1";

    static string NewFolder() => Directory.CreateTempSubdirectory("orch-api-").FullName;

    /// <summary>The supervisor on a test server. <paramref name="loopback"/>: each request comes from 127.0.0.1, as a local tool.</summary>
    static WebApplicationFactory<Program> Start(string store, string? token, bool loopback = false, int? kestrelPort = null)
    {
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        Environment.SetEnvironmentVariable("ORCH_IMPORT", "0");
        if (kestrelPort is { } listen) Environment.SetEnvironmentVariable("ORCH_PORT", listen.ToString());
        try
        {
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                if (token is not null) b.UseSetting("ORCH_TOKEN", token);
                if (loopback) b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new Loopback()));
            });
            if (kestrelPort is not null)
            {
                factory.UseKestrel(_ => { });
                factory.StartServer();
            }
            else _ = factory.Server;
            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
            Environment.SetEnvironmentVariable("ORCH_IMPORT", null);
            Environment.SetEnvironmentVariable("ORCH_PORT", null);
        }
    }

    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, call) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return call(context);
            });
            next(app);
        };
    }

    static HttpRequestMessage Get(string path, params (string Key, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (key, value) in headers) request.Headers.TryAddWithoutValidation(key, value);
        return request;
    }

    [Test]
    public async Task With_no_token_a_remote_caller_gets_the_api_as_before_but_never_the_store_api()
    {
        using var factory = Start(NewFolder(), token: null);
        var client = factory.CreateClient();
        Assert.That((await client.GetAsync("/api/sessions")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/store")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task A_remote_caller_needs_the_token_in_the_header_or_as_a_bearer_token()
    {
        using var factory = Start(NewFolder(), Token);
        var client = factory.CreateClient();
        Assert.That((await client.SendAsync(Get("/api/store"))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.SendAsync(Get("/api/store", ("X-Orch-Token", "wrong")))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.SendAsync(Get("/api/store", ("X-Orch-Token", Token)))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.SendAsync(Get("/api/store", ("Authorization", $"Bearer {Token}")))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(new HttpRepositories(factory.CreateClient(), Token).Exists(), Is.True);
        Assert.That(() => new HttpRepositories(factory.CreateClient(), "wrong").Exists(), Throws.InstanceOf<IOException>().With.Message.Contains("ORCH_TOKEN"));
    }

    [Test]
    public async Task A_local_caller_needs_no_token_but_a_tunnel_request_from_127_0_0_1_does()
    {
        using var factory = Start(NewFolder(), Token, loopback: true);
        var client = factory.CreateClient();
        Assert.That((await client.SendAsync(Get("/api/store"))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.SendAsync(Get("/api/sessions"))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.SendAsync(Get("/api/store", ("Cf-Connecting-Ip", "203.0.113.9")))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.SendAsync(Get("/api/store", ("X-Forwarded-For", "203.0.113.9")))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("events", null, "../version.json")]
    [TestCase("bodies", null, "bodies/../../x.json")]
    [TestCase("claims", null, "answer/..\\x")]
    [TestCase("claims", null, "C:/x")]
    [TestCase("session-file", "../../policy.json", "s1")]
    [TestCase("session-file", "", "s1")]
    [TestCase("definitions", null, "def-1")]
    [TestCase("nothing", null, "x")]
    public void The_store_api_refuses_an_id_or_a_scope_outside_the_store(string kind, string? file, string id)
    {
        var service = new StoreService(new LocalRepositories(NewFolder()));
        Assert.That(service.Get(new WireScope(kind, File: file), id).Status, Is.EqualTo(400));
        Assert.That(service.Save(new WireScope(kind, File: file), new WireSave([new WireDoc(id, 0, "{}")], [], [])).Status, Is.EqualTo(400));
    }

    [Test]
    public void The_store_api_refuses_a_session_or_folder_with_a_path()
    {
        var service = new StoreService(new LocalRepositories(NewFolder()));
        Assert.That(service.List(new WireScope(StoreWire.SessionItems, Session: "..", Folder: "stack")).Status, Is.EqualTo(400));
        Assert.That(service.List(new WireScope(StoreWire.SessionItems, Session: "s1", Folder: "a/b")).Status, Is.EqualTo(400));
        Assert.That(service.List(new WireScope(StoreWire.SessionItems, Session: "s1", Folder: "stack")).Status, Is.EqualTo(200));
    }

    [Test]
    public void A_stale_change_through_the_api_writes_nothing()
    {
        var root = NewFolder();
        var a = TestStoreApi.Open(root);
        var b = TestStoreApi.Open(root);
        a.Claims.Put("answer/t1", "first");
        var read = b.Claims.Get("answer/t1")!;
        a.Claims.Put("answer/t1", "second");
        b.Claims.Add(new Core.Storage.Doc<string>("answer/t2", "new"));
        read.Value = "late";
        Assert.That(() => b.Claims.Save(), Throws.InstanceOf<Core.Storage.StoreConflictException>());
        Assert.That(a.Claims.Read("answer/t1"), Is.EqualTo("second"));
        Assert.That(a.Claims.Exists("answer/t2"), Is.False, "a refused save writes nothing");
    }

    [Test]
    public void The_feed_gives_only_new_envelopes_and_reads_all_again_after_a_new_epoch()
    {
        var root = NewFolder();
        var writer = new FileStore(root);
        writer.Events.Post(new PostInput(EventKinds.Message, Parties.User, Parties.Session("s1"), new Dictionary<string, object?> { ["text"] = "one" }) { Session = "s1" });
        var remote = new FileStore(TestStoreApi.Open(root));
        Assert.That(remote.Events.All().Count(), Is.EqualTo(1));
        writer.Events.Post(new PostInput(EventKinds.Message, Parties.User, Parties.Session("s1"), new Dictionary<string, object?> { ["text"] = "two" }) { Session = "s1" });
        Assert.That(remote.Events.All().Select(e => remote.Events.Fields(e)["text"]), Is.EquivalentTo(new[] { "one", "two" }));
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public void Orch_works_through_the_store_api_when_ORCH_URL_is_set()
    {
        var store = NewFolder();
        var other = NewFolder();
        var port = FreePort();
        using var factory = Start(store, Token, kestrelPort: port);
        Environment.SetEnvironmentVariable("ORCH_URL", $"http://127.0.0.1:{port}");
        Environment.SetEnvironmentVariable("ORCH_TOKEN", Token);
        Environment.SetEnvironmentVariable("ORCH_STORE", other);
        try
        {
            var output = new StringWriter();
            Assert.That(Cli.Cli.Run(["new", "--title", "Cloud child", "--goal", "Write a.txt", "--cwd", store], output, output, store), Is.EqualTo(0), output.ToString());
            var local = new FileStore(store);
            var id = local.Sessions().Single();
            Assert.That(local.Meta(id).Title, Is.EqualTo("Cloud child"));
            output = new StringWriter();
            Assert.That(Cli.Cli.Run(["post", "report", "--from", $"session:{id}", "--to", "user", "--state", "done", "--summary", "a.txt is written"], output, output, store), Is.EqualTo(0), output.ToString());
            Assert.That(new FileStore(store).Report(id)?.Summary, Is.EqualTo("a.txt is written"));
            output = new StringWriter();
            Assert.That(Cli.Cli.Run(["events", "--kind", "report"], output, output, store), Is.EqualTo(0), output.ToString());
            Assert.That(output.ToString(), Does.Contain("a.txt is written"));
            output = new StringWriter();
            Assert.That(Cli.Cli.Run(["status", id[..8]], output, output, store), Is.EqualTo(0), output.ToString());
            Assert.That(Directory.EnumerateFileSystemEntries(other), Is.Empty, "HTTP mode writes nothing to ORCH_STORE");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_URL", null);
            Environment.SetEnvironmentVariable("ORCH_TOKEN", null);
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }

    [Test]
    public void Orch_reports_a_missing_supervisor_in_HTTP_mode()
    {
        Environment.SetEnvironmentVariable("ORCH_URL", $"http://127.0.0.1:{FreePort()}");
        try
        {
            var error = new StringWriter();
            Assert.That(Cli.Cli.Run(["tree"], TextWriter.Null, error, NewFolder()), Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("no store API answers"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_URL", null);
        }
    }

    [Test]
    public void The_cli_store_is_the_url_then_the_folder()
    {
        Environment.SetEnvironmentVariable("ORCH_URL", "https://orch.example.com/");
        try
        {
            Assert.That(Home.CliStoreRoot(), Is.EqualTo("https://orch.example.com"));
            Assert.That(Home.CliStoreRoot(local: true), Is.Not.StartWith("http"));
            Assert.That(StoreBackends.Create(Home.CliStoreRoot()), Is.InstanceOf<HttpRepositories>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_URL", null);
        }
        var folder = NewFolder();
        Environment.SetEnvironmentVariable("ORCH_STORE", folder);
        try
        {
            Assert.That(Home.CliStoreRoot(), Is.EqualTo(Path.GetFullPath(folder)));
            Assert.That(StoreBackends.Create(Home.CliStoreRoot()), Is.InstanceOf<LocalRepositories>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }
}
