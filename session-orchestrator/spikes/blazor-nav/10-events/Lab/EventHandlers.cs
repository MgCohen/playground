using Microsoft.AspNetCore.Components;

namespace Spike.Events.Lab;

/// <summary>The result of one phase of a command: the command id, the phase, how it ended and its JS time.</summary>
public sealed class OrchDoneArgs : EventArgs
{
    public int Id { get; set; }
    public string Phase { get; set; } = "";
    public string By { get; set; } = "";
    public int Ms { get; set; }
}

// The one custom event of the channel. The browser event is "orch-done": the names differ (the .NET 11 rule).
[EventHandler("onorchdone", typeof(OrchDoneArgs), enableStopPropagation: true, enablePreventDefault: false)]
public static class EventHandlers;
