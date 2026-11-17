using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Leases;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// Writes demo data for a live test of the UI into the store that ORCH_SEED names: sessions, cards of each type and a stream.
/// Explicit: it runs only with <c>dotnet test --filter DemoSeed</c>. Never point ORCH_SEED at <c>store/</c>.
/// </summary>
[Explicit("Writes demo data into ORCH_SEED for a live UI test")]
public sealed class DemoSeed
{
    [Test]
    public void Seed_a_demo_store()
    {
        var root = Environment.GetEnvironmentVariable("ORCH_SEED");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("Set ORCH_SEED to an empty scratch folder.");
        var store = new FileStore(root);

        string Session(string title, string? parent = null) => store.Create(new Meta("") { Title = title, Cwd = Path.GetTempPath(), Parent = parent, Started = true, Branch = "orch/session-demo" });
        void Update(string id, string step, double progress, string summary) =>
            store.Events.Post(new PostInput(EventKinds.Update, Parties.Session(id), Parties.User, new Dictionary<string, object?> { ["step"] = step, ["progress"] = progress, ["summary"] = summary }) { Session = id });

        var rootId = Session("Orchestrator: root");
        var a3 = Session("A3: stream queries", rootId);
        var b2 = Session("B2: session states", rootId);
        var a4 = Session("A4: inbox UI", rootId);
        var a2 = Session("A2: communication code", rootId);
        var phone = Session("Phone: color");

        Update(a3, "Step 3 of 5", 60, "query routes");
        Update(a4, "Build", 40, "Blazor screens");
        Update(rootId, "Wait", 20, "for 4 children");

        store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(a2), Parties.User, new Dictionary<string, object?>
        {
            ["state"] = ReportStates.Done, ["summary"] = "All communication goes through the Stream API.",
            ["done"] = new[] { "Stream API in C# and TypeScript", "Push with file watchers", "Ack and watchdog" },
            ["links"] = new[] { "PR #11 https://github.com/example/playground/pull/11" },
        }) { Session = a2, Needs = Needs.Ack });

        store.Events.Post(new PostInput(EventKinds.Query, Parties.Session(a3), Parties.User, new Dictionary<string, object?> { ["question"] = "Which store does the live test use?", ["context"] = "The test must not use store/." })
        { Session = a3, Thread = "demo-query-1", Needs = Needs.Answer });

        store.Events.Post(new PostInput(EventKinds.Decision, Parties.Session(phone), Parties.User, new Dictionary<string, object?>
        {
            ["question"] = "Which color goes in color.txt?", ["context"] = "The brief gives no color.", ["options"] = new[] { "RED", "BLUE" },
            ["recommendation"] = "No pick. The brief gives no reason.", ["risk"] = "high",
        }) { Session = phone, Thread = "demo-color", Needs = Needs.Answer });

        store.Events.Post(new PostInput(EventKinds.Decision, Parties.Session(a3), Parties.User, new Dictionary<string, object?>
        {
            ["question"] = "May I keep the old routes until A4 is merged?", ["context"] = "A4 reads the old routes.", ["options"] = new[] { "yes", "no" }, ["defaultOption"] = "yes",
            ["recommendation"] = "Yes. A4 still reads them.", ["risk"] = "low",
        }) { Session = a3, Thread = "demo-routes", Needs = Needs.Answer });

        store.Events.Post(new PostInput(EventKinds.Decision, Parties.Session(a3), Parties.User, new Dictionary<string, object?>
        {
            ["question"] = "Which sweep interval do we use for lost watcher events?", ["context"] = "A file watcher can drop events.", ["options"] = new[] { "60 s", "30 s", "120 s" }, ["defaultOption"] = "60 s",
            ["recommendation"] = "60 s. It is the default in policy.json.", ["risk"] = "medium",
        }) { Session = a3, Thread = "demo-sweep", Needs = Needs.Answer });

        store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(b2), Parties.User, new Dictionary<string, object?>
        {
            ["state"] = ReportStates.Blocked, ["summary"] = "The lease test cannot start.", ["blockers"] = new[] { "Remote Control is off in the second app session." },
        }) { Session = b2, Needs = Needs.Ack });

        Assert.That(new Inbox(store).Cards(), Has.Count.EqualTo(6));
        TestContext.Out.WriteLine($"Seeded {root} at {Times.Format(DateTimeOffset.UtcNow)}");
    }
}
