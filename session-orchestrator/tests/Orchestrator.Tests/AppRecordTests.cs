using Orchestrator.Store;

namespace Orchestrator.Tests;

[NonParallelizable]
public sealed class AppRecordTests
{
    [Test]
    public void The_app_record_is_found_under_any_account_folder()
    {
        var root = Directory.CreateTempSubdirectory("orch-app-").FullName;
        var old = AppRecords.Root;
        try
        {
            AppRecords.Root = root;
            Assert.That(AppRecords.Exists("local_abc"), Is.False);
            var folder = Path.Combine(root, "claude_x", "claude-code-sessions", "account", "org");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "local_abc.json"), "{}");
            Assert.That(AppRecords.Exists("local_abc"), Is.True);
        }
        finally
        {
            AppRecords.Root = old;
        }
    }

    [Test]
    public async Task A_child_that_the_app_shows_but_that_is_not_live_gets_the_parent_archive_path_at_once()
    {
        var root = Directory.CreateTempSubdirectory("orch-app-").FullName;
        var old = AppRecords.Root;
        try
        {
            AppRecords.Root = root;
            using var rig = new Rig();
            var parent = await rig.Started();
            var child = rig.Create("work", "child", parent);
            await rig.Cycle();
            await rig.Cycle();
            var appId = $"local_{child}";
            rig.Store.SaveMeta(rig.Store.Meta(child) with { AppId = appId, Imported = true });
            var folder = Path.Combine(root, "claude_x", "claude-code-sessions", "account", "org");
            Directory.CreateDirectory(folder);
            var record = Path.Combine(folder, $"{appId}.json");
            File.WriteAllText(record, "{ \"isArchived\": false }");
            new Lifecycle(rig.Store).Archive(child, "user", force: false);
            await rig.Cycle();
            var notices = rig.Store.Events.All().Where(e => e.Kind == Core.Events.EventKinds.Notice && e.To == Core.Events.Parties.Session(parent)).ToList();
            var archiveChild = notices.Where(e => rig.Store.Events.Fields(e).GetValueOrDefault("kind") as string == "archive-child").ToList();
            Assert.That(archiveChild, Has.Count.EqualTo(1), "no wait for the deadline: the child is not live, so it cannot archive itself");
            Assert.That(rig.Store.Renderer.Text(archiveChild[0]), Does.Contain("enabled false").And.Contain(appId));
            Assert.That(new StateReader(rig.Store).State(child).File?.Requested?.To, Is.EqualTo(Core.Lifecycle.States.Archived));
            File.WriteAllText(record, "{ \"isArchived\": true }");
            await rig.Cycle();
            Assert.That(new StateReader(rig.Store).State(child).File?.State, Is.EqualTo(Core.Lifecycle.States.Archived));
            rig.Clock.Advance(TimeSpan.FromMinutes(15));
            await rig.Cycle();
            Assert.That(rig.Store.Events.All().Count(e => e.Kind == Core.Events.EventKinds.Notice && rig.Store.Events.Fields(e).GetValueOrDefault("kind") as string == "archive-child"), Is.EqualTo(1));
        }
        finally
        {
            AppRecords.Root = old;
        }
    }

    [Test]
    public void The_archive_notices_turn_remote_control_off_before_the_archive()
    {
        using var rig = new Rig();
        foreach (var kind in new[] { "archive-self", "archive-child" })
        {
            var text = rig.Store.Templates.Render(Core.Templates.Kinds.Notice, rig.Store.Texts.Notice(kind, ("title", "t"), ("child", "c"), ("appId", "local_c")), kind);
            Assert.That(text.IndexOf("set_remote_control", StringComparison.Ordinal), Is.LessThan(text.IndexOf("archive_session", StringComparison.Ordinal)), kind);
            Assert.That(text, Does.Contain("enabled false"), kind);
        }
    }

    [Test]
    public void Archive_tree_archives_the_children_first()
    {
        using var rig = new Rig();
        var root = rig.Create("root", "root");
        var child = rig.Create("child", "child", parent: root);
        var grandchild = rig.Create("grandchild", "grandchild", parent: child);
        var lifecycle = new Lifecycle(rig.Store);
        lifecycle.ArchiveTree(root, "user", force: false);
        Assert.That(new[] { root, child, grandchild }.Select(id => lifecycle.Control(id).State), Is.All.EqualTo(Core.Contract.ControlStates.Archived));
    }

    [Test]
    public void A_live_imported_session_gets_the_archive_self_notice_and_the_app_state_is_reported()
    {
        var root = Directory.CreateTempSubdirectory("orch-app-").FullName;
        var old = AppRecords.Root;
        try
        {
            AppRecords.Root = root;
            using var rig = new Rig();
            var live = rig.Create("live", "live");
            var gone = rig.Create("gone", "gone");
            var notLive = rig.Create("not live", "not live");
            foreach (var id in new[] { live, gone, notLive }) rig.Store.SaveMeta(rig.Store.Meta(id) with { Imported = true, AppId = $"local_{id}" });
            rig.MakeLive(live, TimeSpan.Zero, TimeSpan.Zero);
            rig.MakeLive(gone, TimeSpan.Zero, TimeSpan.Zero);
            var folder = Path.Combine(root, "claude_x", "claude-code-sessions", "account", "org");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"local_{live}.json"), "{\"isArchived\":false}");
            File.WriteAllText(Path.Combine(folder, $"local_{gone}.json"), "{\"isArchived\": true}");
            var lines = new Lifecycle(rig.Store).ArchiveInApp([live, gone, notLive], TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.StartWith($"The app still shows {live[..8]}"));
            Assert.That(rig.Store.Pending(live).Text, Does.Contain("archive_session with session_id \"self\""));
            Assert.That(rig.Store.Pending(notLive).Text, Does.Not.Contain("archive_session"));
            Assert.That(AppRecords.IsArchived($"local_{gone}"), Is.True);
        }
        finally
        {
            AppRecords.Root = old;
        }
    }
}
