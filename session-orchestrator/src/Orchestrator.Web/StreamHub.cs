using Orchestrator.Store;

namespace Orchestrator.Web;

/// <summary>
/// The push of the stream for the UI back end and the supervisor: one file watcher on events/ and nudges/.
/// Each change wakes the supervisor tick, the open views and the SSE clients.
/// </summary>
public sealed class StreamHub : IDisposable
{
    readonly Core.Storage.IStreamSignal watcher;

    public StreamHub(FileStore store)
    {
        store.Events.Open();
        watcher = store.Repositories.Watch();
        watcher.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public Task<bool> Wait(TimeSpan sweep, CancellationToken cancel) => watcher.Wait(sweep, cancel);

    public void Dispose() => watcher.Dispose();
}
