using Microsoft.JSInterop;
using Orchestrator.Web;
using Orchestrator.Web.Components.Tray;

namespace Orchestrator.Tests;

/// <summary>The order of the tray windows (TrayFlow) with fake windows and pages: the faults (H3) and the busy notice (H4).</summary>
public sealed class TrayFlowTests
{
    sealed class FakeWindow : IWidgetWindow
    {
        public List<string> Steps { get; } = [];

        public List<string> Faults { get; } = [];

        public string Phase { get; private set; } = TrayPhase.Hidden;

        public bool Pinned => false;

        public event Action? PinnedChanged { add { } remove { } }

        public event Action<string>? Requested { add { } remove { } }

        public void SetPinned(bool pinned) { }

        public void Minimize() { }

        public void Close() { }

        public Task Enter(string phase)
        {
            Steps.Add($"window {phase}");
            Phase = phase;
            return Task.CompletedTask;
        }

        public void Fault(Exception error, string where) => Faults.Add($"{where}: {error.Message}");

        public void Peek(bool on) { }
    }

    sealed class FakePages(List<string> steps) : TrayFlow.IPanel, TrayFlow.IBadge
    {
        public Func<Task>? OnFold { get; set; }

        public Task Prepare() => Step("prepare");

        public Task Grow() => Step("grow");

        public async Task Fold()
        {
            steps.Add("fold");
            if (OnFold is { } fold) await fold();
        }

        public Task In() => Step("badge in");

        public Task Out() => Step("badge out");

        Task Step(string name)
        {
            steps.Add(name);
            return Task.CompletedTask;
        }
    }

    static (TrayFlow Flow, FakeWindow Window, FakePages Pages) Start()
    {
        var window = new FakeWindow();
        var pages = new FakePages(window.Steps);
        var flow = new TrayFlow(window);
        flow.Attach((TrayFlow.IPanel)pages);
        flow.Attach((TrayFlow.IBadge)pages);
        return (flow, window, pages);
    }

    sealed class FakeTrace : Orchestrator.Web.Plugins.Js.Contracts.IJsTrace
    {
        readonly List<string> lines = [];

        public IReadOnlyList<string> Lines => lines;

        public void Write(string line) => lines.Add(line);
    }

    [Test]
    public async Task Open_from_badge_order()
    {
        var window = new FakeWindow();
        var pages = new FakePages(window.Steps);
        var trace = new FakeTrace();
        var flow = new TrayFlow(window, trace);
        flow.Attach((TrayFlow.IPanel)pages);
        flow.Attach((TrayFlow.IBadge)pages);
        await flow.Request(TrayRequest.Open);
        await flow.Request(TrayRequest.Dock);
        window.Steps.Clear();
        var before = trace.Lines.Count;

        await flow.Request(TrayRequest.Open);

        Assert.That(window.Steps, Is.EqualTo(new[] { "badge out", "window start", "prepare", "window open", "grow" }),
            "badge-out, panel-show, prepare, panel-activate, grow");
        var windowLines = trace.Lines.Skip(before).Select(l => l.Split(':')[0]).ToList();
        Assert.That(windowLines, Is.EqualTo(new[] { "window panel-show", "window panel-activate" }));
        Assert.That(trace.Lines.Skip(before), Has.All.Contains("gap"), "each window step has its window gap");
    }

    [Test]
    public async Task Flow_reports_a_page_error()
    {
        var (flow, window, pages) = Start();
        await flow.Request(TrayRequest.Open);
        pages.OnFold = () => throw new JSException("the page is gone");

        await flow.Request(TrayRequest.Dock);

        Assert.That(window.Faults, Has.Count.EqualTo(1).And.Some.Contains("the page is gone"), "H3: the error goes to the fault log");
        Assert.That(flow.Phase, Is.EqualTo(window.Phase), "the phase of the flow is the phase of the windows");
    }

    [Test]
    public async Task A_notice_during_a_hide_brings_the_badge_after_it()
    {
        var (flow, window, pages) = Start();
        await flow.Request(TrayRequest.Open);
        var fold = new TaskCompletionSource();
        pages.OnFold = () => fold.Task;

        var hide = flow.Request(TrayRequest.Hide);
        var notice = flow.BringBadge();
        Assert.That(notice.IsCompleted, Is.False, "H4: the notice waits for the run");
        fold.SetResult();
        await hide;

        Assert.That(await notice, Is.True, "the badge comes in after the hide");
        Assert.That(flow.Phase, Is.EqualTo(TrayPhase.Badge));
        Assert.That(window.Steps.SkipWhile(s => s != "window hidden"), Is.EqualTo(new[] { "window hidden", "window badge-out", "badge in", "window badge" }));
    }

    [Test]
    public async Task A_notice_never_brings_the_badge_over_an_open_panel()
    {
        var (flow, _, pages) = Start();
        await flow.Request(TrayRequest.Open);
        var fold = new TaskCompletionSource();
        pages.OnFold = () => fold.Task;

        var hide = flow.Request(TrayRequest.Hide);
        var notice = flow.BringBadge();
        _ = flow.Request(TrayRequest.Open);
        pages.OnFold = null;
        fold.SetResult();
        await hide;

        Assert.That(await notice, Is.False);
        Assert.That(flow.Phase, Is.EqualTo(TrayPhase.Open), "the open of the user wins");
    }
}
