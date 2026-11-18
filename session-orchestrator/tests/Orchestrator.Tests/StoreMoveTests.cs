using System.Security.Cryptography;
using Orchestrator.Core.Contract;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>H4: one default store root in each place, and orch store migrate. The tests use temp folders only.</summary>
[NonParallelizable]
public sealed class StoreMoveTests
{
    static string Repo => Home.Root();

    [Test]
    public void The_default_store_root_is_in_local_app_data_and_ORCH_STORE_overrides_it()
    {
        var old = Environment.GetEnvironmentVariable("ORCH_STORE");
        try
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", null);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.That(Home.StoreRoot(), Is.EqualTo(Path.Combine(local, "orch", "store")));
            Assert.That(Home.StoreRoot(), Is.EqualTo(Home.DefaultStoreRoot()));
            Assert.That(Home.StoreRoot(), Does.Not.StartWith(Repo), "the store is out of the checkout: git clean cannot delete it");
            var test = Path.Combine(Path.GetTempPath(), "orch-test-store");
            Environment.SetEnvironmentVariable("ORCH_STORE", test);
            Assert.That(Home.StoreRoot(), Is.EqualTo(test));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", old);
        }
    }

    [Test]
    public void The_plugin_install_ps1_and_the_tray_use_the_same_default()
    {
        var install = File.ReadAllText(Path.Combine(Repo, "install.ps1"));
        Assert.That(install, Does.Contain("[string] $Store = $(if ($env:ORCH_STORE) { $env:ORCH_STORE } else { Join-Path $env:LOCALAPPDATA 'orch\\store' })"));
        var backend = File.ReadAllText(Path.Combine(Repo, "plugin", "hooks", "backend.ts"));
        Assert.That(backend, Does.Contain("$.env.get('ORCH_STORE')").And.Contain("$.env.get('LOCALAPPDATA')").And.Contain("/orch/store`"));
        Assert.That(backend, Does.Not.Contain("${S.home}/store"), "the old default in the checkout is gone");
        var tray = File.ReadAllText(Path.Combine(Repo, "src", "Orchestrator.Tray", "Program.cs"));
        Assert.That(tray, Does.Contain("Home.StoreRoot()"), "the tray with --live and no folder uses the default store root");
    }

    [Test]
    public void Migrate_copies_the_store_checks_the_copy_and_keeps_the_source()
    {
        using var rig = new Rig();
        var session = rig.Create("start", "root");
        rig.Decide(session, "d1", "high", null);
        Home.CheckVersion(rig.Store.Repositories, rig.Clock.Now);
        var before = Hashes(rig.Folder);
        var target = Path.Combine(Directory.CreateTempSubdirectory("orch-move-").FullName, "orch", "store");
        var output = new StringWriter();
        var code = Run(target, output, "store", "migrate", "--from", rig.Folder);
        try
        {
            Assert.That(code, Is.EqualTo(0), output.ToString());
            Assert.That(output.ToString(), Does.Contain("The check found no difference").And.Contain("The old store did not change"));
            Assert.That(Hashes(target), Is.EqualTo(before), "the copy has each file with the same content");
            Assert.That(Hashes(rig.Folder), Is.EqualTo(before), "the source did not change");
            Assert.That(Directory.Exists(StoreMigration.TempOf(target)), Is.False);
            var moved = new FileStore(target, rig.Clock);
            Assert.That(moved.OpenDecisions(session).Select(d => d.Id), Is.EqualTo(new[] { "d1" }));

            var again = new StringWriter();
            Assert.That(Run(target, again, "store", "migrate", "--from", rig.Folder), Is.EqualTo(1));
            Assert.That(again.ToString(), Does.Contain("exists and has data").And.Contain("Nothing changed"));
            Assert.That(Hashes(target), Is.EqualTo(before), "a second migration writes nothing into a store");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(target))!, true);
        }
    }

    [Test]
    public void Migrate_sets_a_fresh_target_store_aside_and_deletes_nothing()
    {
        using var rig = new Rig();
        rig.Create("start", "root");
        Home.CheckVersion(rig.Store.Repositories, rig.Clock.Now);
        var parent = Directory.CreateTempSubdirectory("orch-move-").FullName;
        var target = Path.Combine(parent, "store");
        try
        {
            // Any orch command on the new default root makes a fresh store before the migration.
            var output = new StringWriter();
            Assert.That(Run(target, output, "init"), Is.EqualTo(0), output.ToString());
            Assert.That(StoreMigration.IsFresh(target), Is.True);
            output = new StringWriter();
            Assert.That(Run(target, output, "store", "migrate", "--from", rig.Folder), Is.EqualTo(0), output.ToString());
            Assert.That(output.ToString(), Does.Contain("fresh store with no data"));
            Assert.That(Hashes(target), Is.EqualTo(Hashes(rig.Folder)));
            var aside = Directory.GetDirectories(parent, "store.fresh-*").Single();
            Assert.That(File.Exists(Path.Combine(aside, "version.json")), Is.True, "the fresh store is kept, not deleted");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Test]
    public void Migrate_refuses_a_source_with_a_live_session_and_a_folder_that_is_not_a_store()
    {
        using var rig = new Rig();
        var session = rig.Create("start", "root");
        Home.CheckVersion(rig.Store.Repositories, rig.Clock.Now);
        Files.Write(rig.Store.PathOf(session, StoreFiles.Heartbeat), "now");
        var parent = Directory.CreateTempSubdirectory("orch-move-").FullName;
        var target = Path.Combine(parent, "store");
        try
        {
            var output = new StringWriter();
            Assert.That(Run(target, output, "store", "migrate", "--from", rig.Folder), Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("still write to the old store").And.Contain(session));
            Assert.That(Directory.Exists(StoreMigration.TempOf(target)), Is.False);

            var empty = Directory.CreateTempSubdirectory("orch-empty-").FullName;
            output = new StringWriter();
            Assert.That(Run(target, output, "store", "migrate", "--from", empty), Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("is not a store"));
            Directory.Delete(empty);

            output = new StringWriter();
            Assert.That(Run(target, output, "store", "migrate", "--from", rig.Folder, "--force"), Is.EqualTo(0), output.ToString());
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Test]
    public void The_check_finds_a_changed_a_missing_and_an_extra_file()
    {
        var a = Directory.CreateTempSubdirectory("orch-check-").FullName;
        var b = Directory.CreateTempSubdirectory("orch-check-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(a, "same.txt"), "x");
            File.WriteAllText(Path.Combine(b, "same.txt"), "x");
            File.WriteAllText(Path.Combine(a, "changed.txt"), "one");
            File.WriteAllText(Path.Combine(b, "changed.txt"), "two");
            File.WriteAllText(Path.Combine(a, "missing.txt"), "m");
            File.WriteAllText(Path.Combine(b, "extra.txt"), "e");
            File.WriteAllText(Path.Combine(a, "write.json.1.ab.tmp"), "a write in progress is not part of the store");
            var differences = StoreMigration.Check(a, b);
            Assert.That(differences, Has.Count.EqualTo(3));
            Assert.That(differences, Has.Some.Contains("other content").And.Some.Contains("changed.txt"));
            Assert.That(differences, Has.Some.Contains("missing in the copy: missing.txt"));
            Assert.That(differences, Has.Some.Contains("not in the source").And.Some.Contains("extra.txt"));
        }
        finally
        {
            Directory.Delete(a, true);
            Directory.Delete(b, true);
        }
    }

    /// <summary>Runs orch with ORCH_STORE set to the target, so the command never opens the real store.</summary>
    static int Run(string store, TextWriter output, params string[] args)
    {
        var old = Environment.GetEnvironmentVariable("ORCH_STORE");
        Environment.SetEnvironmentVariable("ORCH_STORE", store);
        try
        {
            return Cli.Cli.Run(args, output, output, Path.GetTempPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORCH_STORE", old);
        }
    }

    static Dictionary<string, string> Hashes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
