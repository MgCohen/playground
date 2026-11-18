using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>The E2 checks of the minimized form (N1, the edge tab) and the notification (N2, the peek bubble).</summary>
public static partial class SelfTest
{
    static async Task Idle(EdgeTab tab)
    {
        for (var i = 0; i < 100 && tab.Moving; i++) await Task.Delay(20);
    }

    /// <summary>Copies the right edge of the screen around the edge tab.</summary>
    static string EdgeShot(EdgeTab tab, string folder, string name)
    {
        var screen = Screen.FromControl(tab).Bounds;
        var margin = (int)(30 * tab.DeviceDpi / 96f);
        var area = Rectangle.Intersect(screen, Rectangle.FromLTRB(tab.Left, tab.Top - margin, screen.Right, tab.Bottom + margin));
        using var bitmap = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        var png = Path.Combine(folder, $"tray-e2-{name}.png");
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return png;
    }

    static async Task<IEnumerable<Check>> Edge(TrayApp tray, CoreWebView2 web, string folder)
    {
        var form = tray.Form;
        var tab = tray.Tab;
        var checks = new List<Check>();
        var scale = form.DeviceDpi / 96f;
        var back = Cursor.Position;
        Cursor.Position = new Point(form.Left + form.Width / 2, form.Top + 200);

        // N1: minimize (T4) shrinks the widget, then the edge tab slides in from the right. The ring is 6 px from the screen edge.
        await Home(web);
        await web.ExecuteScriptAsync("document.querySelector('.hd.win .wbtn.min').click()");
        for (var i = 0; i < 100 && (form.Visible || !tab.OnScreen); i++) await Task.Delay(20);
        await Task.Delay(120);
        var sliding = tab.Slide < 1;
        await Idle(tab);
        var area = Screen.FromControl(tab).WorkingArea;
        var gap = area.Right - tab.RingBounds.Right;
        var minPng = EdgeShot(tab, folder, "n1-edge-tab");
        checks.Add(new Check("n1-minimize", !form.Visible && tab.Visible && tab.Slide >= 1 && Math.Abs(gap - (int)(6 * scale)) <= 1,
            $"widget hidden {!form.Visible}; tab shown {tab.Visible}, slides in {sliding}; ring {tab.RingBounds}, gap to the screen edge {gap} px (expect {(int)(6 * scale)})", minPng));

        // N1: a hover of 350 ms extends the tab to the left with the counts. A short pass does not.
        var ring = tab.RingBounds;
        var center = new Point(ring.Left + ring.Width / 2, ring.Top + ring.Height / 2);
        Mouse.MoveTo(center);
        await Task.Delay(150);
        var early = tab.Expanded;
        await Task.Delay(600);
        await Idle(tab);
        var hoverPng = EdgeShot(tab, folder, "n1-hover");
        var expanded = tab.Expanded;
        Mouse.MoveTo(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2));
        await Task.Delay(500);
        await Idle(tab);
        checks.Add(new Check("n1-hover", !early && expanded && !tab.Expanded, $"extended at 150 ms {early}, after the hover {expanded}, after the pointer leaves {tab.Expanded}", hoverPng));

        // N2: a new waiting card while the widget is hidden: the ring spins and the peek bubble comes out of the tab; it goes after 5 s.
        var now = tray.Counts();
        var more = now.Counts with { Questions = now.Counts.Questions + 1 };
        tray.Notify((more, "Question · Pattern engine spike", "Which sample set do you want?"));
        await Task.Delay(400);
        var peekPng = EdgeShot(tab, folder, "n2-peek");
        var peeking = tab.Peeking;
        await Task.Delay(EdgeTab.PeekMs + 400);
        await Idle(tab);
        checks.Add(new Check("n2-peek", peeking && !tab.Peeking, $"bubble at 400 ms {peeking}; after {EdgeTab.PeekMs} ms {tab.Peeking}", peekPng));
        tab.SetCounts(now.Counts);

        // N1: a drag moves the tab up and down only; the place is saved.
        ring = tab.RingBounds;
        center = new Point(ring.Left + ring.Width / 2, ring.Top + ring.Height / 2);
        var (left, top) = (tab.Left, tab.Top);
        var by = -(int)(120 * scale);
        await Task.Run(() => Mouse.Drag(center, center + new Size(-(int)(80 * scale), by)));
        await Task.Delay(300);
        await Idle(tab);
        var dragPng = EdgeShot(tab, folder, "n1-drag");
        checks.Add(new Check("n1-drag", tab.Left == left && tab.Top == top + by && tab.SavedTop == tab.Top && !form.Visible,
            $"from {left},{top} to {tab.Left},{tab.Top} (expect {left},{top + by}); saved top {tab.SavedTop}; widget still hidden {!form.Visible}", dragPng));

        // N1: a click opens the widget: the tab slides out to the right, then the widget grows.
        ring = tab.RingBounds;
        await Task.Run(() => Mouse.Click(new Point(ring.Left + ring.Width / 2, ring.Top + ring.Height / 2)));
        for (var i = 0; i < 100 && (!form.Visible || form.Growing); i++) await Task.Delay(20);
        await Task.Delay(300);
        checks.Add(new Check("n1-open", form.Visible && !tab.Visible, $"widget shown {form.Visible}; tab hidden {!tab.Visible}", ""));
        Cursor.Position = back;
        return checks;
    }
}
