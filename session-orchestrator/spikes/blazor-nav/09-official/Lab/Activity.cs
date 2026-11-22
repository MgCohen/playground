using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Spike.Official.Lab;

/// <summary>The count of inbound circuit activities (events, JS interop results, render acks) of one circuit.</summary>
public sealed class Activity
{
    public int Count;
}

/// <summary>The official circuit hook: it wraps each inbound activity of the circuit.</summary>
public sealed class ActivityHandler(Activity activity) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) => context =>
    {
        Interlocked.Increment(ref activity.Count);
        return next(context);
    };
}
