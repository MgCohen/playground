namespace Orchestrator.Tray;

/// <summary>
/// G1 and V10: the one shadow of the widget. This see-through window right below the widget draws a soft shadow around the
/// shown part during the grow, at rest and during the shrink, so the shadow never changes. The system draws no shadow and no
/// round corners for the widget: the window region rounds it, and this window fills the corners with the page color, so the
/// hard cut of the region shows a soft edge.
/// </summary>
public sealed class GrowShadow : LayeredForm
{
    const int Spread = 28;
    const int FadeMs = 160;

    readonly System.Windows.Forms.Timer fade = new() { Interval = 15 };
    readonly System.Diagnostics.Stopwatch clock = new();
    Rectangle widget;
    int shown;

    /// <summary>
    /// V10: the color of the page canvas. The window draws a smooth rounded fill of it under the widget, so the hard cut of the
    /// window region at the corners shows a soft edge.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Color.FromArgb(0xe6, 0xe3, 0xde);
    float strength = 1;

    // V21: the mouse goes through the shadow window to the window below it.
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x20; // WS_EX_TRANSPARENT
            return p;
        }
    }

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
        if (!Visible) Faults.Trace($"shadow track: show at height {height}, owner visible {owner.Visible}, state {owner.WindowState}");
        // V20: no shadow for a widget that is hidden or minimized.
        if (!owner.Visible || owner.WindowState == FormWindowState.Minimized)
        {
            Hide();
            return;
        }
        fade.Stop();
        strength = 1;
        shown = height;
        var m = (int)(Spread * Dpi);
        // The shadow is on top only when the widget is (the pin), so it never covers another window.
        if (TopMost != owner.TopMost) TopMost = owner.TopMost;
        if (owner.Bounds != widget || !Visible)
        {
            widget = owner.Bounds;
            Bounds = Rectangle.Inflate(widget, m, m);
        }
        if (!Visible) Show();
        Below(owner);
        // V23: a move (a drag) only moves the window. The frame is drawn again only when its size, the shown height or the
        // fill color changes; a full draw for each step of a drag made the drag slow.
        var key = (widget.Size, shown, strength, Fill, Dpi);
        if (Visible && key.Equals(drawn)) return;
        drawn = key;
        Draw();
    }

    object? drawn;

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
        // An even soft shadow: thin fill layers that grow outward, moved down a little. No pen ring, so no dark band at the edge.
        // V22: a light shadow, close to the shadow of the system: about 17 % black at the edge, 18 px wide.
        const int Layers = 16;
        for (var i = Layers; i >= 1; i--)
        {
            var grow = i * (Spread - 10) * Dpi / Layers;
            var layer = RectangleF.Inflate(rect, grow, grow);
            layer.Offset(0, 2 * Dpi);
            using var lp = Round(layer, 8 * Dpi + grow);
            // Black: a warm color at an alpha of 4 rounds to pure red in the premultiplied bitmap, and the layers add it up.
            using var brush = new SolidBrush(Color.FromArgb((int)(3 * strength), 0, 0, 0));
            g.FillPath(brush, lp);
        }
        // The page color only in the four corners, under the hard cut of the widget region: never a full cover of the widget.
        using var fill = new SolidBrush(Fill);
        var c = 12 * Dpi;
        var state = g.Save();
        using (var corners = new Region(RectangleF.Empty))
        {
            corners.Union(new RectangleF(rect.Left, rect.Top, c, c));
            corners.Union(new RectangleF(rect.Right - c, rect.Top, c, c));
            corners.Union(new RectangleF(rect.Left, rect.Bottom - c, c, c));
            corners.Union(new RectangleF(rect.Right - c, rect.Bottom - c, c, c));
            g.SetClip(corners, System.Drawing.Drawing2D.CombineMode.Replace);
            g.FillPath(fill, path);
        }
        g.Restore(state);
    });

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        Faults.Trace($"shadow visible {Visible}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) fade.Dispose();
        base.Dispose(disposing);
    }
}
