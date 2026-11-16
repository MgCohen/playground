using Orchestrator.Store;

namespace Orchestrator.Tests;

public sealed class ImportTests
{
    [Test]
    public async Task A_good_first_turn_imports_the_session_into_the_app_once()
    {
        using var rig = new Rig(import: true);
        var id = await rig.Started();
        rig.Store.Send(id, "more work");
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await rig.Cycle();
        Assert.That(rig.Imports, Is.EqualTo(new[] { id }));
        Assert.That(rig.Turns, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task The_supervisor_waits_after_an_import_so_that_the_app_can_take_the_lease()
    {
        using var rig = new Rig(import: true);
        var id = await rig.Started();
        rig.Store.Send(id, "more work");
        await rig.Cycle();
        Assert.That(rig.Turns, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task With_no_import_option_nothing_is_imported()
    {
        using var rig = new Rig();
        await rig.Started();
        Assert.That(rig.Imports, Is.Empty);
    }

    [Test]
    public async Task A_new_session_gets_its_title_on_the_first_turn()
    {
        using var rig = new Rig();
        rig.Create("start", title: "Foobar root");
        await rig.Cycle();
        Assert.That(rig.Turns[0].Title, Is.EqualTo("Foobar root"));
    }

    [Test]
    public void The_import_command_sets_the_working_folder_and_hides_the_window()
    {
        var command = string.Join(' ', Lifecycle.OpenCommand("abc", "C:/work/repo"));
        Assert.That(command, Does.Contain(@"-WorkingDirectory 'C:\work\repo'").And.Contain("-WindowStyle Hidden").And.Contain("'--resume','abc'"));
    }
}
