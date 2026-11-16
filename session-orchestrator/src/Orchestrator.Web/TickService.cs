using Orchestrator.Store;

namespace Orchestrator.Web;

/// <summary>
/// Runs the supervisor tick at each push of the stream, and at least each <see cref="Interval"/> for the time rules:
/// the default timeouts, the reminders, the watchdog and the stale lease.
/// </summary>
public sealed class TickService(Supervisor.Supervisor supervisor, FileStore store, StreamHub hub) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                supervisor.Tick();
            }
            catch (Exception e)
            {
                store.Log($"tick failed: {e.Message}");
            }
            await hub.Wait(Interval, stop);
        }
    }
}
