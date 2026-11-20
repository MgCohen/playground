namespace Orchestrator.Web.Plugins.Js.Infrastructure;

internal sealed class JsCalls
{
    readonly Dictionary<string, CancellationTokenSource> newest = [];
    readonly Lock gate = new();

    public JsRun Start(string? key, CancellationToken outer)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        if (key is null)
        {
            return new JsRun(source, () => { });
        }
        lock (gate)
        {
            if (newest.Remove(key, out var older))
            {
                older.Cancel();
            }
            newest[key] = source;
        }
        return new JsRun(source, () => End(key, source));
    }

    void End(string key, CancellationTokenSource source)
    {
        lock (gate)
        {
            if (newest.TryGetValue(key, out var current) && current == source)
            {
                newest.Remove(key);
            }
        }
    }
}
