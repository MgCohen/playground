using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>The upgrade of a scratch store from version 2 to version 3. No test touches the real store.</summary>
[NonParallelizable]
public sealed class StoreUpgradeTests
{
    string root = "";

    [SetUp]
    public void Prepare() => root = Directory.CreateTempSubdirectory("orch-upgrade-").FullName;

    [TearDown]
    public void Clean()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }

    LocalRepositories Repos() => new(root);

    /// <summary>A store of version 2 as the code of version 2 wrote it: version.json, meta.json and control.json.</summary>
    void Version2Store()
    {
        Write("version.json", """{ "version": 2, "at": "2026-11-17T10:00:00Z" }""");
        Session("root-1", """
            { "id": "root-1", "title": "Root", "cwd": "C:/repo", "createdBy": "orch", "createdAt": "2026-11-17T10:00:00Z", "started": true,
              "agent": "orch:orchestrator", "allowDefaults": true, "imported": true, "appId": "local_root-1", "remoteControl": true }
            """, null);
        Session("child-b", """
            { "id": "child-b", "parent": "root-1", "title": "Second child", "cwd": "C:/repo.wt/orch-child-b", "createdBy": "session:root-1",
              "createdAt": "2026-11-17T10:05:00Z", "worktree": true, "repo": "C:/repo", "branch": "orch/session-child-b", "agent": "worker",
              "skills": ["orch:orch-report"], "background": true }
            """, """{ "state": "stopped", "at": "2026-11-17T11:00:00Z", "by": "user", "reason": "pause" }""");
        Session("child-a", """
            { "id": "child-a", "parent": "root-1", "title": "First child", "cwd": "C:/x", "createdBy": "session:root-1", "createdAt": "2026-11-17T10:01:00Z" }
            """, """{ "state": "archived", "at": "2026-11-17T12:00:00Z", "by": "supervisor" }""");
        Session("app-chat", """{ "id": "app-chat", "title": "App chat", "cwd": "C:/y" }""", null);
        Directory.CreateDirectory(Repos().PathOf("run-state-only"));
        File.WriteAllText(Repos().PathOf("run-state-only", StoreFiles.Heartbeat), "2026-11-17T09:00:00Z");
    }

    void Write(string file, string text)
    {
        var path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Session(string id, string meta, string? control)
    {
        Write($"sessions/{id}/{StoreFiles.Meta}", meta);
        if (control is not null) Write($"sessions/{id}/{StoreFiles.Control}", control);
        Write($"sessions/{id}/{StoreFiles.Enabled}", "");
    }

    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T10:00:00Z");

    [Test]
    public void The_upgrade_moves_meta_and_control_into_one_registry_entry_for_each_session()
    {
        Version2Store();
        var result = StoreUpgrade.ToVersion3(Repos(), Now, "Claude Code");
        var registry = new SessionRegistry(Repos().SessionEntries);

        Assert.That((result.From, result.Entries), Is.EqualTo((2, 4)));
        Assert.That(Repos().StoreVersions.Read(StoreIds.Version)!.Version, Is.EqualTo(3));
        Assert.That(registry.Exists("run-state-only"), Is.False, "a folder with no meta and no control has no registry data");

        var root1 = registry.Of("root-1");
        Assert.That((root1.Code, root1.Title, root1.Role, root1.Work.Cwd), Is.EqualTo(("O1", "Root", Roles.Orchestrator, "C:/repo")));
        Assert.That((root1.Engine.Kind, root1.Engine.SessionId, root1.Engine.AppId, root1.Engine.Imported, root1.Engine.RemoteControl),
            Is.EqualTo(("Claude Code", "root-1", "local_root-1", (bool?)true, (bool?)true)), "the core contract fields Imported, RemoteControl and AppId moved into the entry");
        Assert.That(root1.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.True);
        Assert.That((root1.Lifecycle.Phase, root1.Lifecycle.Started, root1.Created), Is.EqualTo((ControlStates.Active, (bool?)true, new Stamp("2026-11-17T10:00:00Z", "orch"))));

        var a = registry.Of("child-a");
        var b = registry.Of("child-b");
        Assert.That((a.Code, b.Code), Is.EqualTo(("C1", "C2")), "codes by create time under each parent");
        Assert.That(registry.FullCode("child-b"), Is.EqualTo("O1 C2"));
        Assert.That((a.Lifecycle.Phase, a.Lifecycle.By), Is.EqualTo((ControlStates.Archived, Parties.Supervisor)));
        Assert.That((b.Lifecycle.Phase, b.Lifecycle.Reason, b.Lifecycle.Started), Is.EqualTo((ControlStates.Stopped, "pause", (bool?)null)));
        Assert.That(b.IsNew(), Is.True, "a meta with createdBy and no started stays new");
        Assert.That((b.Work.Worktree, b.Work.Repo, b.Work.Branch), Is.EqualTo(((bool?)true, "C:/repo", "orch/session-child-b")));
        Assert.That((b.Type?.Agent, b.Type?.Skills?.Single(), b.Role), Is.EqualTo(("worker", "orch:orch-report", Roles.Worker)));
        Assert.That(b.IsPrivate(), Is.True, "background became visibility private");
        Assert.That(a.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.True, "a child copies the settings of its parent");

        var chat = registry.Of("app-chat");
        Assert.That((chat.Code, chat.Lifecycle.Started, chat.IsNew()), Is.EqualTo(("O2", (bool?)true, false)), "a meta with no createdBy is a started session");
        Assert.That(chat.Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("auto"));
    }

    [Test]
    public void The_upgrade_keeps_the_old_files_and_a_second_run_changes_nothing()
    {
        Version2Store();
        StoreUpgrade.ToVersion3(Repos(), Now, "Claude Code");
        var text = File.ReadAllText(Repos().PathOf("child-b", StoreFiles.Session));
        Assert.That(File.Exists(Repos().PathOf("child-b", StoreFiles.Meta)), Is.True, "meta.json stays as a backup");
        Assert.That(File.Exists(Repos().PathOf("child-b", StoreFiles.Control)), Is.True, "control.json stays as a backup");

        var again = StoreUpgrade.ToVersion3(Repos(), Now.AddHours(1), "Claude Code");
        Assert.That((again.From, again.Entries), Is.EqualTo((3, 0)));
        Assert.That(File.ReadAllText(Repos().PathOf("child-b", StoreFiles.Session)), Is.EqualTo(text));
    }

    [Test]
    public void A_stopped_upgrade_runs_again_and_keeps_the_entries_that_it_made()
    {
        Version2Store();
        new SessionRegistry(Repos().SessionEntries).Create(new SessionSpec { Id = "root-1", Code = "R9", Title = "made before", By = "test" });
        var result = StoreUpgrade.ToVersion3(Repos(), Now, "Claude Code");
        Assert.That(result.Entries, Is.EqualTo(3));
        Assert.That(new SessionRegistry(Repos().SessionEntries).Of("root-1").Title, Is.EqualTo("made before"));
    }

    [Test]
    public void A_dry_run_writes_nothing()
    {
        Version2Store();
        var result = StoreUpgrade.ToVersion3(Repos(), Now, "Claude Code", dryRun: true);
        Assert.That(result.Entries, Is.EqualTo(4));
        Assert.That(Repos().StoreVersions.Read(StoreIds.Version)!.Version, Is.EqualTo(2));
        Assert.That(File.Exists(Repos().PathOf("root-1", StoreFiles.Session)), Is.False);
    }

    [Test]
    public void The_upgrade_reads_only_version_2()
    {
        Write("version.json", """{ "version": 1, "at": "2026-11-01T10:00:00Z" }""");
        Assert.Throws<InvalidDataException>(() => StoreUpgrade.ToVersion3(Repos(), Now, "Claude Code"));
        Assert.Throws<InvalidDataException>(() => StoreUpgrade.ToVersion3(new LocalRepositories(Path.Combine(root, "none")), Now, "Claude Code"));
    }

    [Test]
    public void The_version_check_refuses_version_2_and_names_the_upgrade()
    {
        Version2Store();
        var error = Assert.Throws<InvalidDataException>(() => Home.CheckVersion(Repos(), Now))!;
        Assert.That(error.Message, Does.Contain("orch store upgrade"));
    }

    [Test]
    public void Orch_store_upgrade_refuses_a_live_session_and_then_upgrades()
    {
        Version2Store();
        File.WriteAllText(Repos().PathOf("root-1", StoreFiles.Heartbeat), Core.Leases.Times.Format(DateTimeOffset.UtcNow));
        var (exit, output) = Run("store", "upgrade");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(output, Does.Contain("No upgrade").And.Contain("root-1"));
        Assert.That(Repos().StoreVersions.Read(StoreIds.Version)!.Version, Is.EqualTo(2));

        (exit, output) = Run("store", "upgrade", "--force");
        Assert.That(exit, Is.EqualTo(0), output);
        Assert.That(output, Does.Contain("Upgraded").And.Contain("4 registry entries"));
        (exit, output) = Run("status", "child-b");
        Assert.That(exit, Is.EqualTo(0), output);
        Assert.That(output, Does.Contain("code: O1 C2").And.Contain("stopped"));
    }

    (int Exit, string Output) Run(params string[] args)
    {
        var output = new StringWriter();
        Environment.SetEnvironmentVariable("ORCH_STORE", root);
        try
        {
            return (Cli.Cli.Run(args, output, output, root), output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }
}
