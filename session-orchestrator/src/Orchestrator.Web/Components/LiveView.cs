using Microsoft.AspNetCore.Components;
using Orchestrator.Store;

namespace Orchestrator.Web.Components;

public abstract class LiveView : ComponentBase, IDisposable
{
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

    Timer? timer;

    [Inject] protected Board Board { get; set; } = default!;

    protected string? Message { get; set; }

    protected override void OnInitialized()
    {
        Load();
        timer = new Timer(_ => InvokeAsync(() =>
        {
            Load();
            StateHasChanged();
        }), null, Refresh, Refresh);
    }

    protected abstract void Load();

    protected void Act(Action action, string done)
    {
        try
        {
            action();
            Message = done;
        }
        catch (Exception e) when (e is MessageException or IOException or ShellException)
        {
            Message = e.Message;
        }
        Load();
    }

    public void Dispose()
    {
        timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
