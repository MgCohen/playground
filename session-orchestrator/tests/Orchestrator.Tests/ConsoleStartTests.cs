using System.Diagnostics;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Providers.ClaudeCode;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// The switch of R-F8 for a parent outside the app: a new child that is not a background child starts as a console
/// session with Remote Control, not as a print turn. Its first turn is only the notice console-start; the plugin of the
/// live process delivers the brief. A parent in the app starts its children itself (AppStartTests).
/// </summary>
public sealed class ConsoleStartTests
{
    static void InApp(Rig t, string id) => t.Store.Change(id, e => e with { Engine = e.Engine with { Imported = true } });

    [Test]
    public async Task A_child_of_a_parent_outside_the_app_starts_in_a_console_with_only_the_start_notice()
    {
        using var t = new Rig(console: true);
        var parent = await t.Started();
        var child = t.Create("brief-text-7f3", "child", parent);
        await t.Cycle();
        var turn = t.Turns.Single(r => r.Id == child);
        Assert.That((turn.RemoteControl, turn.IsNew), Is.EqualTo((true, true)));
        Assert.That(turn.Prompt, Does.Contain("Remote Control").And.Contain("READY").And.Not.Contain("brief-text-7f3"));
        var meta = t.Store.Session(child);
        Assert.That((meta.Engine.Console, meta.Engine.RemoteControl), Is.EqualTo(((bool?)true, (bool?)true)));
        Assert.That(t.Store.Undelivered(child).Any(e => e.Kind != EventKinds.Notice), Is.True, "the plugin of the console delivers the brief");
    }

    [Test]
    public async Task The_supervisor_waits_for_the_plugin_of_the_console_and_runs_no_print_turn_at_once()
    {
        using var t = new Rig(console: true);
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await t.Cycle();
        await t.Cycle();
        Assert.That(t.Turns.Count(r => r.Id == child), Is.EqualTo(1));
    }

    [Test]
    public async Task A_console_that_takes_no_lease_is_stopped_and_the_child_starts_with_a_print_turn()
    {
        using var t = new Rig(console: true);
        var parent = await t.Started();
        var child = t.Create("brief-text-9c1", "child", parent);
        await t.Cycle();
        t.Clock.Advance(TimeSpan.FromSeconds(31));
        await t.Cycle();
        await t.Cycle();
        var turns = t.Turns.Where(r => r.Id == child).ToList();
        Assert.That(turns.Select(r => (r.RemoteControl, r.IsNew)), Is.EqualTo(new[] { (true, true), (false, true) }), "R-20: a trust prompt, for example");
        Assert.That(turns[1].Prompt, Does.Contain("brief-text-9c1"));
        Assert.That(t.Store.Session(child).Engine.Console, Is.Null);
    }

    [Test]
    public async Task A_console_whose_plugin_takes_the_lease_is_not_stopped()
    {
        using var t = new Rig(console: true);
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await t.Cycle();
        t.MakeLive(child, TimeSpan.Zero, TimeSpan.Zero);
        t.Clock.Advance(TimeSpan.FromSeconds(31));
        t.MakeLive(child, TimeSpan.Zero, TimeSpan.Zero);
        await t.Cycle();
        Assert.That(t.Turns.Count(r => r.Id == child), Is.EqualTo(1));
        Assert.That(t.Store.Session(child).Engine.Console, Is.True);
    }

    [Test]
    public async Task A_child_of_a_parent_in_the_app_a_background_child_and_a_root_do_not_start_in_a_console()
    {
        using var t = new Rig(console: true);
        var root = t.Create("start");
        var parent = await t.Started();
        InApp(t, parent);
        var appChild = t.Create("work", "app child", parent);
        var other = await t.Started();
        var quiet = t.Store.Create(new Seed { Title = "quiet", Cwd = "C:/x", Parent = other, CreatedBy = "supervisor", Background = true }, "work");
        await t.Cycle();
        Assert.That(t.Turns.Where(r => r.Id == root || r.Id == appChild || r.Id == quiet).All(r => !r.RemoteControl), Is.True);
        Assert.That(t.Turns.Any(r => r.Id == appChild) && t.Turns.Any(r => r.Id == quiet), Is.True);
    }

    [Test]
    public async Task Without_the_option_a_child_runs_a_print_turn_as_before()
    {
        using var t = new Rig();
        var parent = await t.Started();
        var child = t.Create("work", "child", parent);
        await t.Cycle();
        Assert.That(t.Turns.Single(r => r.Id == child).RemoteControl, Is.False);
        Assert.That(t.Store.Session(child).Engine.Console, Is.Null);
    }

    [Test]
    public void The_console_arguments_keep_the_session_id_and_turn_on_Remote_Control()
    {
        var request = new TurnRequest("id-1", "C:/x", "Reply READY.", true) { Title = "Child A", Agent = "worker", RemoteControl = true };
        Assert.That(ClaudeCodeProvider.ConsoleArguments(request, null),
            Is.EqualTo(new[] { "--setting-sources", "user", "--remote-control", "Child A", "--session-id", "id-1", "-n", "Child A", "--agent", "orch:worker", "Reply READY." }));
        Assert.That(ClaudeCodeProvider.ConsoleEnvironment(new()) ["ORCH_HOLDER"], Is.EqualTo(Holders.Live), "the plugin of a live process holds the lease live");
    }

    [TestCase("plain", "plain")]
    [TestCase("two words", "\"two words\"")]
    [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [TestCase("C:\\a b\\", "\"C:\\a b\\\\\"")]
    [TestCase("", "\"\"")]
    public void A_console_argument_is_quoted_for_the_Windows_command_line(string argument, string quoted) =>
        Assert.That(Claude.Quote(argument), Is.EqualTo(quoted));

    [Test]
    public void The_archive_stops_the_process_of_a_console_session()
    {
        using var t = new Rig();
        var id = t.Create("work", "console child");
        using var process = Process.Start(new ProcessStartInfo("ping", "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        t.Store.Change(id, e => e with { Engine = e.Engine with { Console = true } });
        t.Store.SaveLease(id, new Lease(Holders.Live, Times.Format(t.Clock.Now)) { Pid = process.Id });
        var lines = new Lifecycle(t.Store).Archive(id, Parties.User, force: true);
        Assert.That(process.WaitForExit(5000), Is.True);
        Assert.That(lines, Has.Some.Contains($"Stopped the console process {process.Id}"));
    }

    [Test]
    public void The_archive_leaves_the_process_of_a_session_that_is_not_a_console_session()
    {
        using var t = new Rig();
        var id = t.Create("work", "app child");
        using var process = Process.Start(new ProcessStartInfo("ping", "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        t.Store.SaveLease(id, new Lease(Holders.Live, Times.Format(t.Clock.Now)) { Pid = process.Id });
        try
        {
            new Lifecycle(t.Store).Archive(id, Parties.User, force: true);
            Assert.That(process.HasExited, Is.False);
        }
        finally
        {
            process.Kill();
        }
    }
}
