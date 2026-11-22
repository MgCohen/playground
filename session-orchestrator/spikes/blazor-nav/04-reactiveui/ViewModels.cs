using NavSpike.Shared;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Spike.ReactiveUI;

/// <summary>The screen of one circuit: the RoutingState, the shared count (F4) and one view model for each tab.</summary>
public sealed class AppScreen : ReactiveObject, IScreen
{
    int count;

    public AppScreen(Trace trace)
    {
        Trace = trace;
        Router = new RoutingState();
        Home = new HomeViewModel(this);
        Counter = new CounterViewModel(this);

        Router.Navigate.Subscribe(vm => Trace.Add($"Router: Navigate -> {vm.UrlPathSegment}"));
        Router.NavigateBack.Subscribe(vm => Trace.Add($"Router: NavigateBack -> {vm?.UrlPathSegment}"));
        Router.NavigateAndReset.Subscribe(vm => Trace.Add($"Router: NavigateAndReset -> {vm.UrlPathSegment}"));
        Router.CurrentViewModel.Subscribe(vm => Trace.Add($"Router: CurrentViewModel = {vm?.UrlPathSegment}, stack {Router.NavigationStack.Count}"));

        Router.NavigateAndReset.Execute(Home).Subscribe();
    }

    public Trace Trace { get; }

    public RoutingState Router { get; }

    public HomeViewModel Home { get; }

    public CounterViewModel Counter { get; }

    public int Count
    {
        get => count;
        set => this.RaiseAndSetIfChanged(ref count, value);
    }
}

/// <summary>A routable view model that logs its WhenActivated block (activation and deactivation).</summary>
public abstract class PageViewModel : ReactiveObject, IRoutableViewModel, IActivatableViewModel
{
    protected PageViewModel(AppScreen screen, string segment)
    {
        Screen = screen;
        UrlPathSegment = segment;
        this.WhenActivated((Action<IDisposable> d) =>
        {
            screen.Trace.Add($"{segment} VM: WhenActivated");
            d(new Leave(() => screen.Trace.Add($"{segment} VM: WhenActivated disposed (deactivated)")));
        });
    }

    public AppScreen Screen { get; }

    public IScreen HostScreen => Screen;

    public string UrlPathSegment { get; }

    public ViewModelActivator Activator { get; } = new();

    sealed class Leave(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public sealed class HomeViewModel(AppScreen screen) : PageViewModel(screen, "Home")
{
    public void Add() => Screen.Count++;
}

public sealed class CounterViewModel(AppScreen screen) : PageViewModel(screen, "Counter")
{
    string note = "";

    public int Count => Screen.Count;

    /// <summary>The page state of F5. It lives in the view model, not in the view.</summary>
    public string Note
    {
        get => note;
        set => this.RaiseAndSetIfChanged(ref note, value);
    }
}
