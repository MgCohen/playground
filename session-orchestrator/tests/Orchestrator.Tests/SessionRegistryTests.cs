using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Storage;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>The contract of <see cref="IRepository{T, TId}"/> on the session registry (store version 3).</summary>
public sealed class LocalSessionEntryContract : RepositoryContract<SessionEntry>
{
    protected override IRepository<Doc<SessionEntry>, string> Open(string root) => new LocalRepositories(root).SessionEntries;

    protected override string IdOf(int n) => $"session-{n}";

    protected override SessionEntry Sample(int n) => new(IdOf(n))
    {
        Code = $"C{n}",
        Title = $"Session {n}",
        Work = new SessionWork { Cwd = $"C:/work/{n}" },
        Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, n % 2 == 0 ? "auto" : "chat"),
        Created = new Stamp($"2026-11-18T10:00:0{n}Z", Parties.User),
    };
}

public sealed class SessionCodeTests
{
    static SessionRegistry Registry(out string root)
    {
        root = Directory.CreateTempSubdirectory("orch-registry-").FullName;
        return new SessionRegistry(new LocalRepositories(root).SessionEntries);
    }

    [Test]
    public void A_root_gets_O_codes_and_a_child_gets_C_codes_under_its_parent()
    {
        var registry = Registry(out _);
        var o1 = registry.Create(new SessionSpec { Title = "first root", By = Parties.User });
        var o2 = registry.Create(new SessionSpec { Title = "second root", By = Parties.User });
        var c1 = registry.Create(new SessionSpec { Parent = o2.Id, Title = "child", By = Parties.User });
        var c2 = registry.Create(new SessionSpec { Parent = o2.Id, Title = "child 2", By = Parties.User });
        var grandchild = registry.Create(new SessionSpec { Parent = c2.Id, Title = "Write the tests", By = Parties.User });
        var other = registry.Create(new SessionSpec { Parent = o1.Id, Title = "other", By = Parties.User });

        Assert.That(new[] { o1.Code, o2.Code, c1.Code, c2.Code, grandchild.Code, other.Code }, Is.EqualTo(new[] { "O1", "O2", "C1", "C2", "C1", "C1" }));
        Assert.That(registry.FullCode(grandchild.Id), Is.EqualTo("O2 C2 C1"), "the full code of a child is the parent code plus its own code");
        Assert.That(registry.FullCode(o1.Id), Is.EqualTo("O1"), "a root has a code too");
        Assert.That(registry.AppTitle(grandchild.Id), Is.EqualTo("[O2 C2 C1] Write the tests"));
        Assert.That(registry.Children(o2.Id).Select(e => e.Code), Is.EqualTo(new[] { "C1", "C2" }), "the children are a query of the parent field");
    }

    [Test]
    public void A_code_of_the_creator_must_be_unique_among_the_siblings()
    {
        var registry = Registry(out _);
        var root = registry.Create(new SessionSpec { By = Parties.User });
        registry.Create(new SessionSpec { Parent = root.Id, Code = "S2", By = Parties.User });
        Assert.Throws<RegistryException>(() => registry.Create(new SessionSpec { Parent = root.Id, Code = "S2", By = Parties.User }));
        Assert.Throws<RegistryException>(() => registry.Create(new SessionSpec { Parent = root.Id, Code = "S 3", By = Parties.User }), "a code has no space, because the full code uses spaces");
        Assert.That(registry.Create(new SessionSpec { Code = "S2", By = Parties.User }).Code, Is.EqualTo("S2"), "another parent can use the same code");
        Assert.That(registry.Create(new SessionSpec { Parent = root.Id, By = Parties.User }).Code, Is.EqualTo("C1"));
    }

    [Test]
    public void The_next_code_follows_the_highest_code_of_the_siblings()
    {
        var registry = Registry(out _);
        registry.Create(new SessionSpec { Code = "O7", By = Parties.User });
        registry.Create(new SessionSpec { Code = "X1", By = Parties.User });
        Assert.That(registry.Create(new SessionSpec { By = Parties.User }).Code, Is.EqualTo("O8"));
    }

    [Test]
    public void An_entry_of_the_plugin_with_no_code_gets_the_next_free_code()
    {
        var registry = Registry(out var root);
        var parent = registry.Create(new SessionSpec { By = Parties.User });
        // The shape that /orch-enable writes (plugin/hooks/register.ts, writeEntry).
        var path = new LocalRepositories(root).PathOf("plugin-1", StoreFiles.Session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            { "id": "plugin-1", "engine": { "kind": "Claude Code", "sessionId": "plugin-1" }, "title": "App chat in x", "role": "free",
              "work": { "cwd": "C:/x" }, "settings": {}, "lifecycle": { "phase": "active", "at": "2026-11-18T10:00:00Z", "by": "orch-enable", "started": true },
              "created": { "at": "2026-11-18T10:00:00Z", "by": "orch-enable" }, "updated": { "at": "2026-11-18T10:00:00Z", "by": "orch-enable" } }
            """);
        Assert.That(registry.Of("plugin-1").Code, Is.Null);
        Assert.That(registry.AssignCodes(Parties.Supervisor), Is.EqualTo(1));
        Assert.That(registry.Of("plugin-1").Code, Is.EqualTo("O2"), "the parent is O1");
        Assert.That(registry.Of("plugin-1").IsNew(), Is.False);
        Assert.That(registry.Resolve("plugin-1").Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("auto"), "an entry with no settings resolves to the defaults");
        Assert.That(registry.AssignCodes(Parties.Supervisor), Is.EqualTo(0));
    }

    [Test]
    public void Two_writers_of_one_entry_lose_no_change()
    {
        var registry = Registry(out var root);
        var id = registry.Create(new SessionSpec { Title = "a", By = Parties.User }).Id;
        var other = new SessionRegistry(new LocalRepositories(root).SessionEntries);
        var calls = 0;
        registry.Update(id, e =>
        {
            // The other writer saves between the read and the save of the first writer, one time.
            if (calls++ == 0) other.Update(id, x => x with { Title = "b" }, Parties.User);
            return e with { Work = e.Work with { Branch = "orch/x" } };
        }, Parties.Supervisor);
        var entry = registry.Of(id);
        Assert.That((entry.Title, entry.Work.Branch, calls), Is.EqualTo(("b", "orch/x", 2)));
        Assert.That(entry.Updated?.By, Is.EqualTo(Parties.Supervisor));
    }
}

public sealed class SessionSettingsTests
{
    static FileStore Store() => new(Directory.CreateTempSubdirectory("orch-settings-").FullName);

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Test]
    public void A_root_copies_the_defaults_of_each_tier()
    {
        var store = Store();
        var root = store.Registry.Create(new SessionSpec { By = Parties.User });
        Assert.That(root.Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("auto"));
        Assert.That(root.Settings.Text(SettingTiers.Behavior, SettingTiers.Visibility), Is.EqualTo(SettingTiers.Public));
        Assert.That(root.Settings.Text(SettingTiers.Permissions, SettingTiers.Mode), Is.EqualTo("auto"));
        Assert.That(root.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.False);
    }

    [Test]
    public void A_child_copies_each_value_that_its_creator_does_not_set_from_the_direct_parent()
    {
        var store = Store();
        var root = store.Registry.Create(new SessionSpec { By = Parties.User, Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, "chat") });
        var parent = store.Registry.Create(new SessionSpec
        {
            Parent = root.Id,
            By = Parties.User,
            Settings = new SessionSettings().With(SettingTiers.Permissions, SettingTiers.Mode, "bypass").With(SettingTiers.Behavior, SettingTiers.Visibility, SettingTiers.Private),
        });
        var child = store.Registry.Create(new SessionSpec
        {
            Parent = parent.Id,
            By = Parties.Session(parent.Id),
            Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, "approve"),
        });

        Assert.That(child.Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("approve"), "the own value first");
        Assert.That(child.Settings.Text(SettingTiers.Permissions, SettingTiers.Mode), Is.EqualTo("bypass"), "a copy of the direct parent");
        Assert.That(child.Settings.Text(SettingTiers.Behavior, SettingTiers.Visibility), Is.EqualTo(SettingTiers.Private));
        Assert.That(parent.Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("chat"), "the parent copied the root");
        Assert.That(child.IsPrivate(), Is.True);
    }

    [Test]
    public void A_later_change_of_the_parent_does_not_change_the_copy_of_a_child()
    {
        var store = Store();
        var parent = store.Registry.Create(new SessionSpec { By = Parties.User });
        var child = store.Registry.Create(new SessionSpec { Parent = parent.Id, By = Parties.User });
        store.Registry.Configure(parent.Id, SettingTiers.Behavior, SettingTiers.Mode, Json("chat"), Parties.User);
        Assert.That(store.Session(child.Id).Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("auto"));
        Assert.That(store.Session(parent.Id).Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("chat"));
    }

    [Test]
    public void Only_the_user_and_the_parent_change_a_setting()
    {
        var store = Store();
        var parent = store.Registry.Create(new SessionSpec { By = Parties.User });
        var child = store.Registry.Create(new SessionSpec { Parent = parent.Id, By = Parties.User });
        var other = store.Registry.Create(new SessionSpec { By = Parties.User });

        store.Registry.Configure(child.Id, SettingTiers.Behavior, SettingTiers.Mode, Json("approve"), Parties.Session(parent.Id));
        Assert.That(store.Session(child.Id).Settings.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("approve"));
        Assert.Throws<RegistryException>(() => store.Registry.Configure(child.Id, SettingTiers.Behavior, SettingTiers.Mode, Json("auto"), Parties.Session(other.Id)));
        Assert.Throws<RegistryException>(() => store.Registry.Configure(child.Id, SettingTiers.Behavior, SettingTiers.Mode, Json("auto"), Parties.Session(child.Id)));
        store.Registry.Configure(child.Id, SettingTiers.Permissions, SettingTiers.AllowDefaults, Json(true), Parties.User);
        Assert.That(store.Session(child.Id).Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.True);
    }

    [Test]
    public void A_mode_must_be_in_the_catalog_of_its_tier()
    {
        var store = Store();
        var root = store.Registry.Create(new SessionSpec { By = Parties.User });
        foreach (var mode in new[] { "auto", "chat", "ask", "approve" })
            Assert.DoesNotThrow(() => store.Registry.Configure(root.Id, SettingTiers.Behavior, SettingTiers.Mode, Json(mode), Parties.User), mode);
        Assert.Throws<RegistryException>(() => store.Registry.Configure(root.Id, SettingTiers.Behavior, SettingTiers.Mode, Json("nope"), Parties.User), "a behavior mode is a folder of templates/modes/");
        Assert.Throws<RegistryException>(() => store.Registry.Configure(root.Id, SettingTiers.Permissions, SettingTiers.Mode, Json("chat"), Parties.User));
        Assert.Throws<RegistryException>(() => store.Registry.Configure(root.Id, "colors", "mode", Json("red"), Parties.User));
        Assert.Throws<RegistryException>(() => store.Registry.Create(new SessionSpec { By = Parties.User, Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, "nope") }));
    }

    [Test]
    public void The_resolution_fills_a_key_that_an_entry_does_not_have_from_the_parent_chain_then_the_defaults()
    {
        var store = Store();
        var root = store.Registry.Create(new SessionSpec { By = Parties.User, Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, "chat") });
        var child = store.Registry.Create(new SessionSpec { Parent = root.Id, By = Parties.User });
        // An entry of an older version: no settings at all.
        store.Registry.Update(child.Id, e => e with { Settings = new SessionSettings() }, Parties.Supervisor);
        store.Registry.Update(root.Id, e => e with { Settings = new SessionSettings().With(SettingTiers.Behavior, SettingTiers.Mode, "chat") }, Parties.Supervisor);

        var resolved = store.Registry.Resolve(child.Id);
        Assert.That(resolved.Text(SettingTiers.Behavior, SettingTiers.Mode), Is.EqualTo("chat"), "from the parent");
        Assert.That(resolved.Text(SettingTiers.Permissions, SettingTiers.Mode), Is.EqualTo("auto"), "from the defaults");
    }

    [Test]
    public void The_brief_fields_background_and_allowDefaults_become_settings()
    {
        var store = Store();
        var id = new Messages(store).NewSession(new BriefFields { Title = "bg", Goal = "g", Cwd = "C:/x", Background = true, AllowDefaults = true, Agent = "worker", Skills = ["orch:orch-ask"] }, "test");
        var entry = store.Session(id);
        Assert.That(entry.Settings.Text(SettingTiers.Behavior, SettingTiers.Visibility), Is.EqualTo(SettingTiers.Private));
        Assert.That(entry.Settings.Flag(SettingTiers.Permissions, SettingTiers.AllowDefaults), Is.True);
        Assert.That((entry.Type?.Agent, entry.Type?.Skills?.Single(), entry.Role), Is.EqualTo(("worker", "orch:orch-ask", Roles.Free)));
        Assert.That((entry.Engine.Kind, entry.Engine.SessionId), Is.EqualTo((FileStore.EngineKind, id)));
        Assert.That(entry.Code, Is.EqualTo("O1"));
        Assert.That(entry.IsNew(), Is.True, "the first turn makes the engine session");
    }
}
