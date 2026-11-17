using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.Tray;

/// <summary>
/// The tray icon and the widget. A left click on the icon shows or hides the widget. Right click: the menu
/// (Show, Pin on top, Exit). Exit closes the widget and the icon.
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
        Form = new WidgetForm(services, dot, pinned, placeFile);
        services.GetRequiredService<WidgetWindow>().Form = Form;
        var pin = new ToolStripMenuItem("Pin on top") { CheckOnClick = true, Checked = pinned };
        pin.CheckedChanged += (_, _) => Form.Pinned = pin.Checked;
        Form.PinnedChanged += () => pin.Checked = Form.Pinned;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Form.ShowWidget());
        menu.Items.Add(pin);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        icon = new NotifyIcon { Icon = dot, Text = Tip($"Orchestrator ({label})"), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Form.Toggle();
        };
        if (show) Form.ShowWidget();
        // The window handle exists, so a second start can marshal to the UI thread.
        else _ = Form.Handle;
    }

    public WidgetForm Form { get; }

    /// <summary>Shows the widget from any thread. A second start of the app calls it.</summary>
    public void ShowFromAnyThread()
    {
        if (exiting || Form.IsDisposed) return;
        Form.BeginInvoke(Form.ShowWidget);
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
