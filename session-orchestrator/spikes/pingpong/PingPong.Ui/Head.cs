namespace PingPong.Ui;

/// <summary>The few functions that differ between the heads.</summary>
public class Head
{
    string? start;

    /// <summary>Opens a page in the main app. Null: this head is a main app, so a link navigates.</summary>
    public Action<string>? OpenMain { get; init; }

    /// <summary>A route that the host asks for (a link from another program).</summary>
    public event Action<string>? Go;

    public string? TakeStart() => Interlocked.Exchange(ref start, null);

    /// <summary>Shows a route now, or first if no page listens yet.</summary>
    public void Show(string route)
    {
        if (Go is { } f) f(route);
        else start = route;
    }
}
