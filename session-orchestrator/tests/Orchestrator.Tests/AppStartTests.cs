using Orchestrator.Cli;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Providers.ClaudeCode;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// R-F8: a parent in the desktop app starts a child that is not a background child with the app tool, so the child
/// shows in the app with Remote Control from the start. <c>orch new --app-start</c> writes the start file and gives the
/// app call; <c>orch join</c> makes the entry with the id of the child. The plugin side: spawn-child.test.ts.
/// </summary>
[NonParallelizable]
public sealed class AppStartTests
{
    const string Child = "cccc3333-0000-0000-0000-000000000000";

    static (int Exit, string Output) Run(Rig rig, bool inApp, params string[] args)
    {
        var output = new StringWriter();
        Environment.SetEnvironmentVariable("ORCH_STORE", rig.Folder);
        Environment.SetEnvironmentVariable("ORCH_START_DIR", Path.Combine(rig.Folder, "starts"));
        Environment.SetEnvironmentVariable(ClaudeCodeProvider.EntrypointVariable, inApp ? ClaudeCodeProvider.DesktopEntrypoint : null);
        try
        {
            return (Cli.Cli.Run(args, output, output, rig.Folder), output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
            Environment.SetEnvironmentVariable("ORCH_START_DIR", null);
            Environment.SetEnvironmentVariable(ClaudeCodeProvider.EntrypointVariable, null);
        }
    }

    static string Parent(Rig rig) => new Messages(rig.Store).NewSession(new BriefFields { Title = "parent", Goal = "g", Cwd = rig.Folder.Replace('\\', '/') }, "test");

    static string StartFile(string output) => output.Split('\n')[0].Trim()[(SessionCommands.AppStartMark.Length + 1)..];

    [Test]
    public void A_caller_in_the_app_gets_a_start_file_and_the_app_call_and_no_entry()
    {
        using var rig = new Rig();
        var parent = Parent(rig);
        var before = rig.Store.AllSessions().Count;
        var (exit, output) = Run(rig, true, "new", "--title", "Child A", "--goal", "Do A.", "--parent", parent, "--worktree", "--app-start");
        Assert.That(exit, Is.EqualTo(0), output);
        Assert.That(output, Does.StartWith(SessionCommands.AppStartMark + " "));
        Assert.That(output, Does.Contain(ClaudeCodeProvider.StartTool).And.Contain("\"permission_mode\":\"auto\"").And.Contain("\"use_worktree\":true").And.Contain("orch join"));
        Assert.That(File.Exists(StartFile(output)), Is.True);
        Assert.That(rig.Store.AllSessions().Count, Is.EqualTo(before), "the entry waits for the id of the child");
    }

    [TestCase(false, null)]
    [TestCase(true, "--background")]
    [TestCase(true, "--where")]
    public void The_supervisor_starts_the_child_outside_the_app_and_for_a_background_or_cloud_child(bool inApp, string? flag)
    {
        using var rig = new Rig();
        var parent = Parent(rig);
        string[] args = ["new", "--title", "Child B", "--goal", "Do B.", "--parent", parent, "--app-start"];
        if (flag == "--background") args = [.. args, "--background"];
        if (flag == "--where") args = [.. args, "--where", "cloud"];
        var (exit, output) = Run(rig, inApp, args);
        Assert.That(exit, Is.EqualTo(0), output);
        Assert.That(output, Does.Not.Contain(SessionCommands.AppStartMark));
        var id = output.Trim().Split('\n').Last().Trim();
        Assert.That(rig.Store.Session(id).Title, Is.EqualTo("Child B"));
    }

    [Test]
    public void Join_makes_the_entry_with_the_id_of_the_child_and_the_brief_and_removes_the_start_file()
    {
        using var rig = new Rig();
        var parent = Parent(rig);
        var (_, output) = Run(rig, true, "new", "--title", "Child C", "--goal", "Do C.", "--parent", parent, "--done-when", "C is done", "--worktree", "--app-start");
        var file = StartFile(output);
        var (exit, joined) = Run(rig, true, "join", file, "--id", Child);
        Assert.That(exit, Is.EqualTo(0), joined);
        Assert.That(joined, Does.Contain($"Joined as {Child} (Child C), child of {parent[..8]}"));
        var meta = rig.Store.Session(Child);
        Assert.That((meta.Title, meta.Parent, meta.Lifecycle.Started, meta.Engine.Imported, meta.Engine.AppId),
            Is.EqualTo(("Child C", parent, (bool?)true, (bool?)true, $"local_{Child}")));
        Assert.That((meta.Work.Worktree, meta.Work.Repo, meta.Work.Branch), Is.EqualTo(((bool?)null, (string?)null, (string?)null)), "the worktree of the app is the app's");
        Assert.That(rig.Store.IsEnabled(Child), Is.True);
        Assert.That(meta.IsNew(), Is.False, "the supervisor runs no first turn: the app started the child");
        var brief = rig.Store.Undelivered(Child).Single(e => e.Kind == EventKinds.Brief);
        Assert.That(rig.Store.Events.Fields(brief)["doneWhen"], Is.EqualTo("C is done"));
        Assert.That(brief.From, Is.EqualTo(Parties.Session(parent)));
        Assert.That(File.Exists(file), Is.False);
    }

    [Test]
    public void Join_in_the_app_records_the_app_id_of_the_app_not_the_one_of_the_engine_id()
    {
        using var rig = new Rig();
        var parent = Parent(rig);
        var (_, output) = Run(rig, true, "new", "--title", "Child F", "--goal", "Do F.", "--parent", parent, "--app-start");
        Environment.SetEnvironmentVariable(ClaudeCodeProvider.HostSessionVariable, "local_1d15b9da-0000-0000-0000-000000000000");
        try
        {
            Run(rig, true, "join", StartFile(output), "--id", Child);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClaudeCodeProvider.HostSessionVariable, null);
        }
        Assert.That(rig.Store.Session(Child).Engine.AppId, Is.EqualTo("local_1d15b9da-0000-0000-0000-000000000000"), "R-18");
    }

    [Test]
    public void The_host_id_counts_only_in_the_app()
    {
        Environment.SetEnvironmentVariable(ClaudeCodeProvider.HostSessionVariable, "local_parent");
        try
        {
            Assert.That(new ClaudeCodeProvider(new()).CallerAppId(), Is.Null, "a -p child of an app session inherits the id of its parent");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClaudeCodeProvider.HostSessionVariable, null);
        }
    }

    [Test]
    public void A_second_join_with_the_same_file_or_id_is_refused()
    {
        using var rig = new Rig();
        var parent = Parent(rig);
        var (_, output) = Run(rig, true, "new", "--title", "Child D", "--goal", "Do D.", "--parent", parent, "--app-start");
        var file = StartFile(output);
        Run(rig, true, "join", file, "--id", Child);
        var (exit, again) = Run(rig, true, "join", file, "--id", Child);
        Assert.That(exit, Is.Not.EqualTo(0));
        Assert.That(again, Does.Contain("no start file"));
    }

    [Test]
    public void Join_uses_the_store_of_the_start_file()
    {
        using var rig = new Rig();
        using var other = new Rig();
        var parent = Parent(rig);
        var (_, output) = Run(rig, true, "new", "--title", "Child E", "--goal", "Do E.", "--parent", parent, "--app-start");
        var (exit, joined) = Run(other, true, "join", StartFile(output), "--id", Child);
        Assert.That(exit, Is.EqualTo(0), joined);
        Assert.That(new FileStore(rig.Folder).Registry.Exists(Child), Is.True);
        Assert.That(other.Store.Registry.Exists(Child), Is.False);
    }

    [Test]
    public void The_app_call_names_the_folder_the_title_and_the_first_command()
    {
        var text = new ClaudeCodeProvider(new()).AppStartInstruction(new("T 1", "C:/repo", false, "orch join \"C:/t/s.json\""))!;
        var cwd = System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath("C:/repo"))[1..^1];
        Assert.That(text, Does.Contain("\"title\":\"T 1\"").And.Contain($"\"cwd\":\"{cwd}\"").And.Contain("\"use_worktree\":false"));
        Assert.That(text, Does.Contain("orch join \\\"C:/t/s.json\\\"").And.Not.Contain("\\u0022"), "readable quotes for the model");
        if (OperatingSystem.IsWindows()) Assert.That(cwd, Is.EqualTo("C:\\\\repo"), "the app refuses C:/ as another spelling of C:\\ (R-17)");
        Assert.That(text, Does.Contain("inApp false"), "the fallback when the app tool is missing or refused");
    }
}
