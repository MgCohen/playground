namespace Orchestrator.Tray;

/// <summary>
/// V4: one form at a time. A notice that comes during a grow, a minimize or a hide never leaves the widget and the edge tab
/// on the screen together. A pool thread samples both forms each 10 ms; a sample where both show and neither moves fails.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The test places: the edge tab high on the right edge, the widget at the top left. Other trays use the middle.</summary>
    public static void TestPlaces(TrayApp tray)
    {
        var area = Screen.FromControl(tray.Form).WorkingArea;
        var scale = tray.Form.DeviceDpi / 96f;
        tray.Form.UsePlace(new Point(area.Left + (int)(40 * scale), area.Top + (int)(40 * scale)));
        if (tray.Tab.PlaceFile is { } file) File.WriteAllText(file, (area.Top + (int)(220 * scale)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Two crops side by side: the widget area and the strip of the edge tab. No other part of the screen.</summary>
    static string Pair(WidgetForm form, EdgeTab tab, string png)
    {
        var screen = Screen.FromControl(form).Bounds;
        var left = Rectangle.Intersect(screen, form.Bounds);
        var right = Rectangle.Intersect(screen, tab.Bounds);
        var gap = 12;
        using var all = new Bitmap(left.Width + gap + right.Width, Math.Max(left.Height, right.Height));
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.FromArgb(0x80, 0x80, 0x80));
            g.CopyFromScreen(left.Location, Point.Empty, left.Size);
            g.CopyFromScreen(right.Location, new Point(left.Width + gap, 0), right.Size);
        }
        all.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return png;
    }

    static async Task<IEnumerable<Check>> Overlap(TrayApp tray, string folder)
    {
        var form = tray.Form;
        var tab = tray.Tab;
        var checks = new List<Check>();
        var now = tray.Counts();
        var n = 0;
        (WaitCounts, string?, string?) More() => (now.Counts with { Questions = now.Counts.Questions + ++n }, $"Question · Overlap test {n}", "One form at a time");
        async Task Rest()
        {
            await Task.Delay(300);
            await Settle(form);
            await Idle(tab);
            await Task.Delay(300);
        }

        // The sampler: both forms on the screen and at rest is the defect.
        using var stop = new CancellationTokenSource();
        var bad = new List<string>();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (form.Visible && !form.Growing && tab.Visible && tab.Slide > 0 && !tab.Moving)
                    lock (bad) bad.Add($"{LayeredForm.FrameClock.Elapsed.TotalMilliseconds:0}");
                try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { }
            }
        });

        if (!form.Visible) form.ShowWidget();
        await Rest();

        // A: a notice during the minimize shrink. The tab slides in after the hide and peeks.
        form.Minimize();
        await Task.Delay(60);
        tray.Notify(More());
        await Rest();
        var a = (!form.Visible, tab.Visible, tab.Peeking);
        var aPng = EdgeShot(tab, folder, "v4-minimize");
        checks.Add(new Check("v4-minimize", a == (true, true, true), $"notice during the shrink: widget hidden {a.Item1}, tab shown {a.Item2}, peek {a.Item3}", aPng));

        // B: a notice during the grow (the tab is on the screen when the show starts). The tab goes and stays hidden.
        form.ShowWidget();
        await Task.Delay(120);
        tray.Notify(More());
        await Rest();
        var b = (form.Visible, !tab.Visible);
        var bPng = Pair(form, tab, Path.Combine(folder, "tray-v4-grow.png"));
        checks.Add(new Check("v4-grow", b == (true, true), $"notice during the grow: widget shown {b.Item1}, tab hidden {b.Item2}", bPng));

        // C: a notice during a close (no minimize). The tab slides in after the hide.
        form.HideWidget();
        await Task.Delay(60);
        tray.Notify(More());
        await Rest();
        var c = (!form.Visible, tab.Visible);
        checks.Add(new Check("v4-close", c == (true, true), $"notice during the close: widget hidden {c.Item1}, tab shown {c.Item2}", ""));

        // D: a notice while the widget shows and rests. The tab stays hidden.
        form.ShowWidget();
        await Rest();
        tray.Notify(More());
        await Rest();
        var d = (form.Visible, !tab.Visible);
        checks.Add(new Check("v4-shown", d == (true, true), $"notice with the widget shown: widget shown {d.Item1}, tab hidden {d.Item2}", ""));

        // E: a notice during the grow of a show from the tray icon, with no tab on the screen before the show.
        form.HideWidget();
        await Rest();
        tab.SlideOut();
        await Rest();
        form.ShowWidget();
        await Task.Delay(120);
        tray.Notify(More());
        await Rest();
        var e = (form.Visible, !tab.Visible);
        var ePng = Pair(form, tab, Path.Combine(folder, "tray-v4-show.png"));
        checks.Add(new Check("v4-show", e == (true, true), $"notice during the grow, no tab before: widget shown {e.Item1}, tab hidden {e.Item2}", ePng));

        stop.Cancel();
        await sampler;
        tab.SetCounts(now.Counts);
        string[] samples;
        lock (bad) samples = [.. bad];
        checks.Add(new Check("v4-one-form", samples.Length == 0,
            $"{samples.Length} samples with the widget and the tab both shown at rest{(samples.Length > 0 ? $" (first at {samples[0]} ms)" : "")}", ""));
        return checks;
    }
}
