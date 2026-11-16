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
}
