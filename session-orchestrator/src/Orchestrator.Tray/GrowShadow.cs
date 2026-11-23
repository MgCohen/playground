namespace Orchestrator.Tray;

/// <summary>
/// G1: the shadow grows with the widget. The shadow of the system is on or off, and it has the full window size, so the grow
/// turns it off. This see-through window right below the widget draws a soft shadow around the shown part during the grow
/// and the shrink. At the end of the grow it fades out while the shadow of the system comes back.
/// </summary>
public sealed class GrowShadow : LayeredForm
{
    const int Spread = 28;
    const int FadeMs = 160;

    readonly System.Windows.Forms.Timer fade = new() { Interval = 15 };
    readonly System.Diagnostics.Stopwatch clock = new();
    Rectangle widget;
    int shown;
    float strength = 1;

    public GrowShadow()
    {
        Text = "Orchestrator shadow";
        fade.Tick += (_, _) =>
        {
            strength = 1 - Math.Min(1, clock.ElapsedMilliseconds / (float)FadeMs);
            Draw();
            if (strength > 0) return;
            fade.Stop();
            Hide();
        };
    }

    /// <summary>The shown part of the widget: its bottom <paramref name="height"/> pixels.</summary>
    public void Track(Form owner, int height)
    {
        fade.Stop();
        strength = 1;
        shown = height;
        var m = (int)(Spread * Dpi);
        if (owner.Bounds != widget || !Visible)
        {
            widget = owner.Bounds;
            Bounds = Rectangle.Inflate(widget, m, m);
        }
        if (!Visible) Show();
        Below(owner);
        Draw();
    }

    /// <summary>Fades the shadow out and hides it.</summary>
    public void FadeOut()
    {
        if (!Visible) return;
        clock.Restart();
        fade.Start();
    }

    void Draw() => Frame(g =>
    {
        if (shown <= 0 || strength <= 0) return;
        var m = Spread * Dpi;
        var rect = new RectangleF(m, m + widget.Height - shown, widget.Width, shown);
        using var path = Round(rect, 8 * Dpi);
        SoftShadow(g, path, Dpi, 1.3f * strength, 10, 5);
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing) fade.Dispose();
        base.Dispose(disposing);
    }
}
