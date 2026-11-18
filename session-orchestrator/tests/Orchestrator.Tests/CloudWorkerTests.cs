using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Providers.ClaudeCode;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

/// <summary>A provider with no engine: it records each turn. A new cloud session gets the id <see cref="CloudId"/>.</summary>
public sealed class FakeProvider(bool giveId = true) : IProvider
{
    public const string CloudId = "cloud-session-1";

    public List<TurnRequest> Turns { get; } = [];

    public string Name => "Fake";

    public Task<TurnResult> Turn(TurnRequest request, CancellationToken cancel)
    {
        lock (Turns) Turns.Add(request);
        var appId = request.Where == Places.Cloud ? request.AppId ?? (giveId ? CloudId : null) : null;
        return Task.FromResult(new TurnResult(true, "done", null, 0) { AppId = appId });
    }

    public Task<AskResult> Ask(AskRequest request, CancellationToken cancel) => Task.FromResult(new AskResult(false, null, "no engine"));

    public IReadOnlyList<string> OpenCommand(string sessionId, string? cwd = null) => ["fake-open", sessionId];

    public string AppId(string sessionId) => $"fake_{sessionId}";

    public AppFacts Read(string appId) => new(false, false);

    public string? CallerSessionId() => null;

    public IReadOnlyList<(string Key, object? Value)> AppTools { get; } = [("remoteControlTool", "fake_rc"), ("archiveTool", "fake_archive")];

    public string PluginTools => "fake__";

    public IReadOnlyList<DoctorCheck> Doctor(CommandRun run) => [];
}

/// <summary>Cloud children (G2): the supervisor starts and delivers to a cloud session only through <see cref="IProvider"/>.</summary>
public sealed class CloudWorkerTests
{
    static (Rig Rig, FakeProvider Provider, Supervisor.Supervisor Supervisor) Setup(bool giveId = true)
    {
        var rig = new Rig();
        var provider = new FakeProvider(giveId);
        var supervisor = new Supervisor.Supervisor(rig.Store, new ProviderTurns(provider), rig.Docs,
            new SupervisorOptions(2, 2, 2, Rig.TestPolicy) { Provider = provider, Import = id => throw new InvalidOperationException($"no app import for {id}") });
        return (rig, provider, supervisor);
    }

    static async Task Cycle(Supervisor.Supervisor supervisor)
    {
        supervisor.Tick();
        await supervisor.Idle();
    }

    static string NewCloudChild(Rig rig, bool worktree = false) =>
        new Messages(rig.Store).NewSession(new BriefFields { Title = "Cloud child", Goal = "Write a.txt and report done.", Cwd = rig.Folder, Worktree = worktree, Where = Places.Cloud }, "spawn_child");

    [Test]
    public async Task A_cloud_child_starts_through_the_provider_with_the_cloud_notice_and_keeps_its_id()
    {
        var (rig, provider, supervisor) = Setup();
        using var _ = rig;
        var id = NewCloudChild(rig, worktree: true);
        Assert.That(rig.Store.Meta(id).Where, Is.EqualTo(Places.Cloud));
        await Cycle(supervisor);
        var start = provider.Turns.Single();
        Assert.That(start.Where, Is.EqualTo(Places.Cloud));
        Assert.That(start.IsNew, Is.True);
        Assert.That(start.AppId, Is.Null);
        Assert.That(start.Cwd, Is.EqualTo(rig.Folder), "a cloud session gets no local worktree");
        Assert.That(start.Prompt, Does.Contain("Write a.txt and report done."));
        Assert.That(start.Prompt, Does.Contain("This session runs in the cloud"));
        Assert.That(start.Prompt, Does.Contain($"session:{id}"));
        Assert.That(rig.Store.Meta(id).AppId, Is.EqualTo(FakeProvider.CloudId));
        Assert.That(rig.Store.Pending(id).IsEmpty, Is.True, "the brief and the notice have an ack");

        rig.Store.Events.Post(new PostInput(EventKinds.Message, Parties.User, Parties.Session(id), new Dictionary<string, object?> { ["text"] = "Also write b.txt." }) { Session = id });
        await Cycle(supervisor);
        var next = provider.Turns[1];
        Assert.That(next.IsNew, Is.False);
        Assert.That(next.Where, Is.EqualTo(Places.Cloud));
        Assert.That(next.AppId, Is.EqualTo(FakeProvider.CloudId));
        Assert.That(next.Prompt, Does.Contain("Also write b.txt."));
        Assert.That(next.Prompt, Does.Not.Contain("This session runs in the cloud"));
    }

    [Test]
    public async Task A_cloud_start_with_no_session_id_fails_and_starts_again_later()
    {
        var (rig, provider, supervisor) = Setup(giveId: false);
        using var _ = rig;
        var id = NewCloudChild(rig);
        await Cycle(supervisor);
        Assert.That(provider.Turns, Has.Count.EqualTo(1));
        Assert.That(rig.Store.Meta(id).AppId, Is.Null);
        Assert.That(rig.Store.Blocked(id)?.Reason, Does.Contain("no session id"));
        Assert.That(rig.Store.Pending(id).IsEmpty, Is.False, "the brief stays for the next start");
        await Cycle(supervisor);
        Assert.That(provider.Turns[1].IsNew, Is.True);
        Assert.That(provider.Turns[1].Prompt.Split("This session runs in the cloud").Length, Is.EqualTo(2), "one cloud notice");
    }

    [Test]
    public async Task A_local_child_has_no_place_and_no_cloud_notice()
    {
        var (rig, provider, supervisor) = Setup();
        using var _ = rig;
        new Messages(rig.Store).NewSession(new BriefFields { Title = "Local child", Goal = "Work here.", Cwd = rig.Folder }, "user");
        await Cycle(supervisor);
        Assert.That(provider.Turns.Single().Where, Is.Null);
        Assert.That(provider.Turns.Single().Prompt, Does.Not.Contain("This session runs in the cloud"));
    }

    [Test]
    public void The_brief_refuses_an_unknown_place()
    {
        using var rig = new Rig();
        Assert.That(() => new Messages(rig.Store).NewSession(new BriefFields { Title = "T", Goal = "G", Cwd = rig.Folder, Where = "moon" }, "user"), Throws.InstanceOf<MessageException>());
    }

    [Test]
    public void The_cloud_arguments_start_a_session_or_send_to_it()
    {
        var start = ClaudeCodeProvider.CloudArguments(new TurnRequest("s1", "C:/x", "the \"brief\"", true) { Where = Places.Cloud, Title = "Cloud child" });
        Assert.That(start, Is.EqualTo(new[] { "--cloud", "the \"brief\"" }), "a start is not print mode: print mode refuses --cloud");
        var next = ClaudeCodeProvider.CloudArguments(new TurnRequest("s1", "C:/x", "more", false) { Where = Places.Cloud, AppId = "session_01abcdefgh" });
        Assert.That(next, Is.EqualTo(new[] { "-p", "more", "--cloud", "session_01abcdefgh" }));
        Assert.That(next, Has.None.EqualTo("--resume").And.None.EqualTo("--session-id"));
    }

    [Test]
    public void The_cloud_session_id_comes_from_the_output_of_the_start()
    {
        var output = string.Join('\n', "Created cloud session session_01Y7UMGXQGcsekzz7bDemKjq", "View: https://claude.ai/code/session_01Y7UMGXQGcsekzz7bDemKjq");
        Assert.That(ClaudeCodeProvider.CloudSessionId(output), Is.EqualTo("session_01Y7UMGXQGcsekzz7bDemKjq"));
        Assert.That(ClaudeCodeProvider.CloudSessionId("no id here, only session_x"), Is.Null);
    }

    [Test]
    public void The_cloud_environment_file_names_each_variable_and_holds_no_secret()
    {
        var folder = Path.Combine(Home.Root(), "src", "Orchestrator.Providers", "ClaudeCode", "Cloud");
        var values = File.ReadAllLines(Path.Combine(folder, "environment.env"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);
        Assert.That(values.Keys, Is.SupersetOf(new[] { "ORCH_URL", "ORCH_TOKEN", "ORCH_IMPORT", "DOTNET_ROOT" }));
        Assert.That(values["ORCH_TOKEN"], Is.Empty, "the token is a secret: it never goes in the repository");
        var setup = File.ReadAllText(Path.Combine(folder, "setup.sh"));
        Assert.That(setup, Does.Contain("set -euo pipefail"));
        Assert.That(setup, Does.Contain("dotnet tool update -g Orchestrator.Cli"));
        Assert.That(setup, Does.Contain("\"defaultMode\""));
        Assert.That(setup, Does.Not.Contain("ORCH_TOKEN="));
    }
}
