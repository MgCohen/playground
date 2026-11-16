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
