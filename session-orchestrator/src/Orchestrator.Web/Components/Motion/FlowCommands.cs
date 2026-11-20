using Orchestrator.Web.Plugins.Js.Contracts;
using Orchestrator.Web.Plugins.Js.Contracts.Commands;
using Umpire.Vsa.Contracts.Commands;

namespace Orchestrator.Web.Components.Motion;

/// <summary>
/// A JS command of a flow step (the tray stage and the badge). A JS error throws, so the tray flow writes a fault and takes the
/// phase of the windows (H3: nothing is swallowed). A cancel by a newer call with the same key does not throw.
/// </summary>
public static class FlowCommands
{
    public static async Task<T> Must<T>(this ICommandSender commands, JsCommand<T> command)
    {
        var result = await commands.SendAsync(command, default);
        if (result.Error is { } error && error.Code != JsErrors.Canceled)
            throw new InvalidOperationException($"{error.Code}: {error.Message}");
        return result.Value;
    }
}
