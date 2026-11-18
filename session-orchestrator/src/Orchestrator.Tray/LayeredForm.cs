using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>
/// A top window with per-pixel alpha (a layered window with UpdateLayeredWindow). The subclass draws each frame into a
/// 32-bit bitmap with GDI+: soft edges and any shape, and the clear parts let the clicks through. It never takes the focus.
/// The edge tab (N1), the home ball (G1) and the grow shadow (G1) use it.
/// </summary>
public abstract partial class LayeredForm : Form
{
    protected LayeredForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate | WsExTopmost;
            return p;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>The DPI scale of the window.</summary>
    protected float Dpi => DeviceDpi / 96f;

    /// <summary>Draws one frame with <paramref name="draw"/> and puts it on the screen at the window place.</summary>
    protected void Frame(Action<Graphics> draw)
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            draw(g);
        }
        Blit(bitmap);
    }

    void Blit(Bitmap bitmap)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memory, handle);
        try
        {
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var top = new NativePoint(Left, Top);
            var blend = new BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            _ = UpdateLayeredWindow(Handle, screen, ref top, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            _ = SelectObject(memory, old);
            _ = DeleteObject(handle);
            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    /// <summary>Puts this window directly below <paramref name="above"/> in the z-order, with no move and no activation.</summary>
    protected void Below(Form above) => _ = SetWindowPos(Handle, above.Handle, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    /// <summary>Puts this window on top of the other top windows, with no move and no activation.</summary>
    public void ToTop() => _ = SetWindowPos(Handle, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    /// <summary>A rounded rectangle path.</summary>
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Max(1, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>A soft shadow below a shape: rings of a low alpha that get wider, moved down a little.</summary>
    public static void SoftShadow(Graphics g, GraphicsPath shape, float s, float strength = 1, int rings = 8, float down = 4)
    {
        for (var i = rings; i >= 1; i--)
        {
            using var copy = (GraphicsPath)shape.Clone();
            using var m = new Matrix();
            m.Translate(0, down * s);
            copy.Transform(m);
            using var pen = new Pen(Color.FromArgb((int)(9 * strength), 60, 40, 20), i * 2 * s) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, copy);
        }
    }

    const int WsExLayered = 0x80000;
    const int WsExToolWindow = 0x80;
    const int WsExNoActivate = 0x8000000;
    const int WsExTopmost = 0x8;
    const int UlwAlpha = 2;
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize(int cx, int cy)
    {
        public int Cx = cx;
        public int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BlendFunction
    {
        public byte Op;
        public byte Flags;
        public byte Alpha;
        public byte Format;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref NativePoint pptDst, ref NativeSize psize, nint hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
