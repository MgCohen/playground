namespace Orchestrator.Web.Plugins.Js.Infrastructure;

using Microsoft.Extensions.Logging;
using Orchestrator.Web.Plugins.Js.Contracts;

internal sealed class JsTrace(ILogger<JsTrace> log) : IJsTrace
{
    const int Kept = 500;

    readonly Queue<string> lines = new();
    readonly Lock gate = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (gate)
            {
                return [.. lines];
            }
        }
    }

    public event Action<string>? Written;

    public void Write(string line) => Add(line, LogLevel.Debug);

    public void Warn(string line) => Add($"WARN {line}", LogLevel.Warning);

    void Add(string line, LogLevel level)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        lock (gate)
        {
            lines.Enqueue(stamped);
            while (lines.Count > Kept)
            {
                lines.Dequeue();
            }
        }
        log.Log(level, "{Line}", line);
        Written?.Invoke(stamped);
    }
}
