using Microsoft.AspNetCore.Components;

namespace Spike.Official.Lab;

/// <summary>The data of a motion event: the animation or property name and the data-id of the element that moved.</summary>
public sealed class MotionEventArgs : EventArgs
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public double Elapsed { get; set; }
    public double At { get; set; }
}

/// <summary>The data of a traced click: the data-test of the nearest element that has one.</summary>
public sealed class TracedClickArgs : EventArgs
{
    public string Test { get; set; } = "";
    public double At { get; set; }
}

// The official custom events (Spike.Official.lib.module.js registers the browser side). Each name differs from its
// browser event name (the .NET 11 rule).
[EventHandler("onmotionend", typeof(MotionEventArgs), enableStopPropagation: true, enablePreventDefault: false)]
[EventHandler("onmotioncancel", typeof(MotionEventArgs), enableStopPropagation: true, enablePreventDefault: false)]
[EventHandler("onmotiontransend", typeof(MotionEventArgs), enableStopPropagation: true, enablePreventDefault: false)]
[EventHandler("onmotionsettled", typeof(MotionEventArgs), enableStopPropagation: true, enablePreventDefault: false)]
[EventHandler("ontracedclick", typeof(TracedClickArgs), enableStopPropagation: true, enablePreventDefault: true)]
public static class EventHandlers;
