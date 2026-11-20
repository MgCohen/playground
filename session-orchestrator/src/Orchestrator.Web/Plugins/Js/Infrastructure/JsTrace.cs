namespace Orchestrator.Web.Plugins.Js.Infrastructure;

using Microsoft.Extensions.Logging;

internal sealed class JsTrace(ILogger<JsTrace> log)
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

    public void Write(string line)
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
        log.LogDebug("{Line}", line);
        Written?.Invoke(stamped);
    }
}
