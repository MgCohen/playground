using Microsoft.AspNetCore.Components;
using NavSpike.Shared;

namespace Spike.Official.Lab;

/// <summary>
/// A page base that traces each UI event through the official IHandleEvent hook. It shows what the hook knows about an
/// event: the argument type and the target method of the callback, not the element.
/// </summary>
public abstract class LabPage : TracedPage, IHandleEvent
{
    public static bool TraceEvents = true;

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg)
    {
        if (TraceEvents) Trace.Add($"{PageName}: IHandleEvent {arg?.GetType().Name ?? "null"}");
        await item.InvokeAsync(arg);
        StateHasChanged();
    }
}
