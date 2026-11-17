using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Core.Policy;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tray;

/// <summary>
/// The mock data of the tray window: a new store in a temporary folder, with some sessions, cards of each kind and events.
/// The real store does not change. The folder goes when the app exits.
/// </summary>
public sealed class MockStore : IDisposable
{
    /// <summary>The policy of the mock store. No default creates a branch or a PR.</summary>
    public static readonly Policy MockPolicy = new(
        [new Level("low", "small", 60, true), new Level("medium", "design", 600, false), new Level("high", "hard to undo", null, false)],
        ["AGENTS.md"], OnDefault.None);

    MockStore(string folder)
    {
        Folder = folder;
        Store = new FileStore(folder, writer: Parties.Supervisor) { Policy = MockPolicy };
    }

    public string Folder { get; }

    public FileStore Store { get; }

    public static MockStore Create()
    {
        var mock = new MockStore(Directory.CreateTempSubdirectory("orch-tray-mock-").FullName);
        mock.Seed();
        return mock;
    }

    void Seed()
    {
        var messages = new Messages(Store);
        var cwd = Path.Combine(Folder, "work");
        Directory.CreateDirectory(cwd);

        var plan = Session("Plan the inbox redesign", cwd, null, "Split the inbox work into views, store queries and the tray app.");
        Update(plan, "Split", 60, "Three child sessions run. Two wait for a review.");
        Running(plan);

        var views = Session("A4 Inbox views", cwd, plan, "Make the new inbox views for the phone and the desktop.");
        Decision(views, "Put the filters in a side bar or in a top bar?", "The desktop window has more width than the phone.",
            ["Side bar on the desktop, top bar on the phone", "Top bar on all sizes"], "Side bar on the desktop, top bar on the phone", "medium");
        Update(views, "Layout", 35, "The card layout is done. The filters are next.");

        var tray = Session("T1 Desktop tray app", cwd, plan, "Make a tray app that shows the UI in a desktop window.");
        Running(tray);
        Update(tray, "Window", 70, "The window shows the mock data. The screenshot is next.");
        Decision(tray, "Install the tray app on this PC now?", "install.ps1 publishes one file to %USERPROFILE%\\Apps and adds a desktop shortcut.",
            ["Install now", "Install after the review"], null, "high");

        var queries = Session("A3 Stream queries", cwd, plan, "Give the stream queries one typed shape.");
        var shape = Decision(queries, "Keep one record for each query, or one generic record?", "Each view reads the same fields.",
            ["One record for each query", "One generic record"], "One generic record", "low");
        messages.Answer(queries, shape, "One generic record", Parties.Policy,
            source: new AnswerSource(Parties.Policy) { Reason = "The level low has a timeout of 60 seconds, and no answer came.", OnDefault = OnDefault.None });
        Report(queries, ReportStates.Done, "The queries have one typed shape. PR #12 is open.",
            ("done", new[] { "One record for each named query", "The stream page reads the typed shape", "12 new tests" }),
            ("next", new[] { "Review PR #12" }),
            ("links", new[] { "https://github.com/MgCohen/playground/pull/12" }));
        Store.Events.Post(new PostInput(EventKinds.Notice, Parties.Supervisor, Parties.Session(plan), Messages.Fields(
            ("kind", "question-closed"), ("decision", shape), ("question", "Keep one record for each query, or one generic record?"),
            ("text", "One generic record"), ("title", "A3 Stream queries"), ("child", queries), ("by", Parties.Policy))) { Session = plan });
        Decision(queries, "Delete the old query helpers now?", "No code uses them after the change.", ["yes", "no"], "no", "low");

        var engine = Session("Pattern engine spike", cwd, null, "Test the composition rules of the pattern engine.");
        Store.Events.Post(new PostInput(EventKinds.Question, Parties.Session(engine), Parties.User,
            Messages.Fields(("question", "Which sample set do you want for the benchmark?"), ("context", "The spike needs real patterns to measure the speed.")))
        { Session = engine, Thread = Guid.NewGuid().ToString("N")[..12], Needs = Needs.Answer });
        Report(engine, ReportStates.Blocked, "The benchmark needs a sample set from the user.");

        var docs = Session("Docs cleanup", cwd, null, "Make the know-how page current.");
        messages.Send(docs, "Also add the WebView2 notes.");
        Report(docs, ReportStates.Done, "The know-how page is current.");
        new Lifecycle(Store).Stop(docs, Parties.User, "done for today");
    }

    string Session(string title, string cwd, string? parent, string goal)
    {
        var id = new Messages(Store).NewSession(new BriefFields { Title = title, Goal = goal, Cwd = cwd, Parent = parent }, Parties.User);
        Files.WriteJson(Store.PathOf(id, StoreFiles.Status), new Status("idle", null, Store.Now));
        return id;
    }

    void Running(string id) => Files.WriteJson(Store.PathOf(id, StoreFiles.Status), new Status("running", null, Store.Now));

    string Decision(string id, string question, string context, string[] options, string? defaultOption, string risk)
    {
        var thread = Guid.NewGuid().ToString("N")[..12];
        Store.Events.Post(new PostInput(EventKinds.Question, Parties.Session(id), Parties.User, Messages.Fields(
            ("question", question), ("context", context), ("options", options), ("recommendation", options[0]), ("risk", risk), ("defaultOption", defaultOption)).Where(f => f.Value is not null).ToDictionary())
        { Session = id, Thread = thread, Needs = Needs.Answer });
        return thread;
    }

    void Update(string id, string step, double progress, string summary) =>
        Store.Events.Post(new PostInput(EventKinds.Update, Parties.Session(id), Parties.User, Messages.Fields(("step", step), ("progress", progress), ("summary", summary))) { Session = id });

    void Report(string id, string state, string summary, params (string Key, object? Value)[] more) =>
        Store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(id), Parties.User, Messages.Fields([("state", state), ("summary", summary), .. more]))
        {
            Session = id,
            Needs = state is ReportStates.Done or ReportStates.Blocked ? Needs.Ack : null,
        });

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A runner with no claude process: each turn ends at once. The tray window never starts a session.</summary>
public sealed class NoRunner : ITurnRunner
{
    public Task<TurnResult> Run(TurnRequest request, CancellationToken cancel) => Task.FromResult(new TurnResult(true, "mock", null, 0));
}

/// <summary>A docs check that never finds an answer.</summary>
public sealed class NoDocs : IDocsChecker
{
    public Task<DocsResult> Check(DocsRequest request, CancellationToken cancel) => Task.FromResult(new DocsResult(false));
}
