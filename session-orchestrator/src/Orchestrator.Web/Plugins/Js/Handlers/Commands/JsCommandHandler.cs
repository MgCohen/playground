namespace Orchestrator.Web.Plugins.Js.Handlers.Commands;

using Microsoft.JSInterop;
using Orchestrator.Web.Plugins.Js.Contracts;
using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Orchestrator.Web.Plugins.Js.Infrastructure;
using Umpire.Messages.Contracts;
using Umpire.Vsa.Handlers.Commands;

internal sealed class JsCommandHandler<TResult>(IJSRuntime js, JsCalls calls)
    : CommandHandler<JsCommand<TResult>, TResult>
{
    public override async Task<Result<TResult>> HandleAsync(IEnvelope<JsCommand<TResult>> envelope)
    {
        var command = envelope.Message;
        var name = JsNames.NameOf(command);
        var arguments = JsNames.ArgumentsOf(command);
        envelope.Items[JsTraceStart.NameItem] = name;
        envelope.Items[JsTraceStart.ArgumentsItem] = JsNames.Show(arguments);
        using var call = calls.Start(command.Key, envelope.Cancellation);
        try
        {
            return await InvokeAsync(name, call.Token, arguments);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or OperationCanceledException
                                      or InvalidOperationException or System.Text.Json.JsonException)
        {
            var code = e is OperationCanceledException ? JsErrors.Canceled : JsErrors.Failed;
            return new Error(code, $"{name} (envelope {envelope.Id}): {e.GetType().Name}: {e.Message}");
        }
    }

    async Task<Result<TResult>> InvokeAsync(string name, CancellationToken cancel, object?[] arguments)
    {
        if (typeof(TResult) != typeof(Unit))
        {
            return await js.InvokeAsync<TResult>(name, cancel, arguments);
        }
        await js.InvokeVoidAsync(name, cancel, arguments);
        return (TResult)(object)Unit.Value;
    }
}
