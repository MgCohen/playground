using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>The tray icon and its menu (Open, Exit). A left click opens or hides the window.</summary>
public sealed partial class TrayApp : ApplicationContext
{
    static readonly Color Accent = Color.FromArgb(0x2f, 0x7d, 0x9c);

    readonly NotifyIcon icon;
    readonly Icon dot;
    bool exiting;

    public TrayApp(IServiceProvider services, bool show)
    {
        dot = Dot(Accent);
        Form = new MainForm(services, dot);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Form.ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        icon = new NotifyIcon { Icon = dot, Text = "Orchestrator (mock data)", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Form.Visible) Form.Hide();
            else Form.ShowWindow();
        };
        if (show) Form.ShowWindow();
        else _ = Form.Handle;
    }

    public MainForm Form { get; }

    /// <summary>Shows the window from any thread. A second start of the app calls it.</summary>
    public void ShowFromAnyThread()
    {
        if (exiting || Form.IsDisposed) return;
        Form.BeginInvoke(Form.ShowWindow);
    }

    public void Exit()
    {
        if (exiting) return;
        exiting = true;
        Form.Exiting = true;
        Form.Close();
        icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon.Dispose();
            Form.Dispose();
            dot.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>A 16 × 16 dot of one color with a white ring.</summary>
    static Icon Dot(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 0, 0, 15, 15);
            g.FillEllipse(fill, 2, 2, 11, 11);
        }
        var handle = bitmap.GetHicon();
        using var temp = System.Drawing.Icon.FromHandle(handle);
        var copy = (Icon)temp.Clone();
        DestroyIcon(handle);
        return copy;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
