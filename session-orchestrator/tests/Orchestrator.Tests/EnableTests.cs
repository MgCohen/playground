using Orchestrator.Core.Contract;
using Orchestrator.Store;

namespace Orchestrator.Tests;

[NonParallelizable]
public sealed class EnableTests
{
    static (int Exit, string Output) Run(Rig rig, params string[] args)
    {
        var output = new StringWriter();
        Environment.SetEnvironmentVariable("ORCH_STORE", rig.Folder);
        try
        {
            return (Cli.Cli.Run(args, output, output, rig.Folder), output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
        }
    }

    [Test]
    public void Enable_writes_the_meta_and_the_enabled_file_of_the_session()
    {
        using var rig = new Rig();
        var (exit, output) = Run(rig, "enable", "--id", "aaaa1111-0000-0000-0000-000000000000", "--title", "Run 2: root");
        var meta = rig.Store.Session("aaaa1111-0000-0000-0000-000000000000");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(output, Does.Contain("Enabled as aaaa1111-0000-0000-0000-000000000000 (Run 2: root)"));
        Assert.That((meta.Title, meta.Lifecycle.Started, meta.Engine.Imported, meta.Created?.By), Is.EqualTo(("Run 2: root", (bool?)true, (bool?)true, "orch enable")));
        Assert.That(File.Exists(rig.Store.PathOf(meta.Id, StoreFiles.Enabled)), Is.True);
    }

    [Test]
    public void A_child_names_its_parent_and_a_second_enable_keeps_the_start()
    {
        using var rig = new Rig();
        Run(rig, "enable", "--id", "aaaa1111-0000-0000-0000-000000000000", "--title", "root");
        var first = rig.Store.Session("aaaa1111-0000-0000-0000-000000000000").Created?.At;
        Run(rig, "enable", "--id", "bbbb2222-0000-0000-0000-000000000000", "--title", "child", "--parent", "aaaa1111");
        Run(rig, "enable", "--id", "aaaa1111-0000-0000-0000-000000000000", "--title", "root, renamed");
        Assert.That(rig.Store.Session("bbbb2222-0000-0000-0000-000000000000").Parent, Is.EqualTo("aaaa1111-0000-0000-0000-000000000000"));
        Assert.That(rig.Store.Session("aaaa1111-0000-0000-0000-000000000000").Title, Is.EqualTo("root, renamed"));
        Assert.That(rig.Store.Session("aaaa1111-0000-0000-0000-000000000000").Created?.At, Is.EqualTo(first));
    }
}
