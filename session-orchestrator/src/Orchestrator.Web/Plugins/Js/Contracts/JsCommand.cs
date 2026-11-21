namespace Orchestrator.Web.Plugins.Js.Contracts;

public abstract record JsCommand(string? Function = null)
{
    public string? Key { get; init; }
}
