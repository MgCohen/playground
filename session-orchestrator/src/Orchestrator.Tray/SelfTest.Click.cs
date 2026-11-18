using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>
/// V21: one real click on the header of the widget at rest. After the click the widget must still be the window at its center,
/// and its pixels must still show the page, not the canvas fill of the shadow window.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Click(WidgetForm form, string folder)
    {
        var checks = new List<Check>();
        if (!form.Visible) form.ShowWidget();
        await Settle(form);
        await Task.Delay(500);
        var scale = form.DeviceDpi / 96f;
        var center = new Point(form.Left + form.Width / 2, form.Top + form.Height / 3);
        string Order()
        {
            var at = WindowFromPoint((uint)center.X | ((long)center.Y << 32));
            var root = GetAncestor(at, 2);
            if (root == form.Handle) return "widget";
            if (root == form.GrowShadow.Handle) return "shadow";
            var title = new char[80];
            var n = GetWindowTextW(root, title, 80);
            _ = GetWindowThreadProcessId(root, out var pid);
            return $"other \"{new string(title, 0, Math.Max(0, n))}\" (pid {pid}, this pid {Environment.ProcessId})";
        }
        var before = Order();
        using var shotBefore = await Task.Run(() => Copy(new Rectangle(form.Location, form.Size)));
        var back = Cursor.Position;
        // The title area of the header, away from the window buttons.
        await Task.Run(() => Mouse.Click(new Point(form.Left + (int)(60 * scale), form.Top + (int)(30 * scale))));
        await Task.Delay(800);
        var afterClick = Order();
        // A short real drag of the header, as the user did.
        var from = new Point(form.Left + (int)(60 * scale), form.Top + (int)(30 * scale));
        await Task.Run(() => Mouse.Drag(from, from + new Size((int)(40 * scale), (int)(30 * scale))));
        await Task.Delay(800);
        center = new Point(form.Left + form.Width / 2, form.Top + form.Height / 3);
        var after = Order();
        using var shotAfter = await Task.Run(() => Copy(new Rectangle(form.Location, form.Size)));
        Cursor.Position = back;
        var png = Path.Combine(folder, "tray-v21-click.png");
        Strip([shotBefore, shotAfter], png);
        // A page shows many colors; the canvas fill of the shadow window is one color.
        static int Colors(Bitmap b)
        {
            var set = new HashSet<int>();
            for (var y = 0; y < b.Height; y += 9)
                for (var x = 0; x < b.Width; x += 9)
                    set.Add(b.GetPixel(x, y).ToArgb());
            return set.Count;
        }
        var colorsBefore = Colors(shotBefore);
        var colorsAfter = Colors(shotAfter);
        // V21: the shadow window to the top of all windows on purpose. The widget is its owned window, so it stays in front.
        form.GrowShadow.ToTop();
        await Task.Delay(400);
        var forced = Order();
        using var shotForced = await Task.Run(() => Copy(new Rectangle(form.Location, form.Size)));
        var colorsForced = Colors(shotForced);
        checks.Add(new Check("v21-shadow-raised", forced == "widget" && colorsForced > colorsBefore / 3,
            $"shadow window raised to the top: window at the widget center {forced} (expect widget); page colors {colorsForced} (before {colorsBefore})", ""));
        Faults.Trace($"click check: before {before}, after {after}, colors {colorsBefore} -> {colorsAfter}, visible {form.Visible}, shadow {form.GrowShadow.Visible}");
        return [.. checks, new Check("v21-header-click", afterClick == "widget" && after == "widget" && colorsAfter > colorsBefore / 3,
            $"window at the widget center before the click: {before}, after the click: {afterClick}, after a drag: {after} (expect widget); page colors before {colorsBefore}, after {colorsAfter}", png)];
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(nint hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
