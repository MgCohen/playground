using Orchestrator.Core.Contract;

namespace Orchestrator.Store;

/// <summary>
/// The push of a subscription: a file watcher on events/ and nudges/. It only wakes the subscriber, which then
/// queries from its cursor. A watcher can drop file events when its buffer is full, so each subscriber also sweeps
/// from its cursor after a time with no push.
/// </summary>
public sealed class StreamWatcher : IDisposable
{
    public static readonly TimeSpan DefaultSweep = TimeSpan.FromSeconds(60);

    readonly List<FileSystemWatcher> watchers = [];
    readonly SemaphoreSlim signal = new(0, 1);

    public StreamWatcher(string root)
    {
        foreach (var folder in new[] { StoreFiles.Events, StoreFiles.Nudges })
        {
            var path = Path.Combine(root, folder);
            Directory.CreateDirectory(path);
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (_, _) => Wake();
            watcher.Renamed += (_, _) => Wake();
            watcher.Changed += (_, _) => Wake();
            watcher.Error += (_, _) => Wake();
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    /// <summary>Raised on each file event under events/ or nudges/.</summary>
    public event Action? Changed;

    void Wake()
    {
        try
        {
            if (signal.CurrentCount == 0) signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        Changed?.Invoke();
    }

    /// <summary>Waits for the next push, or for <paramref name="sweep"/>. Returns true for a push.</summary>
    public async Task<bool> Wait(TimeSpan sweep, CancellationToken cancel = default)
    {
        try
        {
            return await signal.WaitAsync(sweep, cancel);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        signal.Dispose();
    }
}
