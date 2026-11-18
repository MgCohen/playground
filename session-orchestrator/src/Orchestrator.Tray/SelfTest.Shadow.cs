namespace Orchestrator.Tray;

/// <summary>
/// V10: the widget has one shadow. The shadow below the bottom edge (the fixed edge of the grow and the shrink) is the same at
/// rest and at the start of the hide, and the corners stay round with no shadow of the system.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Shadow(WidgetForm form, string folder)
    {
        // The pin keeps the widget above the backdrop, which is on top of the other windows.
        var pinned = form.Pinned;
        form.Pinned = true;
        form.HideWidget();
        await Settle(form);
        var area = Rectangle.Inflate(new Rectangle(form.Location, form.Size), 60, 60);
        using var backdrop = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            TopMost = true, BackColor = Color.FromArgb(0xe9, 0xe6, 0xe1), Bounds = area,
        };
        backdrop.Show();
        await Task.Delay(300);
        form.ShowWidget();
        await Settle(form);
        await Task.Delay(500);
        var scale = form.DeviceDpi / 96f;
        // A column below the bottom edge, at a quarter of the width (away from the home ball), and the square of the top left corner.
        var column = new Rectangle(form.Left + form.Width / 4, form.Bottom, 1, (int)(26 * scale));
        var corner = new Rectangle(form.Left, form.Top, (int)(6 * scale), (int)(6 * scale));
        using var rest = await Task.Run(() => Copy(column));
        using var restCorner = await Task.Run(() => Copy(corner));
        using var page = await Task.Run(() => Copy(new Rectangle(form.Left + form.Width / 4, form.Top + (int)(2 * scale), 1, 1)));
        using var whole = await Task.Run(() => Copy(area));
        // The hide: the ball pops out first, then the shrink starts at its bottom edge. A copy at the start of the shrink.
        form.HideWidget();
        await Task.Delay(BallWindow.OutMs / 2 + 30);
        using var hiding = await Task.Run(() => Copy(column));
        using var hidingWhole = await Task.Run(() => Copy(area));
        await Settle(form);
        form.ShowWidget();
        await Settle(form);
        backdrop.Hide();
        form.Pinned = pinned;
        double diff = 0;
        for (var y = 0; y < rest.Height; y++)
        {
            var a = rest.GetPixel(0, y);
            var b = hiding.GetPixel(0, y);
            diff = Math.Max(diff, (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3.0);
        }
        // A round corner shows the shadow in its outer pixel; a square corner shows the page there.
        var c = restCorner.GetPixel(0, 0);
        var p = page.GetPixel(0, 0);
        var round = Math.Abs(c.R - p.R) + Math.Abs(c.G - p.G) + Math.Abs(c.B - p.B) > 12;
        var png = Path.Combine(folder, "tray-v10-shadow.png");
        Strip([whole, hidingWhole], png);
        return [new Check("v10-one-shadow", diff <= 6 && round,
            $"largest difference of the shadow below the bottom edge, rest against hide: {diff:0.0} (expect 6 or less); top left corner pixel {c.R},{c.G},{c.B} against the page {p.R},{p.G},{p.B}, round {round}", png)];
    }
}
