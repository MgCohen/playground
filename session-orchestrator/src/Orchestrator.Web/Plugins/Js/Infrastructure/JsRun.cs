namespace Orchestrator.Web.Plugins.Js.Infrastructure;

internal sealed class JsRun(CancellationTokenSource source, Action end) : IDisposable
{
    public CancellationToken Token => source.Token;

    public void Dispose()
    {
        end();
        source.Dispose();
    }
}
