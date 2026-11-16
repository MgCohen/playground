using Orchestrator.Store;

namespace Orchestrator.Web;

public sealed class TickService(Supervisor.Supervisor supervisor, FileStore store) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                supervisor.Tick();
            }
            catch (Exception e)
            {
                store.Log($"tick failed: {e.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }
}
