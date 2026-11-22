using Microsoft.Extensions.DependencyInjection;

namespace NavSpike.Shared;

/// <summary>The event log of one circuit. Each line has the time in ms since the first line.</summary>
public sealed class Trace
{
    readonly List<string> lines = [];
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    public IReadOnlyList<string> Lines => lines;

    public event Action? Changed;

    public void Add(string text)
    {
        lines.Add($"{clock.ElapsedMilliseconds,6} {text}");
        Console.WriteLine($"[trace] {text}");
        Changed?.Invoke();
    }
}

/// <summary>The shared count of the spike app (flow F4).</summary>
public sealed class CounterState
{
    public int Count { get; private set; }

    public event Action? Changed;

    public void Increment()
    {
        Count++;
        Changed?.Invoke();
    }
}

public static class SpikeServices
{
    /// <summary>Adds Trace and CounterState as scoped services (one for each circuit).</summary>
    public static IServiceCollection AddSpikeBase(this IServiceCollection services) =>
        services.AddScoped<Trace>().AddScoped<CounterState>();
}
