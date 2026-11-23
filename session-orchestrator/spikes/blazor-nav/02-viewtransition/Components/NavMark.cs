namespace Spike.ViewTransition.Components;

/// <summary>The direction of the next navigation: "push", "back" or "fade". The library does not know push or back.</summary>
public sealed class NavMark
{
    public string Next { get; set; } = "fade";
}
