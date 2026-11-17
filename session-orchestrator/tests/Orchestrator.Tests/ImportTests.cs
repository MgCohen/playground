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
    public async Task A_new_session_is_imported_after_a_short_opening_turn_and_its_brief_waits_for_the_app()
    {
        using var rig = new Rig(import: true);
        var id = rig.Create("Do the work.", "child");
        await rig.Cycle();
        Assert.That(rig.Turns, Has.Count.EqualTo(1));
        Assert.That(rig.Turns[0].IsNew, Is.True);
        Assert.That(rig.Turns[0].Prompt, Does.Contain("opens this session in the desktop app").And.Not.Contain("Do the work."));
        Assert.That(rig.Imports, Is.EqualTo(new[] { id }), "the import comes right after the opening turn, before the brief");
        Assert.That(rig.Store.Pending(id).Text, Does.StartWith("Do the work."), "the brief waits for the live app session");
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        await rig.Cycle();
        Assert.That(rig.Turns, Has.Count.EqualTo(2), "if the app does not take the lease, the supervisor delivers the brief");
        Assert.That(rig.Turns[1].IsNew, Is.False);
        Assert.That(rig.Turns[1].Prompt, Does.StartWith("Do the work.").And.Contain("Remote Control"));
    }

    [Test]
    public async Task A_stopped_opening_turn_is_followed_by_the_opening_turn_again_and_not_by_the_brief()
    {
        using var rig = new Rig(import: true);
        rig.Runner.Gate = new TaskCompletionSource();
        var id = rig.Create("Do the work.", "child");
        rig.Supervisor.Tick();
        new Lifecycle(rig.Store).Stop(id, Core.Events.Parties.User);
        await rig.Cycle();
        rig.Runner.Gate = null;
        Assert.That(rig.Imports, Is.Empty);
        new Lifecycle(rig.Store).Resume(id, Core.Events.Parties.User);
        await rig.Cycle();
        Assert.That(rig.Turns, Has.Count.EqualTo(2));
        Assert.That(rig.Turns[1].Prompt, Does.Contain("opens this session in the desktop app").And.Not.Contain("Do the work."), "review of PR #14, item 7");
        Assert.That(rig.Turns[1].IsNew, Is.False, "the transcript exists: a resume");
        Assert.That(rig.Imports, Is.EqualTo(new[] { id }));
        Assert.That(rig.Store.Pending(id).Text, Does.StartWith("Do the work."));
    }

    [Test]
    public async Task With_no_import_option_the_first_turn_is_the_brief()
    {
        using var rig = new Rig();
        rig.Create("Do the work.");
        await rig.Cycle();
        Assert.That(rig.Turns.Single().Prompt, Is.EqualTo("Do the work."));
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
