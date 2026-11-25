namespace Orchestrator.Tray;

/// <summary>
/// G1: the home ball. At show it pops in at the place of the home button of the widget, and the widget grows out of it; at hide
/// it pops out first, then the widget shrinks. If the page is not ready, it pulses before the grow. It has the look of the home
/// button (EdgeTab.Ball), so the handoff to the button of the page shows no change.
/// </summary>
public sealed class BallWindow : LayeredForm
{
    /// <summary>The pop in and the pop out, in milliseconds (G1 choices: in 240, out 180).</summary>
    public const int InMs = 240, OutMs = 180;

    readonly System.Windows.Forms.Timer clock = new() { Interval = 15 };
    readonly System.Diagnostics.Stopwatch time = System.Diagnostics.Stopwatch.StartNew();
    float diameter;
    long start = -1;
    int ms;
    bool popIn, pulse;
    double scale = 1, alpha = 1;
    Action? then;

    public BallWindow()
    {
        Text = "Orchestrator ball";
        clock.Tick += (_, _) => Step();
    }

    /// <summary>True while the ball pops in or out.</summary>
    public bool Popping => start >= 0;

    /// <summary>The scale of the last frame (the pop overshoots to 1.15).</summary>
    public double PopScale => scale;

    /// <summary>Puts the ball with its center on <paramref name="center"/> (screen pixels), <paramref name="size"/> pixels wide.</summary>
    public void Place(Point center, float size)
    {
        diameter = size;
        var box = (int)Math.Ceiling(size * EdgeTab.BallBox);
        Bounds = new Rectangle(center.X - box / 2, center.Y - box / 2, box, box);
    }

    /// <summary>Pops the ball in: it scales from 0.2 to 1.15, then to 1, and fades in.</summary>
    public void PopIn(Action? done)
    {
        if (!Visible) Show();
        Run(true, InMs, done);
    }

    /// <summary>Pops the ball out (the reverse of the pop in), then hides it.</summary>
    public void PopOut(Action? done)
    {
        if (!Visible)
        {
            scale = alpha = 1;
            Show();
        }
        Run(false, OutMs, () =>
        {
            Hide();
            done?.Invoke();
        });
    }

    /// <summary>The breath of a ball that waits for the page.</summary>
    public void Pulse(bool on)
    {
        pulse = on;
        if (on) clock.Start();
    }

    /// <summary>Hides the ball at once: the home button of the page takes its place.</summary>
    public void HideNow()
    {
        start = -1;
        pulse = false;
        clock.Stop();
        Hide();
    }

    void Run(bool into, int time, Action? done)
    {
        popIn = into;
        ms = time;
        then = done;
        start = this.time.ElapsedMilliseconds;
        clock.Start();
        Step();
    }

    void Step()
    {
        var now = time.ElapsedMilliseconds;
        var glow = 0.0;
        if (start >= 0)
        {
            var t = Math.Min(1, (now - start) / (double)ms);
            // The pop of the motion page: scale .2 → 1.15 (at 60 %) → 1, opacity 0 → 1 (at 60 %). The pop out is the reverse.
            var k = popIn ? t : 1 - t;
            scale = k < .6 ? .2 + (1.15 - .2) * Ease(k / .6) : 1.15 - .15 * Ease((k - .6) / .4);
            alpha = Math.Min(1, k / .6);
            if (t >= 1)
            {
                start = -1;
                var done = then;
                then = null;
                done?.Invoke();
            }
        }
        else if (pulse)
        {
            // A breath of 1 s: the ball gets a little smaller and an accent ring grows around it.
            var p = (now % 1000) / 1000.0;
            scale = 1 - .08 * Math.Sin(p * Math.PI);
            glow = p;
            alpha = 1;
        }
        else if (!Visible || then is null)
        {
            clock.Stop();
        }
        if (Visible) Draw(glow);
    }

    static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    void Draw(double glow) => Frame(g =>
    {
        var d = (float)(diameter * scale);
        var c = Width / 2f;
        if (glow > 0)
        {
            using var ring = new Pen(Color.FromArgb((int)(128 * (1 - glow)), 0xff, 0x6a, 0x1a), (float)(12 * Dpi * glow));
            var r = d / 2 + (float)(6 * Dpi * glow);
            g.DrawEllipse(ring, c - r, c - r, 2 * r, 2 * r);
        }
        if (alpha < 1)
        {
            // GDI+ has no layer alpha: draw on a bitmap, then copy it with the alpha.
            using var layer = new Bitmap(Width, Height);
            using (var lg = Graphics.FromImage(layer))
            {
                lg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                EdgeTab.Ball(lg, c, c, d);
            }
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)alpha });
            g.DrawImage(layer, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
            return;
        }
        EdgeTab.Ball(g, c, c, d);
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing) clock.Dispose();
        base.Dispose(disposing);
    }
}
