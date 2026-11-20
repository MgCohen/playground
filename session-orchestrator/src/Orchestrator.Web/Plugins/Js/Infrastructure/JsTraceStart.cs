namespace Orchestrator.Web.Plugins.Js.Infrastructure;

using System.Diagnostics;
using Umpire.Messages.Contracts;
using Umpire.Messages.Handlers;

public sealed class JsTraceStart : BeforeStep
{
    public const string StartItem = "js.start";

    public const string NameItem = "js.name";

    public const string ArgumentsItem = "js.arguments";

    public override Task<object> BeforeAsync(Envelope envelope)
    {
        envelope.Items[StartItem] = Stopwatch.GetTimestamp();
        return Task.FromResult(envelope.Message);
    }
}
