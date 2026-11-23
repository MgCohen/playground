using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Cli;

/// <summary>orch store: the store root, and the migration of the old store in the checkout to the default store root (H4).</summary>
public static class StoreCommands
{
    public static int Store(CommandContext c)
    {
        var word = c.Args.Positional.Count < 2 ? "root" : c.Args.Positional[1];
        return word switch
        {
            "root" => Root(c),
            "migrate" => Migrate(c),
            "upgrade" => Upgrade(c),
            _ => throw new UsageException($"unknown store command {word}: use root, migrate or upgrade"),
        };
    }

    /// <summary>
    /// Upgrades the store from version 1 or 2 to version 3: meta.json and control.json of each session become the registry
    /// entry session.json. The old files stay as a backup. A store of version 1 also gets the history mark of its envelopes.
    /// </summary>
    static int Upgrade(CommandContext c)
    {
        var repos = c.Store.Repositories;
        var now = DateTimeOffset.UtcNow;
        var live = StoreUpgrade.LiveSessions(repos, now);
        if (live.Count > 0 && !c.Args.Flag("force"))
        {
            c.Output.WriteLine($"No upgrade: {live.Count} session(s) have a heartbeat in the last {StoreUpgrade.LiveWindow.TotalSeconds:0} s ({string.Join(", ", live.Take(5))}). Stop the supervisor, the tray and these sessions, then run the upgrade again (--force skips this check). Nothing changed.");
            return 1;
        }
        var dry = c.Args.Flag("dry-run");
        var result = StoreUpgrade.ToVersion3(repos, now, c.Provider.Name, dry);
        foreach (var line in result.Lines) c.Output.WriteLine($"  {line}");
        if (result.From == 3) return 0;
        c.Output.WriteLine(dry
            ? $"Dry run: the upgrade makes {result.Entries} registry entries in {repos.Location}. Nothing changed."
            : $"Upgraded {repos.Location} to version 3: {result.Entries} registry entries. meta.json and control.json stay as a backup. Restart the supervisor.");
        return 0;
    }

    static int Root(CommandContext c)
    {
        c.Output.WriteLine(c.Store.Root);
        if (!c.Store.Repositories.Exists() && StoreMigration.IsStore(Home.OldStoreRoot()))
            c.Output.WriteLine($"This store does not exist. The old store {Home.OldStoreRoot()} exists: run orch store migrate.");
        return 0;
    }

    /// <summary>Copies the old store to the store root, checks the copy, and tells what to do with the old folder. It deletes nothing.</summary>
    static int Migrate(CommandContext c)
    {
        var from = Path.GetFullPath(c.Args.Option("from") ?? Home.OldStoreRoot());
        var to = Path.GetFullPath(c.Args.Option("to") ?? c.Store.Root);
        if (StoreMigration.Refuse(from, to) is { } reason)
        {
            c.Output.WriteLine($"No migration: {reason}. Nothing changed.");
            return 1;
        }
        var now = DateTimeOffset.UtcNow;
        var live = StoreMigration.LiveSessions(from, now);
        if (live.Count > 0 && !c.Args.Flag("force"))
        {
            c.Output.WriteLine($"No migration: {live.Count} session(s) of {from} have a heartbeat in the last {StoreMigration.LiveWindow.TotalSeconds:0} s ({string.Join(", ", live.Take(5))}). They still write to the old store. Stop the supervisor, the tray and these sessions, then run the migration again (--force skips this check). Nothing changed.");
            return 1;
        }
        if (c.Args.Flag("dry-run"))
        {
            c.Output.WriteLine($"Dry run: the migration copies {from} to {to}. Nothing changed.");
            return 0;
        }
        var result = StoreMigration.Copy(from, to, now);
        if (!result.Ok)
        {
            c.Output.WriteLine($"The check of the copy failed: {result.Differences.Count} difference(s). The target {to} was not made. The source did not change.");
            foreach (var difference in result.Differences.Take(10)) c.Output.WriteLine($"  {difference}");
            c.Output.WriteLine($"The partial copy is in {StoreMigration.TempOf(to)}. Stop each writer of the old store, delete that copy, and run the migration again.");
            return 1;
        }
        c.Output.WriteLine($"Copied {result.Files} files ({result.Bytes:N0} bytes) from {from} to {to}. The check found no difference.");
        if (result.SetAside is { } aside) c.Output.WriteLine($"The target had a fresh store with no data (only version.json and policy.json). It is now in {aside}. Delete that folder when you like.");
        c.Output.WriteLine("The old store did not change. Next steps:");
        c.Output.WriteLine($"  1. Remove ORCH_STORE where it names {from}, or set it to {to}.");
        c.Output.WriteLine("  2. Run install.ps1 again: the tray shortcut then names the new store.");
        c.Output.WriteLine("  3. Start the supervisor and the tray, and run orch doctor.");
        c.Output.WriteLine($"  4. When the new store works, move {from} out of the checkout as a backup (a git clean -fdx deletes it). Do not delete it before the check of step 3.");
        return 0;
    }
}
