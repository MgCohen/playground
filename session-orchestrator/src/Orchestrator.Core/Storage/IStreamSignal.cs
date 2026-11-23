namespace Orchestrator.Core.Storage;

/// <summary>
/// The push of a subscription: a wake-up when a new envelope or a nudge arrives. It has no content: the subscriber
/// then queries from its cursor. A push can be lost, so each subscriber also sweeps after a time with no push.
/// </summary>
public interface IStreamSignal : IDisposable
{
    /// <summary>Raised on each change of the envelopes or the nudges.</summary>
    event Action? Changed;

    /// <summary>Waits for the next push, or for <paramref name="sweep"/>. Returns true for a push.</summary>
    Task<bool> Wait(TimeSpan sweep, CancellationToken cancel = default);
}

public static class StreamSignal
{
    public static readonly TimeSpan DefaultSweep = TimeSpan.FromSeconds(60);
}
