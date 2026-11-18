using System.Text.Json;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Ordering;
using Orchestrator.Core.Policy;
using Orchestrator.Core.Storage;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>
/// The contract of <see cref="IRepository{T, TId}"/>. Each fixture below runs these tests against one data kind of one
/// implementation. A new implementation (for example Notion, H6) adds its fixtures here.
/// </summary>
public abstract class RepositoryContract<T>(string backend)
{
    string root = "";

    /// <summary>The store of the backend of this fixture: the local folder, or the store API of a test supervisor on it (H5).</summary>
    protected IRepositories Repos(string root) => backend == Backends.Http ? TestStoreApi.Open(root) : new LocalRepositories(root);

    /// <summary>A new repository on the same store, as a second process opens it.</summary>
    protected abstract IRepository<Doc<T>, string> Open(string root);

    protected abstract string IdOf(int n);

    protected abstract T Sample(int n);

    /// <summary>False for a kind that never changes after its write: envelopes and session folders.</summary>
    protected virtual bool Changes => true;

    /// <summary>The number of ids that the kind can have. One for a single document, for example the policy.</summary>
    protected virtual int Ids => 3;

    static string Text(T? value) => StoreJson.Write(value);

    IRepository<Doc<T>, string> Open() => Open(root);

    [SetUp]
    public void Prepare() => root = Directory.CreateTempSubdirectory("orch-repo-").FullName;

    [TearDown]
    public void Clean()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void A_get_of_a_missing_id_gives_null()
    {
        Assert.That(Open().Get(IdOf(0)), Is.Null);
    }

    [Test]
    public void A_saved_add_gives_the_value_and_a_version()
    {
        var repo = Open();
        var doc = new Doc<T>(IdOf(0), Sample(0));
        repo.Add(doc);
        Assert.That(Open().Get(IdOf(0)), Is.Null, "an add waits for the save");
        repo.Save();
        var read = Open().Get(IdOf(0))!;
        Assert.That(Text(read.Value), Is.EqualTo(Text(Sample(0))));
        Assert.That(read.Version, Is.Not.EqualTo(0));
        Assert.That(doc.Version, Is.EqualTo(read.Version));
    }

    [Test]
    public void A_second_add_of_an_existing_id_fails_at_save_and_the_first_value_stays()
    {
        Open().TryCreate(IdOf(0), Sample(0));
        var other = Open();
        other.Add(new Doc<T>(IdOf(0), Sample(1)));
        var e = Assert.Throws<StoreConflictException>(() => other.Save())!;
        Assert.That(e.Conflict, Is.EqualTo(StoreConflict.Exists));
        Assert.That(e.Id, Is.EqualTo(IdOf(0)));
        Assert.That(Text(Open().Read(IdOf(0))), Is.EqualTo(Text(Sample(0))));
    }

    [Test]
    public void Two_adds_of_one_id_in_one_save_fail()
    {
        var repo = Open();
        repo.Add(new Doc<T>(IdOf(0), Sample(0)));
        repo.Add(new Doc<T>(IdOf(0), Sample(1)));
        Assert.That(Assert.Throws<StoreConflictException>(() => repo.Save())!.Conflict, Is.EqualTo(StoreConflict.Exists));
        Assert.That(Text(Open().Read(IdOf(0))), Is.EqualTo(Text(Sample(0))));
    }

    [Test]
    public void A_save_writes_the_change_of_a_read_document()
    {
        if (!Changes) Assert.Ignore("this kind does not change after its write");
        Open().TryCreate(IdOf(0), Sample(0));
        var repo = Open();
        var doc = repo.Get(IdOf(0))!;
        var before = doc.Version;
        doc.Value = Sample(1);
        repo.Save();
        var read = Open().Get(IdOf(0))!;
        Assert.That(Text(read.Value), Is.EqualTo(Text(Sample(1))));
        Assert.That(read.Version, Is.Not.EqualTo(before));
        Assert.That(doc.IsChanged, Is.False);
    }

    [Test]
    public void A_save_of_an_old_version_fails_and_the_newer_value_stays()
    {
        if (!Changes) Assert.Ignore("this kind does not change after its write");
        Open().TryCreate(IdOf(0), Sample(0));
        var first = Open();
        var second = Open();
        var a = first.Get(IdOf(0))!;
        var b = second.Get(IdOf(0))!;
        a.Value = Sample(1);
        first.Save();
        b.Value = Sample(2);
        var e = Assert.Throws<StoreConflictException>(() => second.Save())!;
        Assert.That(e.Conflict, Is.EqualTo(StoreConflict.Stale));
        Assert.That(Text(Open().Read(IdOf(0))), Is.EqualTo(Text(Sample(1))));
    }

    [Test]
    public void A_remove_of_an_old_version_fails()
    {
        if (!Changes) Assert.Ignore("this kind does not change after its write");
        Open().TryCreate(IdOf(0), Sample(0));
        var first = Open();
        var second = Open();
        var a = first.Get(IdOf(0))!;
        var b = second.Get(IdOf(0))!;
        a.Value = Sample(1);
        first.Save();
        second.Remove(b);
        Assert.That(Assert.Throws<StoreConflictException>(() => second.Save())!.Conflict, Is.EqualTo(StoreConflict.Stale));
        Assert.That(Open().Get(IdOf(0)), Is.Not.Null);
    }

    [Test]
    public void A_saved_remove_deletes_the_document()
    {
        Open().TryCreate(IdOf(0), Sample(0));
        var repo = Open();
        repo.Remove(repo.Get(IdOf(0))!);
        Assert.That(Open().Get(IdOf(0)), Is.Not.Null, "a remove waits for the save");
        repo.Save();
        Assert.That(Open().Get(IdOf(0)), Is.Null);
    }

    [Test]
    public void A_save_with_no_change_writes_nothing()
    {
        Open().TryCreate(IdOf(0), Sample(0));
        var repo = Open();
        var version = repo.Get(IdOf(0))!.Version;
        repo.Save();
        Assert.That(Open().Get(IdOf(0))!.Version, Is.EqualTo(version));
    }

    [Test]
    public void A_list_gives_the_saved_documents_that_match()
    {
        var repo = Open();
        for (var n = 0; n < Ids; n++) repo.Add(new Doc<T>(IdOf(n), Sample(n)));
        repo.Save();
        Assert.That(Open().List(_ => true).Select(d => d.Id), Is.EquivalentTo(Enumerable.Range(0, Ids).Select(IdOf)));
        Assert.That(Open().List(d => d.Id == IdOf(Ids - 1)).Select(d => Text(d.Value)), Is.EqualTo(new[] { Text(Sample(Ids - 1)) }));
    }
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalEventContract(string backend) : RepositoryContract<Event>(backend)
{
    static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    protected override IRepository<Doc<Event>, string> Open(string root) => Repos(root).Events;

    protected override string IdOf(int n) => $"{Start.AddDays(n).ToUnixTimeMilliseconds()}-0000000{n}-test";

    protected override Event Sample(int n) =>
        new(IdOf(n), Start.AddDays(n).ToString("O"), "s1", EventKinds.Message, Parties.User, Parties.Session("s1"), $"text {n}", $"bodies/x/{n}.json", new Dictionary<string, JsonElement>());

    protected override bool Changes => false;
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalBodyContract(string backend) : RepositoryContract<JsonElement>(backend)
{
    protected override IRepository<Doc<JsonElement>, string> Open(string root) => Repos(root).Bodies;

    protected override string IdOf(int n) => $"{StoreFiles.Bodies}/2026-11-1{n}/{n}-abc-test.json";

    protected override JsonElement Sample(int n) => JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["text"] = $"body {n} – ü", ["n"] = n }, StoreJson.Options);
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalPolicyContract(string backend) : RepositoryContract<Policy>(backend)
{
    static readonly Policy Default = Policy.Parse(File.ReadAllText(Home.Config("policy.default.json")), "policy.default.json");

    protected override IRepository<Doc<Policy>, string> Open(string root) => Repos(root).Policies;

    protected override string IdOf(int n) => StoreIds.Policy;

    protected override Policy Sample(int n) => Default with { OnDefault = n % 2 == 0 ? OnDefault.Commit : OnDefault.StackedPr, Docs = [$"docs-{n}"] };

    protected override int Ids => 1;
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalStoreVersionsContract(string backend) : RepositoryContract<StoreVersion>(backend)
{
    protected override IRepository<Doc<StoreVersion>, string> Open(string root) => Repos(root).StoreVersions;

    protected override string IdOf(int n) => StoreIds.Version;

    protected override StoreVersion Sample(int n) => new(n + 1, $"2026-11-18T10:00:0{n}Z");

    protected override int Ids => 1;
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalClaimContract(string backend) : RepositoryContract<string>(backend)
{
    protected override IRepository<Doc<string>, string> Open(string root) => Repos(root).Claims;

    protected override string IdOf(int n) => StoreIds.AnswerClaim($"decision-{n}");

    protected override string Sample(int n) => $"session:writer-{n}";
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalCursorContract(string backend) : RepositoryContract<CursorState>(backend)
{
    protected override IRepository<Doc<CursorState>, string> Open(string root) => Repos(root).Cursors;

    protected override string IdOf(int n) => $"session-s{n}/deliver";

    protected override CursorState Sample(int n) => new($"session:s{n}", "deliver", $"{n}-x-test", [$"{n}-x-test"], $"2026-11-18T10:00:0{n}Z") { Filter = "to = self" };
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalNudgeContract(string backend) : RepositoryContract<string>(backend)
{
    protected override IRepository<Doc<string>, string> Open(string root) => Repos(root).Nudges;

    protected override string IdOf(int n) => $"session-s{n}";

    protected override string Sample(int n) => n == 0 ? "" : $"{n}-x-test";
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalSessionFolderContract(string backend) : RepositoryContract<string>(backend)
{
    protected override IRepository<Doc<string>, string> Open(string root) => Repos(root).Sessions;

    protected override string IdOf(int n) => $"session-{n}";

    protected override string Sample(int n) => IdOf(n);

    protected override bool Changes => false;
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalLeaseContract(string backend) : RepositoryContract<Lease>(backend)
{
    protected override IRepository<Doc<Lease>, string> Open(string root) => Repos(root).SessionFile<Lease>(StoreFiles.Lease);

    protected override string IdOf(int n) => $"session-{n}";

    protected override Lease Sample(int n) => new(n % 2 == 0 ? Holders.Live : Holders.Supervisor, $"2026-11-18T10:00:0{n}Z") { Pid = 100 + n };
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalHeartbeatContract(string backend) : RepositoryContract<string>(backend)
{
    protected override IRepository<Doc<string>, string> Open(string root) => Repos(root).SessionFile<string>(StoreFiles.Heartbeat);

    protected override string IdOf(int n) => $"session-{n}";

    protected override string Sample(int n) => $"2026-11-18T10:00:0{n}Z";
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalSessionItemContract(string backend) : RepositoryContract<DocsCheck>(backend)
{
    protected override IRepository<Doc<DocsCheck>, string> Open(string root) => Repos(root).SessionItems<DocsCheck>("session-1", StoreFiles.DocsChecks);

    protected override string IdOf(int n) => $"decision-{n}";

    protected override DocsCheck Sample(int n) => new(n % 2 == 0, $"answer {n}", "docs/plan.md", $"2026-11-18T10:00:0{n}Z");
}

/// <summary>The two rules at work in the store code: first answer wins, and a cursor merge keeps the done ids of each writer.</summary>
public sealed class RepositoryRuleTests
{
    [Test]
    public void A_cursor_merge_of_two_writers_keeps_all_done_ids()
    {
        using var t = new Rig();
        var a = new FileStore(t.Folder, t.Clock);
        var b = new FileStore(t.Folder, t.Clock);
        var ids = Enumerable.Range(0, 40).Select(n => Names.NewEventId(t.Clock.Now.AddMilliseconds(n), "test")).ToList();
        Parallel.Invoke(
            () => { foreach (var id in ids.Where((_, i) => i % 2 == 0)) a.Events.Subscribe(Parties.User, "merge", _ => true).Done(id); },
            () => { foreach (var id in ids.Where((_, i) => i % 2 == 1)) b.Events.Subscribe(Parties.User, "merge", _ => true).Done(id); });
        var cursor = a.Repositories.Cursors.Read($"{Parties.Folder(Parties.User)}/merge")!;
        Assert.That(cursor.Recent, Is.EquivalentTo(ids));
        Assert.That(cursor.Cursor, Is.EqualTo(ids.Max(Names.Order)));
    }

    [Test]
    public void A_body_file_has_the_same_text_as_a_write_of_the_body_fields()
    {
        var fields = new Dictionary<string, object?> { ["text"] = "a – ü \"q\" <b>", ["n"] = 2.5, ["list"] = new List<object?> { "x", 1 }, ["flag"] = true };
        Assert.That(StoreJson.Write(JsonSerializer.SerializeToElement(fields, StoreJson.Options)), Is.EqualTo(StoreJson.Write(fields)));
    }
}

public abstract class LocalDefinitionContract(Catalog catalog, string backend) : RepositoryContract<Definition>(backend)
{
    protected override IRepository<Doc<Definition>, string> Open(string root) => Repos(root).Definitions(catalog);

    protected override string IdOf(int n) => $"def-{n}";

    protected override Definition Sample(int n) => new(new Dictionary<string, string>
    {
        [catalog.Marker] = $"{{ \"name\": \"def-{n}\", \"n\": {n} }}",
        ["render/session.md"] = $"Text {n} – ü\nline 2",
    });
}

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalTemplateDefinitionContract(string backend) : LocalDefinitionContract(Catalog.Templates, backend);

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalModeDefinitionContract(string backend) : LocalDefinitionContract(Catalog.Modes, backend);

[TestFixture(Backends.Local)]
[TestFixture(Backends.Http)]
public sealed class LocalWorkflowDefinitionContract(string backend) : LocalDefinitionContract(Catalog.Workflows, backend);
