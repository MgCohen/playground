using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>
/// V20: the shadow window follows the state of the widget. With the pin off, another window that takes the focus is above
/// the shadow; a minimize hides the shadow and a restore shows it; a hide hides it.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> ShadowLife(WidgetForm form)
    {
        var pinned = form.Pinned;
        if (!form.Visible) form.ShowWidget();
        await Settle(form);
        await Task.Delay(300);
        var scale = form.DeviceDpi / 96f;
        // A point in the shadow, left of the widget.
        var spot = new Point(form.Left - (int)(8 * scale), form.Top + form.Height / 2);

        // 1. Pin off, then a normal window over the widget takes the focus.
        form.Pinned = false;
        using var other = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            BackColor = Color.FromArgb(0x40, 0x70, 0xa0), Bounds = Rectangle.Inflate(new Rectangle(form.Location, form.Size), 40, 40),
        };
        other.Show();
        other.Activate();
        await Task.Delay(400);
        var top = WindowFromPoint((uint)spot.X | ((long)spot.Y << 32));
        var covered = top == other.Handle;
        other.Hide();
        form.Pinned = pinned;
        form.ShowWidget();
        await Settle(form);

        // 2. A minimize of the system, then a restore.
        form.WindowState = FormWindowState.Minimized;
        await Task.Delay(300);
        var minimized = !form.GrowShadow.Visible;
        form.WindowState = FormWindowState.Normal;
        await Task.Delay(300);
        var restored = form.GrowShadow.Visible;

        // 3. A hide.
        form.HideWidget();
        await Settle(form);
        var hidden = !form.GrowShadow.Visible;
        form.ShowWidget();
        await Settle(form);
        return [new Check("v20-shadow-life", covered && minimized && restored && hidden,
            $"pin off and another window in front: that window is above the shadow {covered} (window at the point {top}, other {other.Handle}, shadow {form.GrowShadow.Handle}); minimize hides the shadow {minimized}; restore shows it {restored}; hide hides it {hidden}", "")];
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(long point);
}
