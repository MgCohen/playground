using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The tray icon and the tray window. A left click on the icon opens the panel, or hides the tray when the panel is open.
/// Right click: the menu (Show, Pin on top, Exit). Exit closes the window and the icon. The stage of the views (TrayStage) does
/// the phases, the motion, the badge and its notices; this class only sends the requests of the user.
/// </summary>
public sealed partial class TrayApp : ApplicationContext
{
    static readonly Color Accent = Color.FromArgb(0xff, 0x6a, 0x1a);

    readonly NotifyIcon icon;
    readonly Icon dot;
    bool exiting;

    public TrayApp(IServiceProvider services, string label, bool show, bool pinned, string? placeFile)
    {
        dot = Dot(Accent);
        Window = new TrayWindow(services, dot, pinned, placeFile);
        var contract = services.GetRequiredService<WidgetWindow>();
        contract.OpenAtStart = show;
        contract.Window = Window;
        var pin = new ToolStripMenuItem("Pin on top") { CheckOnClick = true, Checked = pinned };
        pin.CheckedChanged += (_, _) => Window.Pinned = pin.Checked;
        Window.PinnedChanged += () => pin.Checked = Window.Pinned;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Window.Request(TrayRequest.Open));
        menu.Items.Add(pin);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        icon = new NotifyIcon { Icon = dot, Text = Tip($"Orchestrator ({label})"), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Window.Request(Window.Phase == TrayPhase.Open ? TrayRequest.Hide : TrayRequest.Open);
        };
        Store = services.GetRequiredService<Orchestrator.Store.FileStore>();
        // The window handle exists from here, so the page starts and a second start of the app can marshal to the UI thread.
        _ = Window.Handle;
    }

    public TrayWindow Window { get; }

    /// <summary>The store that the tray shows. The self-test writes to it.</summary>
    public Orchestrator.Store.FileStore Store { get; }

    /// <summary>Opens the panel from any thread. A second start of the app calls it.</summary>
    public void ShowFromAnyThread()
    {
        if (exiting || Window.IsDisposed) return;
        Window.BeginInvoke(() => Window.Request(TrayRequest.Open));
    }

    public void Exit()
    {
        if (exiting) return;
        exiting = true;
        Window.Exiting = true;
        Window.Close();
        icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon.Dispose();
            Window.Dispose();
            dot.Dispose();
        }
        base.Dispose(disposing);
    }

    // The tray tip has a maximum of 127 characters.
    static string Tip(string text) => text.Length <= 127 ? text : text[..124] + "...";

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
