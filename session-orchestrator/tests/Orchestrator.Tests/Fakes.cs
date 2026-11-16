using Orchestrator.Core.Contract;
using Orchestrator.Core.Leases;
using Orchestrator.Core.Policy;
using Orchestrator.Store;
using Orchestrator.Supervisor;

namespace Orchestrator.Tests;

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

public sealed class FakeRunner(bool ok) : ITurnRunner
{
    public List<TurnRequest> Turns { get; } = [];
    public TaskCompletionSource? Gate { get; set; }

    public async Task<TurnResult> Run(TurnRequest request, CancellationToken cancel)
    {
        lock (Turns) Turns.Add(request);
        if (Gate is { } gate) await Task.WhenAny(gate.Task, Task.Delay(Timeout.Infinite, cancel)).ContinueWith(_ => { });
        if (cancel.IsCancellationRequested) return new TurnResult(false, null, "stopped", 0);
        return ok ? new TurnResult(true, "done", null, 0) : new TurnResult(false, null, "boom", 0);
    }
}

public sealed class FakeDocs(DocsResult result) : IDocsChecker
{
    public List<string> Checked { get; } = [];

    public Task<DocsResult> Check(DocsRequest request, CancellationToken cancel)
    {
        lock (Checked) Checked.Add(request.Question);
        return Task.FromResult(result);
    }
}

public sealed class Rig : IDisposable
{
    public static readonly Policy TestPolicy = new(
        [new Level("low", "small", 60, true), new Level("medium", "design", 600, false), new Level("high", "hard to undo", null, false)],
        ["AGENTS.md"], OnDefault.StackedPr);

    public Rig(bool ok = true, DocsResult? docs = null)
    {
        Folder = Directory.CreateTempSubdirectory("orch-").FullName;
        Clock = new ManualClock(DateTimeOffset.UtcNow);
        Store = new FileStore(Folder, Clock);
        Runner = new FakeRunner(ok);
        Docs = new FakeDocs(docs ?? new DocsResult(false));
        Supervisor = new Supervisor.Supervisor(Store, Runner, Docs, new SupervisorOptions(2, 2, 2, TestPolicy));
    }

    public string Folder { get; }
    public ManualClock Clock { get; }
    public FileStore Store { get; }
    public FakeRunner Runner { get; }
    public FakeDocs Docs { get; }
    public Supervisor.Supervisor Supervisor { get; }
    public List<TurnRequest> Turns => Runner.Turns;

    public string Create(string brief, string? title = null, string? parent = null, string createdBy = "supervisor") =>
        Store.Create(new Meta("") { Title = title, Cwd = "C:/x", Parent = parent, CreatedBy = createdBy }, brief);

    public async Task Cycle()
    {
        Supervisor.Tick();
        await Supervisor.Idle();
    }

    public async Task<string> Started()
    {
        var id = Create("start");
        await Cycle();
        return id;
    }

    public void Decide(string id, string decision, string risk, string? defaultOption) =>
        Store.SaveDecision(new Decision(decision, id) { Question = $"Q {decision}", Options = ["A", "B"], Risk = risk, DefaultOption = defaultOption, Asked = Times.Format(Clock.Now) });

    public void MakeLive(string id, TimeSpan leaseAge, TimeSpan heartbeatAge)
    {
        Store.SaveLease(id, new Lease(Holders.Live, Times.Format(Clock.Now - leaseAge)));
        Files.Write(Store.PathOf(id, StoreFiles.Heartbeat), Times.Format(Clock.Now - heartbeatAge));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
        }
    }
}
