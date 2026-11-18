using Microsoft.AspNetCore.Components;
using Orchestrator.Store;

namespace Orchestrator.Web.Components;

/// <summary>A view that loads again on each push of the stream, and at a slow sweep for the time-based state.</summary>
public abstract class LiveView : ComponentBase, IDisposable
{
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(10);

    Timer? timer;

    [Inject] protected Board Board { get; set; } = default!;
    [Inject] protected StreamHub Hub { get; set; } = default!;

    protected string? Message { get; set; }

    protected override void OnInitialized()
    {
        Load();
        Hub.Changed += Reload;
        timer = new Timer(_ => Reload(), null, Refresh, Refresh);
    }

    int pending;

    // A burst of file events gives one load: the first event waits a short time, the others in that time do nothing.
    void Reload()
    {
        if (Interlocked.Exchange(ref pending, 1) == 1) return;
        _ = Task.Delay(300).ContinueWith(_ => InvokeAsync(() =>
        {
            Interlocked.Exchange(ref pending, 0);
            Load();
            StateHasChanged();
        }), TaskScheduler.Default);
    }

    protected abstract void Load();

    protected bool Act(Action action, string done)
    {
        var ok = false;
        try
        {
            action();
            Message = done;
            ok = true;
        }
        catch (Exception e) when (e is MessageException or RegistryException or IOException or ShellException)
        {
            Message = e.Message;
        }
        Load();
        return ok;
    }

    public virtual void Dispose()
    {
        Hub.Changed -= Reload;
        timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
