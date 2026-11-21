using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;
using Orchestrator.Store;

namespace Orchestrator.Tests;

[NonParallelizable]
public sealed class WebTests
{
    static (WebApplicationFactory<Program> Factory, string Store) Start(string? token = null)
    {
        var store = Directory.CreateTempSubdirectory("orch-web-").FullName;
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        Environment.SetEnvironmentVariable(Providers.ProviderFactory.Variable, Providers.Fake.FakeProvider.ProviderName);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { if (token is not null) b.UseSetting("ORCH_TOKEN", token); });
        _ = factory.Server;
        Environment.SetEnvironmentVariable("ORCH_STORE", null);
        Environment.SetEnvironmentVariable(Providers.ProviderFactory.Variable, null);
        return (factory, store);
    }

    /// <summary>U3: the Umpire composition adds its kernel and modules, and the host keeps its configuration and logging.</summary>
    [Test]
    public void Web_host_keeps_its_configuration()
    {
        var (factory, _) = Start("keep-me");
        using var owner = factory;
        var services = factory.Services;
        Assert.That(services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["ORCH_TOKEN"], Is.EqualTo("keep-me"));
        Assert.That(services.GetService<Umpire.Kernels.AppTypes>()?.Modules.Select(m => m.Name), Does.Contain("Motion"), "the motion module is composed");
        Assert.That(services.GetServices<Microsoft.Extensions.Logging.ILoggerProvider>(), Is.Not.Empty, "the log providers of ASP.NET stay");
    }

    [Test]
    public async Task The_cards_page_shows_an_open_decision_with_its_suggestions()
    {
        var (factory, root) = Start();
        using var _ = factory;
        var store = new FileStore(root);
        var id = store.Create(new Seed { Title = "Color child", Cwd = "C:/x" }, "start");
        store.SaveDecision(new Decision("toolu_9", id) { Question = "Which color goes in color.txt?", Context = "Step 3", Options = ["RED", "BLUE"], Risk = "high", Asked = Times.Format(DateTimeOffset.UtcNow) });
        var page = await factory.CreateClient().GetStringAsync("/cards");
        Assert.That(page, Does.Contain("Which color goes in color.txt?"));
        Assert.That(page, Does.Contain("BLUE"));
        Assert.That(page, Does.Contain("Color child"));
    }

    [TestCase("/")]
    [TestCase("/cards")]
    [TestCase("/stream")]
    [TestCase("/sessions")]
    [TestCase("/settings")]
    [TestCase("/timeline")]
    [TestCase("/api/sessions")]
    public async Task Each_view_answers(string path)
    {
        var (factory, _) = Start();
        using var __ = factory;
        var response = await factory.CreateClient().GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task The_sessions_page_has_the_new_session_form_and_the_speech_script()
    {
        var (factory, _) = Start();
        using var __ = factory;
        var page = await factory.CreateClient().GetStringAsync("/sessions");
        Assert.That(page, Does.Contain("id=\"new-title\""));
        Assert.That(page, Does.Contain("data-speech-for=\"new-goal\""));
        Assert.That(page, Does.Match("speech(\\.[a-z0-9]+)?\\.js"));
        Assert.That(page, Does.Not.Contain("allowDefaults").IgnoreCase);
    }

    [Test]
    public async Task With_a_token_each_request_needs_the_token()
    {
        var (factory, _) = Start("secret-1");
        using var __ = factory;
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.That((await client.GetAsync("/api/sessions")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var signIn = await client.GetAsync("/?token=secret-1");
        Assert.That(signIn.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(signIn.Headers.GetValues("Set-Cookie").Single(), Does.StartWith("orch_token="));
        var api = new HttpRequestMessage(HttpMethod.Get, "/api/sessions");
        api.Headers.Add("X-Orch-Token", "secret-1");
        Assert.That((await client.SendAsync(api)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/sessions");
        wrong.Headers.Add("X-Orch-Token", "secret-2");
        Assert.That((await factory.CreateClient().SendAsync(wrong)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
