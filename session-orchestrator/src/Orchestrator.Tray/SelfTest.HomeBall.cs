namespace Orchestrator.Tray;

/// <summary>
/// V5: the home ball before the page (the native ball window) and the home button of the page after the handoff are one
/// look. The check copies the same screen square at both times and compares the pixels inside the ball circle.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> HomeBall(WidgetForm form, string folder)
    {
        form.HideWidget();
        await Settle(form);
        // A fixed background behind the ball, so the copy shows no other window.
        var square0 = Rectangle.Inflate(new Rectangle(form.Location, form.Size), 40, 40);
        using var backdrop = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            TopMost = true, BackColor = Color.FromArgb(0xe9, 0xe6, 0xe1), Bounds = square0,
        };
        backdrop.Show();
        await Task.Delay(300);
        form.ShowWidget();
        // The pop in ends at InMs; the ball stays until the end of the grow (gap 60 ms, grow 280 ms).
        await Task.Delay(BallWindow.InMs + 40);
        var square = form.Ball.Bounds;
        using var native = await Task.Run(() => Copy(square));
        await Settle(form);
        await Task.Delay(500);
        using var page = await Task.Run(() => Copy(square));
        var (diff, ratio) = BallDiff(native, page, form.DeviceDpi / 96f * 62);
        var png = Path.Combine(folder, "tray-v5-ball.png");
        Zoom([native, page], png, 3);
        // V15: each frame of the handover shows a ball. One pixel inside the orange area, sampled on a pool thread.
        var spot = new Point(square.Left + square.Width / 2 - (int)(form.DeviceDpi / 96f * 62 * .3f), square.Top + square.Height / 2);
        static bool Orange(Color c) => c.R > 200 && c.G is > 70 and < 150 && c.B < 90;
        async Task<(int Samples, int Missing)> Watch(int ms)
        {
            return await Task.Run(() =>
            {
                var end = Environment.TickCount64 + ms;
                int n = 0, miss = 0;
                using var one = new Bitmap(1, 1);
                using var g = Graphics.FromImage(one);
                while (Environment.TickCount64 < end)
                {
                    g.CopyFromScreen(spot, Point.Empty, new Size(1, 1));
                    n++;
                    if (!Orange(one.GetPixel(0, 0))) miss++;
                    Thread.Sleep(2);
                }
                return (n, miss);
            });
        }
        form.HideWidget();
        await Settle(form);
        await Task.Delay(300);
        form.ShowWidget();
        // The handover comes after the pop in, the gap and the grow; watch from before its start to after its end.
        await Task.Delay(BallWindow.InMs + WidgetForm.GapMs + WidgetForm.GrowMs - 80);
        var show = await Watch(400);
        await Settle(form);
        await Task.Delay(400);
        // The hide: the ball pops out, so watch only the first frames, while the ball is still big.
        var hideWatch = Watch(70);
        form.HideWidget();
        var hide = await hideWatch;
        await Settle(form);
        form.ShowWidget();
        await Settle(form);
        backdrop.Hide();
        var handover = new Check("v15-handover", show.Missing == 0 && hide.Missing == 0,
            $"show: {show.Missing} of {show.Samples} samples with no ball; hide: {hide.Missing} of {hide.Samples} (expect 0)", "");
        return [handover, new Check("v5-one-ball", diff <= 3 && ratio <= .02,
            $"mean color difference {diff:0.0} inside the ball circle (expect 3 or less); {ratio:P1} of the pixels differ by more than 24 (expect 2 % or less)", png)];
    }

    /// <summary>The mean channel difference and the part of the pixels with a big difference, inside a circle of 90 % of the ball.</summary>
    static (double Mean, double Big) BallDiff(Bitmap a, Bitmap b, float ball)
    {
        var cx = a.Width / 2f;
        var cy = a.Height / 2f;
        var r = ball / 2 * .9f;
        double sum = 0;
        int n = 0, big = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                var d = (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B)) / 3.0;
                sum += d;
                n++;
                if (d > 24) big++;
            }
        return n == 0 ? (0, 0) : (sum / n, big / (double)n);
    }

    /// <summary>The copies side by side, each scaled by <paramref name="k"/> with no smoothing.</summary>
    static void Zoom(Bitmap[] frames, string png, int k)
    {
        var gap = 8;
        using var all = new Bitmap(frames.Sum(f => f.Width * k + gap) - gap, frames.Max(f => f.Height * k));
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.FromArgb(0x80, 0x80, 0x80));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            var x = 0;
            foreach (var f in frames)
            {
                g.DrawImage(f, new Rectangle(x, 0, f.Width * k, f.Height * k));
                x += f.Width * k + gap;
            }
        }
        all.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }
}
