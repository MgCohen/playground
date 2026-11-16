using Orchestrator.Core.Events;
using Orchestrator.Store;

namespace Orchestrator.Cli;

public static class LifecycleCommands
{
    public static int Stop(CommandContext c)
    {
        var id = c.Session(1);
        new Lifecycle(c.Store).Stop(id, Parties.User, c.Args.Option("reason"));
        c.Output.WriteLine($"Stopped {SessionCommands.Short(id)}. The supervisor ends a running turn and starts no new turn. The inbox stays. To continue: orch resume {SessionCommands.Short(id)}");
        return 0;
    }

    public static int Resume(CommandContext c)
    {
        var id = c.Session(1);
        new Lifecycle(c.Store).Resume(id, Parties.User);
        c.Output.WriteLine($"Resumed {SessionCommands.Short(id)}. The supervisor sends its waiting inbox at the next tick.");
        return 0;
    }

    public static int Archive(CommandContext c)
    {
        var id = c.Session(1);
        var lifecycle = new Lifecycle(c.Store);
        var tree = c.Args.Flag("tree");
        foreach (var blocked in (tree ? lifecycle.TreeOf(id) : [id]).Where(s => !c.Args.Flag("force") && lifecycle.ArchiveBlockers(s).Count > 0))
            throw new MessageException($"session {blocked} is not archived: {string.Join("; ", lifecycle.ArchiveBlockers(blocked))}. Use --force to archive it anyway.");
        if (!c.Args.Flag("store-only"))
            foreach (var line in lifecycle.ArchiveInApp(tree ? lifecycle.TreeOf(id) : [id], TimeSpan.FromSeconds(double.TryParse(c.Args.Option("wait"), out var seconds) ? seconds : 120))) c.Output.WriteLine(line);
        var lines = tree ? lifecycle.ArchiveTree(id, Parties.User, c.Args.Flag("force")) : lifecycle.Archive(id, Parties.User, c.Args.Flag("force"));
        foreach (var line in lines) c.Output.WriteLine(line);
        return 0;
    }

    public static int Open(CommandContext c)
    {
        var id = c.Session(1);
        var command = Lifecycle.OpenCommand(id, c.Store.Cwd(id));
        if (c.Args.Flag("dry-run"))
        {
            c.Output.WriteLine(string.Join(' ', command));
            return 0;
        }
        var lease = c.Store.Lease(id);
        if (lease.Holder == Core.Contract.Holders.Supervisor || c.Store.Status(id)?.State == "running" || c.Store.ReadJson<Core.Contract.TurnMarker>(id, Core.Contract.StoreFiles.Turn) is { Ended: null })
            throw new UsageException($"session {SessionCommands.Short(id)} has a running turn. Wait until it is idle, or run orch stop first, so that only one process writes it.");
        if (!new Lifecycle(c.Store).Import(id))
            throw new UsageException($"the desktop app did not confirm the import of {SessionCommands.Short(id)} in 30 s. Try again when the session is idle.");
        c.Output.WriteLine($"Opened {SessionCommands.Short(id)} in the desktop app as local_{id}. The app starts its process, and the plugin takes the lease live.");
        return 0;
    }
}
