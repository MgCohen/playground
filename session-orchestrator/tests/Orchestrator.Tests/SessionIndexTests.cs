using System.Diagnostics;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

/// <summary>H4: the index by session of the event stream (finding B2-9) and the day cache of the envelope folders.</summary>
public sealed class SessionIndexTests
{
    static Dictionary<string, object?> Data(string text) => new() { ["text"] = text, ["state"] = "done", ["summary"] = text };

    static Dictionary<string, object?> ReportData(string summary) => new() { ["state"] = "done", ["summary"] = summary };

    [Test]
    public void OfSession_gives_the_envelopes_from_to_and_about_a_session_like_a_scan_of_the_stream()
    {
        var folder = Directory.CreateTempSubdirectory("orch-index-").FullName;
        try
        {
            var stream = new EventStream(folder, TimeProvider.System);
            stream.Open();
            string[] sessions = ["a", "b", "c"];
            for (var i = 0; i < 60; i++)
            {
                var from = Parties.Session(sessions[i % 3]);
                var to = i % 4 == 0 ? Parties.Session(sessions[(i + 1) % 3]) : Parties.Supervisor;
                stream.Append(i % 5 == 0 ? EventKinds.Report : EventKinds.SessionTool, from, to, Data($"e{i}"), session: i % 7 == 0 ? sessions[(i + 2) % 3] : null);
            }
            var middle = stream.All()[30];
            var since = DateTimeOffset.FromUnixTimeMilliseconds(StreamQuery.IdTime(middle.Id)!.Value);
            foreach (var id in sessions)
            {
                bool Of(Event e) => e.From == Parties.Session(id) || e.To == Parties.Session(id) || e.Session == id;
                Assert.That(stream.OfSession(id).Select(e => e.Id), Is.EqualTo(stream.All().Where(Of).Select(e => e.Id)), id);
                Assert.That(stream.OfSession(id, since).Select(e => e.Id), Is.EqualTo(stream.Since(since).Where(Of).Select(e => e.Id)), $"{id} since");
            }
            Assert.That(stream.OfSession("none"), Is.Empty);

            // A second reader (another process) gets the same index from the files.
            var other = new EventStream(folder, TimeProvider.System);
            Assert.That(other.OfSession("b").Select(e => e.Id), Is.EqualTo(stream.OfSession("b").Select(e => e.Id)));
            var late = stream.Append(EventKinds.Report, Parties.Session("b"), Parties.Supervisor, Data("late"));
            Assert.That(other.LastFrom(Parties.Session("b"), EventKinds.Report)?.Id, Is.EqualTo(late.Id), "a post of another writer is in the index at the next call");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void LastFrom_and_the_decisions_of_a_session_skip_forwards_and_other_sessions()
    {
        using var rig = new Rig();
        var a = rig.Create("start", "a");
        var b = rig.Create("start", "b");
        rig.Decide(a, "d1", "high", null);
        rig.Decide(b, "d2", "high", null);
        rig.Decide(a, "d3", "low", "A");
        var report = rig.Store.Events.Append(EventKinds.Report, Parties.Session(a), Parties.User, ReportData("first"), session: a);
        rig.Store.Events.Forward(report, Parties.Session(b));
        Assert.That(rig.Store.LastPost(a, EventKinds.Report)?.Id, Is.EqualTo(report.Id), "a forward is not a post of the session");
        Assert.That(rig.Store.Decisions(a).Select(d => d.Id), Is.EqualTo(new[] { "d1", "d3" }));
        Assert.That(rig.Store.OpenDecisions(b).Select(d => d.Id), Is.EqualTo(new[] { "d2" }));
        Assert.That(rig.Store.Events.PostsFrom(Parties.Session(a), EventKinds.Report), Has.Count.EqualTo(1));
    }

    [Test]
    public void The_day_cache_lists_a_day_folder_again_only_after_a_change()
    {
        var folder = Directory.CreateTempSubdirectory("orch-days-").FullName;
        try
        {
            var writer = new EventStream(folder, TimeProvider.System, writer: "w");
            writer.Open();
            writer.Append(EventKinds.SessionTool, Parties.Session("a"), Parties.Supervisor, Data("one"));
            var repo = new LocalEventRepository(folder);
            Assert.That(repo.ReadFrom(0), Has.Count.EqualTo(1));
            var day = Directory.GetDirectories(Path.Combine(folder, StoreFiles.Events)).Single();
            Directory.SetLastWriteTimeUtc(day, DateTime.UtcNow - TimeSpan.FromMinutes(1));
            Assert.That(repo.ReadFrom(1), Is.Empty);
            var listings = repo.Listings;
            for (var i = 0; i < 50; i++) Assert.That(repo.ReadFrom(1), Is.Empty);
            Assert.That(repo.Listings, Is.EqualTo(listings), "no change: no listing of the day folder");

            var two = writer.Append(EventKinds.SessionTool, Parties.Session("a"), Parties.Supervisor, Data("two"));
            Assert.That(repo.ReadFrom(1).Select(d => d.Id), Is.EqualTo(new[] { two.Id }), "a new file changes the write time of the folder");
            Assert.That(repo.Listings, Is.EqualTo(listings + 1));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void A_folder_write_time_within_the_settle_time_is_listed_at_each_call()
    {
        var folder = Directory.CreateTempSubdirectory("orch-days-").FullName;
        try
        {
            var writer = new EventStream(folder, TimeProvider.System, writer: "w");
            writer.Open();
            writer.Append(EventKinds.SessionTool, Parties.Session("a"), Parties.Supervisor, Data("one"));
            var repo = new LocalEventRepository(folder);
            repo.ReadFrom(0);
            var day = Directory.GetDirectories(Path.Combine(folder, StoreFiles.Events)).Single();
            // Two creates in one clock tick can leave the same folder write time: a fresh write time is no proof.
            Directory.SetLastWriteTimeUtc(day, DateTime.UtcNow);
            var listings = repo.Listings;
            repo.ReadFrom(1);
            repo.ReadFrom(1);
            Assert.That(repo.Listings, Is.EqualTo(listings + 2));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void The_snapshot_queries_of_a_session_do_not_list_the_days_again_on_a_large_stream()
    {
        using var rig = new Rig();
        var a = rig.Create("start", "a");
        rig.Decide(a, "d1", "high", null);
        for (var i = 0; i < 600; i++)
            rig.Store.Events.Append(EventKinds.SessionTool, Parties.Session($"other-{i % 20}"), Parties.Supervisor, Data($"t{i}"), session: $"other-{i % 20}");
        rig.Store.Events.Append(EventKinds.Report, Parties.Session(a), Parties.User, ReportData("done"), session: a);
        var repo = (LocalEventRepository)rig.Store.Repositories.Events;
        foreach (var day in Directory.GetDirectories(Path.Combine(rig.Folder, StoreFiles.Events)))
            Directory.SetLastWriteTimeUtc(day, DateTime.UtcNow - TimeSpan.FromMinutes(1));
        rig.Store.Report(a);
        var listings = repo.Listings;
        var watch = Stopwatch.StartNew();
        for (var tick = 0; tick < 200; tick++)
        {
            Assert.That(rig.Store.Report(a)?.Summary, Is.EqualTo("done"));
            Assert.That(rig.Store.OpenDecisions(a), Has.Count.EqualTo(1));
            Assert.That(rig.Store.Events.OfSession(a, rig.Clock.Now - TimeSpan.FromHours(1)), Is.Not.Empty);
        }
        watch.Stop();
        Assert.That(repo.Listings, Is.EqualTo(listings), "no listing of the day folders with no new file");
        TestContext.Out.WriteLine($"200 ticks of Report, OpenDecisions and OfSession on 600 envelopes: {watch.ElapsedMilliseconds} ms");
    }
}
