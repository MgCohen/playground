using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Orchestrator.Tray;

/// <summary>The counts of the waiting cards by type, for the ring of the edge tab.</summary>
public sealed record WaitCounts(int Questions, int Reviews, int Reports, int Blocked)
{
    public int Total => Questions + Reviews + Reports + Blocked;
}

/// <summary>
/// N1: the minimized form of the widget, an edge tab at the right edge of the screen. It is a full circle (the home ball in the
/// ring of the waiting cards) on a tab that is flush with the screen edge; the ring is 6 px from the edge. A hover of 350 ms
/// extends the tab to the left with the counts. A click opens the widget: the tab slides out to the right. A drag moves it up
/// and down only, and the place is saved. A new event spins the ring.
/// N2: the peek bubble: a short text comes out of the tab, and goes back into it after 5 s.
/// The window is a layered window with per-pixel alpha (UpdateLayeredWindow), drawn with GDI+: soft edges, and the clear parts
/// let the clicks through. The web page is not used, so the tab shows at once.
/// The tab has its own UI thread (<see cref="Start"/>): a long job on the thread of the widget (a load of the Blazor views, a
/// count of the cards after a store change) does not stop its clock (V1, V2). The public acts go to that thread by themselves.
/// </summary>
public sealed class EdgeTab : LayeredForm
{
    // Logical pixels (96 DPI). The ring size: ball size 40 (N1 choice) × 1.4, as on the motion page.
    const int RingD = 56;
    const int Pad = 6;
    const int Shadow = 14;
    const int InfoMax = 230;
    const int BubbleMax = 240;
    const int BubbleGap = 10;

    /// <summary>The hover time before the tab extends (a quick click opens with no preview first).</summary>
    public const int HoverMs = 350;

    /// <summary>The slide in (minimize) and the slide out (open), in milliseconds.</summary>
    public const int SlideInMs = 260, SlideOutMs = 200;

    /// <summary>The time that the peek bubble shows (N2: hide after 5 s).</summary>
    public const int PeekMs = 5000;

    static readonly Color Surf = Color.FromArgb(0xfd, 0xfd, 0xfc);
    static readonly Color Sunken = Color.FromArgb(0xde, 0xdb, 0xd6);
    static readonly Color Ink = Color.FromArgb(0x1b, 0x1a, 0x18);
    static readonly Color Muted = Color.FromArgb(0x5f, 0x5b, 0x55);
    static readonly Color Accent = Color.FromArgb(0xff, 0x6a, 0x1a);
    static readonly Color[] Kinds = [Color.FromArgb(0xff, 0x6a, 0x1a), Color.FromArgb(0x6f, 0x9c, 0xff), Color.FromArgb(0x8f, 0xd9, 0x4f), Color.FromArgb(0xff, 0x5c, 0x5c)];

    readonly string? placeFile;
    readonly System.Windows.Forms.Timer clock = new() { Interval = 15 };
    readonly System.Diagnostics.Stopwatch time = System.Diagnostics.Stopwatch.StartNew();
    WaitCounts counts = new(0, 0, 0, 0);

    // The animated values: slide (0 off screen, 1 in), expand (0 ring only, 1 with the counts), spin (degrees), bubble (0 to 1).
    double slide, slideFrom, slideTo;
    long slideStart = -1, slideMs;
    double expand;
    long spinStart = -1;
    double bubble, bubbleTarget;
    long hoverSince = -1, bubbleUntil = -1;
    string bubbleTitle = "", bubbleSub = "";
    Action? afterSlide;

    // The drag: the mouse down point and the top of the window then.
    Point? down;
    int downTop;
    bool dragging;

    public EdgeTab(string? placeFile)
    {
        this.placeFile = placeFile;
        Text = "Orchestrator tab";
        clock.Tick += (_, _) => Step();
    }

    /// <summary>Makes the tab on a new UI thread with its own message loop, and returns when its window handle exists.</summary>
    public static EdgeTab Start(string? placeFile)
    {
        EdgeTab? tab = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            tab = new EdgeTab(placeFile);
            _ = tab.Handle;
            ready.Set();
            Application.Run();
        })
        { IsBackground = true, Name = "Edge tab" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return tab!;
    }

    /// <summary>Stops the thread of the tab and frees the tab.</summary>
    public void Stop()
    {
        if (IsDisposed || !IsHandleCreated) return;
        Invoke(() =>
        {
            Hide();
            Dispose();
            Application.ExitThread();
        });
    }

    // A call from another thread goes to the thread of the tab, in the order of the calls.
    bool Elsewhere(Action act)
    {
        if (!InvokeRequired) return false;
        if (!IsDisposed) BeginInvoke(act);
        return true;
    }

    /// <summary>The click on the tab: the widget opens. The tab slides out first.</summary>
    public event Action? OpenRequested;

    /// <summary>True while the tab is on the screen (also during a slide).</summary>
    public bool OnScreen => Visible && (slideTo > 0 || slide > 0);

    /// <summary>The shown part of the tab, 0 to 1; the ring is fully on the screen at 1.</summary>
    public double Slide => slide;

    /// <summary>True when the tab shows the counts.</summary>
    public bool Expanded => expand > .99;

    /// <summary>True while the peek bubble shows.</summary>
    public bool Peeking => bubble > .01;

    /// <summary>True while a slide, a spin, an extend or a peek runs.</summary>
    public bool Moving => (clock.Enabled || pumping) && (slideStart >= 0 || spinStart >= 0 || Math.Abs(expand - Target) > .001 || Math.Abs(bubble - bubbleTarget) > .001);

    /// <summary>The ring of the tab on the screen, in screen pixels.</summary>
    public Rectangle RingBounds
    {
        get
        {
            var (x, y, _) = TabRect();
            var s = Dpi;
            return new Rectangle(Left + (int)(x + Pad * s), Top + (int)(y + Pad * s), (int)(RingD * s), (int)(RingD * s));
        }
    }


    double Target => hoverSince >= 0 && (dragging || time.ElapsedMilliseconds - hoverSince >= HoverMs) ? 1 : 0;

    // ===== Show and hide =====

    /// <summary>Slides the tab in from the right edge of the screen.</summary>
    public void SlideIn()
    {
        if (Elsewhere(SlideIn)) return;
        Faults.Trace("side badge: slide in");
        Place();
        if (!Visible) Show();
        Animate(1, SlideInMs, null);
    }

    /// <summary>Slides the tab out to the right, then hides it and runs <paramref name="then"/>.</summary>
    public void SlideOut(Action? then = null)
    {
        if (Elsewhere(() => SlideOut(then))) return;
        Faults.Trace($"side badge: slide out, visible {Visible}");
        if (!Visible)
        {
            then?.Invoke();
            return;
        }
        hoverSince = -1;
        Animate(0, SlideOutMs, () =>
        {
            Hide();
            bubble = bubbleTarget = 0;
            bubbleUntil = -1;
            then?.Invoke();
        });
    }

    void Animate(double to, int ms, Action? then)
    {
        slideFrom = slide;
        slideTo = to;
        slideMs = ms;
        slideStart = time.ElapsedMilliseconds;
        afterSlide = then;
        Wake();
    }

    /// <summary>New counts. A higher total spins the ring and shows the peek bubble with the newest item.</summary>
    public void SetCounts(WaitCounts next, string? title = null, string? sub = null)
    {
        if (Elsewhere(() => SetCounts(next, title, sub))) return;
        var more = next.Total > counts.Total;
        counts = next;
        if (more)
        {
            spinStart = time.ElapsedMilliseconds;
            if (title is not null) Peek(title, sub ?? "");
        }
        Wake();
        Render();
    }

    /// <summary>N2: the peek bubble with a short text. It hides after <see cref="PeekMs"/>.</summary>
    public void Peek(string title, string sub)
    {
        if (Elsewhere(() => Peek(title, sub))) return;
        bubbleTitle = title;
        bubbleSub = sub;
        bubbleTarget = 1;
        bubbleUntil = time.ElapsedMilliseconds + PeekMs;
        Wake();
    }

    /// <summary>The self-test: the extended tab, as after a hover.</summary>
    public void Hover(bool on)
    {
        if (Elsewhere(() => Hover(on))) return;
        hoverSince = on ? time.ElapsedMilliseconds - HoverMs : -1;
        Wake();
    }

    // The window is a strip at the right edge of the work area: the bubble, the gap and the widest tab, with the shadow.
    void Place()
    {
        var s = Dpi;
        var area = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
        if (!Visible) area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var w = (int)((BubbleMax + BubbleGap + Pad + RingD + Pad + 10 + InfoMax + Shadow) * s);
        var h = (int)((RingD + 2 * Pad + 2 * Shadow) * s);
        var top = LoadTop() is { } saved ? saved : area.Top + area.Height / 2 - h / 2;
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h));
        Bounds = new Rectangle(area.Right - w, top, w, h);
    }

    // ===== Mouse: hover, click and the vertical drag =====

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = Cursor.Position;
        downTop = Top;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (down is not { } d || MouseButtons != MouseButtons.Left) return;
        var dy = Cursor.Position.Y - d.Y;
        if (!dragging && Math.Abs(dy) < 4 * Dpi) return;
        if (!dragging) hoverSince = time.ElapsedMilliseconds;
        dragging = true;
        var area = Screen.FromControl(this).WorkingArea;
        Top = Math.Clamp(downTop + dy, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Wake();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        var drag = dragging;
        down = null;
        dragging = false;
        if (drag)
        {
            SaveTop(Top);
            return;
        }
        if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
    }

    // ===== The clock =====

    void Wake()
    {
        if (clock.Enabled) return;
        lastStep = -1;
        clock.Start();
    }

    void Step()
    {
        var now = time.ElapsedMilliseconds;
        // The extend and the bubble move by the time since the last frame, not by a fixed step for each tick: a late tick
        // does not make the motion longer (V1).
        var dt = lastStep < 0 ? 15 : Math.Clamp(now - lastStep, 0, 100);
        lastStep = now;
        if (slideStart >= 0)
        {
            var t = Math.Min(1, (now - slideStart) / (double)slideMs);
            // In: ease out (cubic). Out: ease in.
            var e = slideTo > slideFrom ? 1 - Math.Pow(1 - t, 3) : t * t * t;
            slide = slideFrom + (slideTo - slideFrom) * e;
            if (t >= 1)
            {
                slideStart = -1;
                var then = afterSlide;
                afterSlide = null;
                then?.Invoke();
            }
        }
        // The hover: the pointer on the tab (or a drag) extends it after the hover time; the pointer away folds it.
        var over = Visible && TabScreenRect().Contains(Cursor.Position);
        if (over && hoverSince < 0) hoverSince = now;
        if (!over && !dragging && hoverSince >= 0 && down is null) hoverSince = -1;
        expand = Approach(expand, Target, dt / 200.0);
        if (bubbleUntil >= 0 && now >= bubbleUntil)
        {
            bubbleTarget = 0;
            bubbleUntil = -1;
        }
        bubble = Approach(bubble, bubbleTarget, dt / (bubbleTarget > bubble ? 260.0 : 200.0));
        if (spinStart >= 0 && now - spinStart >= 800) spinStart = -1;
        // Draw only a frame that changed: the clock also polls the pointer for the hover while the tab shows.
        var key = (slide, expand, bubble, spinStart >= 0 ? now : 0, counts, Top);
        if (Visible && !key.Equals(lastFrame)) Render();
        lastFrame = key;
        var idle = slideStart < 0 && spinStart < 0 && Math.Abs(expand - Target) < .001 && Math.Abs(bubble - bubbleTarget) < .001 && bubbleUntil < 0;
        // The hover needs the clock while the tab shows; a hidden tab stops it.
        if (idle && !Visible)
        {
            clock.Stop();
            lastStep = -1;
        }
        // A motion runs: the frames follow the display refresh, not the timer (V2).
        if (Animating && !pumping && Visible)
        {
            pumping = true;
            clock.Stop();
            BeginInvoke(Pump);
        }
    }

    long lastStep = -1;
    bool pumping;

    bool Animating => slideStart >= 0 || spinStart >= 0 || Math.Abs(expand - Target) > .001 || Math.Abs(bubble - bubbleTarget) > .001;

    // One frame for each refresh of the display: draw, wait for the next composition of the screen, then post the next frame.
    // The wait blocks only the thread of the tab; the posted frame lets the mouse messages in between.
    void Pump()
    {
        if (IsDisposed) return;
        Step();
        if (Animating && Visible)
        {
            WaitForFrame();
            BeginInvoke(Pump);
            return;
        }
        pumping = false;
        // The timer polls the hover again, and stops itself when the tab is hidden.
        clock.Start();
    }

    object? lastFrame;

    static double Approach(double value, double target, double step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    // ===== Drawing =====

    // The tab inside the window, in window pixels: left, top and width (the tab goes to the right edge of the window).
    (float X, float Y, float W) TabRect()
    {
        var s = Dpi;
        var info = InfoWidth();
        var w = (Pad + RingD + Pad + (float)(Ease(expand) * (10 + info + 8))) * s;
        var off = (float)(1 - slide) * (w + Shadow * s);
        return (Width - w + off, Shadow * s, w);
    }

    Rectangle TabScreenRect()
    {
        var (x, y, w) = TabRect();
        return new Rectangle(Left + (int)x, Top + (int)y, (int)w, (int)((RingD + 2 * Pad) * Dpi));
    }

    static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    float? infoWidth;
    WaitCounts? infoFor;

    float InfoWidth()
    {
        if (infoWidth is { } cached && infoFor == counts) return cached;
        infoFor = counts;
        infoWidth = MeasureInfo();
        return infoWidth.Value;
    }

    float MeasureInfo()
    {
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        using var bold = new Font("Segoe UI Semibold", 13f * Dpi, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 11.5f * Dpi, GraphicsUnit.Pixel);
        var w = Math.Max(g.MeasureString(Headline, bold).Width, g.MeasureString(Detail, small).Width) / Dpi;
        return Math.Min(InfoMax, w + 4);
    }

    string Headline => counts.Total == 0 ? "Nothing waits" : $"{counts.Total} waiting";

    string Detail => string.Join(" · ", new[] { (counts.Questions, "question"), (counts.Reviews, "review"), (counts.Reports, "report"), (counts.Blocked, "blocked") }
        .Where(p => p.Item1 > 0).Select(p => $"{p.Item1} {p.Item2}{(p.Item1 > 1 && p.Item2 != "blocked" ? "s" : "")}")) is { Length: > 0 } d ? d : "The inbox is clear";

    void Render() => Frame(g =>
    {
        if (slide > 0) Draw(g);
    });

    void Draw(Graphics g)
    {
        var s = Dpi;
        var (x, y, w) = TabRect();
        var h = (RingD + 2 * Pad) * s;
        // The tab: round at the left, flush with the screen edge at the right.
        using (var tab = new GraphicsPath())
        {
            tab.AddArc(x, y, h, h, 90, 180);
            tab.AddLine(x + h / 2, y, Width + h, y);
            tab.AddLine(Width + h, y + h, x + h / 2, y + h);
            tab.CloseFigure();
            SoftShadow(g, tab, s);
            using var fill = new SolidBrush(Surf);
            g.FillPath(fill, tab);
        }
        // The ring of the waiting cards by type, with a gap between the types, and the home ball in the center.
        var d = RingD * s;
        var cx = x + Pad * s + d / 2;
        var cy = y + Pad * s + d / 2;
        var sw = (float)Math.Round(RingD * .1) * s;
        var r = (d - sw) / 2;
        var box = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        using (var track = new Pen(Sunken, sw)) g.DrawEllipse(track, box);
        var spin = spinStart < 0 ? 0 : 360 * SpinEase(Math.Min(1, (time.ElapsedMilliseconds - spinStart) / 800.0));
        var parts = new[] { counts.Questions, counts.Reviews, counts.Reports, counts.Blocked };
        var total = parts.Sum();
        var start = -90.0;
        var gap = total > 1 ? Math.Min(4, sw / s * .4) / (Math.PI * r / s) * 180 : 0;
        for (var i = 0; i < parts.Length && total > 0; i++)
        {
            if (parts[i] == 0) continue;
            var sweep = 360.0 * parts[i] / total;
            using var pen = new Pen(Kinds[i], sw);
            g.DrawArc(pen, box, (float)(start + spin), (float)Math.Max(1, sweep - gap));
            start += sweep;
        }
        Ball(g, cx, cy, (float)Math.Round(RingD * .53) * s);
        // The counts, when the tab extends.
        if (expand > .01)
        {
            var a = (int)(255 * Math.Clamp((expand - .3) / .7, 0, 1));
            using var bold = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 11.5f * s, GraphicsUnit.Pixel);
            using var tx = new SolidBrush(Color.FromArgb(a, Ink));
            using var mu = new SolidBrush(Color.FromArgb(a, Muted));
            var left = x + (Pad + RingD + 10) * s;
            g.DrawString(Headline, bold, tx, left, cy - 18 * s);
            g.DrawString(Detail, small, mu, left, cy + 1 * s);
        }
        if (bubble > .01) Bubble(g, x, cy, s);
    }

    static double SpinEase(double t) => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>N2: the peek bubble comes out of the tab to the left: it grows from its right edge and fades in.</summary>
    void Bubble(Graphics g, float tabX, float cy, float s)
    {
        using var bold = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 11.5f * s, GraphicsUnit.Pixel);
        var textW = Math.Max(g.MeasureString(bubbleTitle, bold).Width, g.MeasureString(bubbleSub, small).Width);
        var full = Math.Min(BubbleMax * s, textW + 28 * s);
        var h = (bubbleSub.Length > 0 ? 46 : 30) * s;
        var part = (float)Ease(bubble);
        var w = full * (.2f + .8f * part);
        var right = tabX - BubbleGap * s;
        var rect = new RectangleF(right - w, cy - h / 2, w, h);
        using var path = Round(rect, 16 * s);
        var a = (int)(255 * part);
        SoftShadow(g, path, s, part);
        using (var fill = new SolidBrush(Color.FromArgb(a, Surf))) g.FillPath(fill, path);
        if (part < .6) return;
        var ta = (int)(255 * (part - .6) / .4);
        using var tx = new SolidBrush(Color.FromArgb(ta, Ink));
        using var mu = new SolidBrush(Color.FromArgb(ta, Muted));
        var state = g.Save();
        g.SetClip(path);
        g.DrawString(bubbleTitle, bold, tx, rect.Left + 14 * s, rect.Top + (bubbleSub.Length > 0 ? 6 : 7) * s);
        if (bubbleSub.Length > 0) g.DrawString(bubbleSub, small, mu, rect.Left + 14 * s, rect.Top + 24 * s);
        g.Restore(state);
    }

    /// <summary>
    /// V5: the home ball is one image, <c>Orchestrator.Web/wwwroot/home-ball.png</c>. The home button of the page shows it, and the
    /// native ball and the edge tab draw it here, so the three are the same pixels. The image is a square of
    /// <see cref="BallBox"/> × the ball size: the ball in the center and its shadow around it.
    /// </summary>
    public static void Ball(Graphics g, float cx, float cy, float d)
    {
        var box = d * BallBox;
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(BallImage.Value!, cx - box / 2, cy - box / 2, box, box);
        g.Restore(state);
    }

    /// <summary>The side of the image square for a ball of size 1: 100 CSS px for the 62 px ball.</summary>
    public const float BallBox = 100f / 62f;

    // V19: one copy for each thread. The edge tab has its own UI thread, and the native ball draws on the widget thread; GDI+
    // refuses one Bitmap in two draws at the same time ("Object is currently in use elsewhere").
    static readonly ThreadLocal<Bitmap> BallImage = new(() =>
    {
        using var stream = typeof(EdgeTab).Assembly.GetManifestResourceStream("home-ball.png") ?? throw new InvalidOperationException("The resource home-ball.png is missing.");
        return new Bitmap(stream);
    });

    /// <summary>Writes the image of the home ball (the source of <see cref="Ball"/> and of the page button) at 3× scale.</summary>
    public static void WriteBall(string png)
    {
        const int scale = 3;
        var size = (int)Math.Round(100f * scale);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawBall(g, size / 2f, size / 2f, 62f * scale);
        }
        bitmap.Save(png, ImageFormat.Png);
    }

    /// <summary>The drawing of the image of the home ball. Only <see cref="WriteBall"/> uses it.</summary>
    static void DrawBall(Graphics g, float cx, float cy, float d)
    {
        var rect = new RectangleF(cx - d / 2, cy - d / 2, d, d);
        // The drop shadow under the ball: 14 thin steps to 12 % of the ball outside it, so the edge fades with no bands.
        for (var i = 14; i >= 1; i--)
        {
            using var sh = new SolidBrush(Color.FromArgb(5, 120, 45, 5));
            var k = i * d * .12f / 14;
            g.FillEllipse(sh, rect.X - k, rect.Y - k + d * .08f, d + 2 * k, d + 2 * k);
        }
        using (var fill = new LinearGradientBrush(rect, Mix(Accent, Color.White, .08), Mix(Accent, Color.Black, .06), LinearGradientMode.Vertical))
            g.FillEllipse(fill, rect);
        using (var hi = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1, d * .03f)))
            g.DrawArc(hi, rect.X + d * .08f, rect.Y + d * .04f, d * .84f, d * .84f, 200, 140);
        // The home icon (viewBox 24): M5 12H3l9-9 9 9h-2 and M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7, 46 % of the ball.
        var size = d * .46f;
        var u = size / 24f;
        var ox = cx - size / 2;
        var oy = cy - size / 2;
        PointF P(float px, float py) => new(ox + px * u, oy + py * u);
        using var pen = new Pen(Ink, 1.8f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, [P(5, 12), P(3, 12), P(12, 3), P(21, 12), P(19, 12)]);
        using var body = new GraphicsPath();
        body.AddLine(P(5, 12), P(5, 19));
        body.AddArc(ox + 5 * u, oy + 17 * u, 4 * u, 4 * u, 180, -90);
        body.AddLine(P(7, 21), P(17, 21));
        body.AddArc(ox + 15 * u, oy + 17 * u, 4 * u, 4 * u, 90, -90);
        body.AddLine(P(19, 19), P(19, 12));
        g.DrawPath(pen, body);
    }

    static Color Mix(Color a, Color b, double k) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));

    // ===== The saved place (only the top: the tab is always at the right edge) =====

    int? LoadTop()
    {
        try
        {
            return placeFile is not null && File.Exists(placeFile) && int.TryParse(File.ReadAllText(placeFile).Trim(), out var top) ? top : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    void SaveTop(int top)
    {
        if (placeFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(placeFile)!);
            File.WriteAllText(placeFile, top.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The file of the saved top, or null.</summary>
    public string? PlaceFile => placeFile;

    /// <summary>The saved top of the tab, or null.</summary>
    public int? SavedTop => LoadTop();

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) Place();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) clock.Dispose();
        base.Dispose(disposing);
    }
}
