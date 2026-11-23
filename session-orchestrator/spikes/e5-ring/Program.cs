using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

// Spike E-5: can the tray widget be a ring with a transparent hole, with soft edges, and keep its web content?
// The probe hosts WebView2 with a composition controller (no child window) on a top window with no redirection bitmap,
// through DirectComposition. The page draws a ring with soft edges on a transparent background. A striped window of the
// same process stands behind it. The probe writes screenshots and result.json to the folder of its first argument.
// Mode 1: no window region. Mode 2: a ring-shaped window region, for clicks through the hole.

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var folder = Path.GetFullPath(args.Length > 0 ? args[0] : "e5-out");
        Directory.CreateDirectory(folder);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var results = new Dictionary<string, object>();
        var back = new BackForm();
        var ring = new RingForm();
        back.Shown += async (_, _) =>
        {
            try
            {
                ring.Show();
                await ring.Ready.Task;
                await Task.Delay(1500);
                foreach (var mode in new[] { "alpha", "region" })
                {
                    if (mode == "region") ring.UseRegion();
                    await Task.Delay(700);
                    var r = ring.Bounds;
                    var s = ring.DeviceDpi / 96f;
                    var center = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                    using var shot = Probe.Shot(Rectangle.Inflate(r, (int)(20 * s), (int)(20 * s)));
                    shot.Save(Path.Combine(folder, $"e5-ring-{mode}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    // The pixels: the hole center, the ring body, and the outer edge of the ring (a soft edge has mixed values).
                    var o = (int)(20 * s);
                    Color At(int dx, int dy) => shot.GetPixel(o + r.Width / 2 + dx, o + r.Height / 2 + dy);
                    var hole = At(0, 0);
                    var body = At((int)(77 * s), 0);
                    var edge = Enumerable.Range((int)(94 * s), (int)(10 * s)).Select(dx => At(dx, 0)).ToList();
                    var behind = Probe.WindowFromPoint(new Probe.Pt(center.X, center.Y));
                    back.Clicks = 0;
                    Probe.Click(center);
                    await Task.Delay(400);
                    var throughHole = back.Clicks > 0;
                    // The click in the hole activates the back window, which then covers the ring: put the ring on top again.
                    ring.BringToFront();
                    ring.TopMost = false;
                    ring.TopMost = true;
                    await Task.Delay(300);
                    var webClicks0 = await ring.WebClicks();
                    Probe.Click(new Point(center.X + (int)(77 * s), center.Y));
                    await Task.Delay(500);
                    var webClicks = await ring.WebClicks() - webClicks0;
                    results[mode] = new
                    {
                        hole = $"#{hole.R:x2}{hole.G:x2}{hole.B:x2}",
                        body = $"#{body.R:x2}{body.G:x2}{body.B:x2}",
                        edge = edge.Select(c => $"#{c.R:x2}{c.G:x2}{c.B:x2}").ToList(),
                        windowAtHole = behind == back.Handle ? "back window" : behind == ring.Handle ? "ring window" : $"other {behind}",
                        clickInHoleReachesBack = throughHole,
                        clickOnRingReachesPage = webClicks > 0,
                    };
                }
            }
            catch (Exception e)
            {
                results["error"] = e.ToString();
            }
            File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Application.Exit();
        };
        Application.Run(back);
    }
}

/// <summary>A striped window of the same process behind the ring. It counts the clicks that reach it.</summary>
sealed class BackForm : Form
{
    public int Clicks;

    public BackForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(200, 200, (int)(420 * DeviceDpi / 96f), (int)(420 * DeviceDpi / 96f));
        TopMost = true;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var w = (int)(12 * DeviceDpi / 96f);
        for (var x = 0; x < Width; x += 2 * w)
        {
            e.Graphics.FillRectangle(Brushes.MediumSeaGreen, x, 0, w, Height);
            e.Graphics.FillRectangle(Brushes.MidnightBlue, x + w, 0, w, Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Clicks++;
    }
}

/// <summary>The ring: a top window with no redirection bitmap, WebView2 in composition mode on a DirectComposition visual.</summary>
sealed class RingForm : Form
{
    public readonly TaskCompletionSource Ready = new();
    CoreWebView2CompositionController? controller;
    nint device, target, visual;

    const string Html = """
        <!doctype html><html><head><style>
        html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
        .ring { position: absolute; inset: 10px; border-radius: 50%;
          background: conic-gradient(#ff6a1a 0 .45turn, #6f9cff 0 .6turn, #8fd94f 0 .85turn, #ff5c5c 0);
          -webkit-mask: radial-gradient(circle closest-side, transparent 54%, #000 56%, #000 97%, transparent 100%);
          mask: radial-gradient(circle closest-side, transparent 54%, #000 56%, #000 97%, transparent 100%); }
        </style></head><body><div class="ring" onclick="window.n = (window.n || 0) + 1"></div></body></html>
        """;

    public RingForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP: DirectComposition draws the window, with per-pixel alpha.
            return p;
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var s = DeviceDpi / 96f;
        Bounds = new Rectangle(200 + (int)(100 * s), 200 + (int)(100 * s), (int)(220 * s), (int)(220 * s));
        device = Probe.CreateDevice();
        target = Probe.CreateTarget(device, Handle);
        visual = Probe.CreateVisual(device);
        Probe.SetRoot(target, visual);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "e5-ring-webview"));
        controller = await env.CreateCoreWebView2CompositionControllerAsync(Handle);
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        controller.RootVisualTarget = Marshal.GetObjectForIUnknown(visual);
        controller.IsVisible = true;
        Probe.Commit(device);
        controller.CoreWebView2.NavigationCompleted += (_, _) => Ready.TrySetResult();
        controller.CoreWebView2.NavigateToString(Html);
    }

    /// <summary>Mode 2: a ring-shaped window region (the outer circle less the hole), so a click in the hole goes to the window behind.</summary>
    public void UseRegion()
    {
        var s = DeviceDpi / 96f;
        var m = (int)(10 * s);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var outer = new Rectangle(m, m, Width - 2 * m, Height - 2 * m);
        path.AddEllipse(outer);
        var inner = Rectangle.Inflate(outer, -(int)(outer.Width * .22), -(int)(outer.Height * .22));
        path.AddEllipse(inner);
        Region = new Region(path);
    }

    public async Task<int> WebClicks() =>
        controller is null ? 0 : JsonSerializer.Deserialize<int>(await controller.CoreWebView2.ExecuteScriptAsync("window.n || 0"));

    // The composition controller gets no input by itself: the host sends the mouse input.
    protected override void WndProc(ref Message m)
    {
        const int WmMouseMove = 0x200, WmLButtonDown = 0x201, WmLButtonUp = 0x202;
        if (controller is not null && m.Msg is WmMouseMove or WmLButtonDown or WmLButtonUp)
        {
            var kind = m.Msg switch
            {
                WmLButtonDown => CoreWebView2MouseEventKind.LeftButtonDown,
                WmLButtonUp => CoreWebView2MouseEventKind.LeftButtonUp,
                _ => CoreWebView2MouseEventKind.Move,
            };
            var p = new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
            controller.SendMouseInput(kind, CoreWebView2MouseEventVirtualKeys.None, 0, p);
        }
        base.WndProc(ref m);
    }
}

static unsafe partial class Probe
{
    static readonly Guid DesktopDevice = new("5f4633fe-1e08-4cb8-8c75-ce24333f5602");

    [LibraryImport("dcomp.dll")]
    private static partial int DCompositionCreateDevice2(nint renderingDevice, in Guid iid, out nint device);

    static nint* Vtbl(nint obj) => *(nint**)obj;

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public static nint CreateDevice()
    {
        Check(DCompositionCreateDevice2(0, DesktopDevice, out var device));
        return device;
    }

    // IDCompositionDesktopDevice: IUnknown (3), IDCompositionDevice2 (21: Commit = 3, CreateVisual = 6), then CreateTargetForHwnd = 24.
    public static void Commit(nint device) => Check(((delegate* unmanaged[Stdcall]<nint, int>)Vtbl(device)[3])(device));

    public static nint CreateVisual(nint device)
    {
        nint visual;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(device)[6])(device, &visual));
        return visual;
    }

    public static nint CreateTarget(nint device, nint hwnd)
    {
        nint target;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Vtbl(device)[24])(device, hwnd, 1, &target));
        return target;
    }

    // IDCompositionTarget: SetRoot = 3.
    public static void SetRoot(nint target, nint visual) => Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Vtbl(target)[3])(target, visual));

    public static Bitmap Shot(Rectangle area)
    {
        var b = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(b);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        return b;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Pt(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Pt point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extra);

    public static void Click(Point at)
    {
        SetCursorPos(at.X, at.Y);
        Thread.Sleep(80);
        mouse_event(0x2, 0, 0, 0, 0);
        Thread.Sleep(40);
        mouse_event(0x4, 0, 0, 0, 0);
    }
}
