using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// The checks of the notice motion of the edge tab: V1 (the peek bubble leaves no shadow on the screen after its fade) and
/// V2 (the ring spin has an even frame rate). The screen copies run on a pool thread, so they do not stop the UI clock.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The screen area of the edge tab and its bubble: the full strip window, with a margin.</summary>
    static Rectangle StripArea(EdgeTab tab)
    {
        var screen = Screen.FromControl(tab).Bounds;
        return Rectangle.Intersect(screen, tab.Bounds);
    }

    static Bitmap Copy(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        return bitmap;
    }

    /// <summary>The number of pixels that differ by more than 6 in a channel, in the left part of the area (the bubble side).</summary>
    static int Diff(Bitmap a, Bitmap b, int width)
    {
        var n = 0;
        for (var y = 0; y < Math.Min(a.Height, b.Height); y += 1)
            for (var x = 0; x < Math.Min(width, Math.Min(a.Width, b.Width)); x += 1)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                if (Math.Abs(p.R - q.R) > 6 || Math.Abs(p.G - q.G) > 6 || Math.Abs(p.B - q.B) > 6) n++;
            }
        return n;
    }

    /// <summary>The time between two posts of the store load (ORCH_PROBE_LOAD_MS; 0 = no load).</summary>
    static int LoadMs => int.TryParse(Environment.GetEnvironmentVariable("ORCH_PROBE_LOAD_MS"), out var ms) ? ms : 100;

    static async Task<IEnumerable<Check>> NoticeMotion(TrayApp tray, CoreWebView2 web, string folder)
    {
        var form = tray.Form;
        var tab = tray.Tab;
        var checks = new List<Check>();
        var back = Cursor.Position;
        // The pointer away from the tab, so no hover extends it.
        var work = Screen.FromControl(form).WorkingArea;
        Cursor.Position = new Point(work.Left + work.Width / 3, work.Top + work.Height / 2);
        if (form.Visible)
        {
            form.Minimize();
            for (var i = 0; i < 100 && (form.Visible || !tab.OnScreen); i++) await Task.Delay(20);
        }
        await Task.Delay(400);
        await Idle(tab);
        var area = StripArea(tab);
        // A fixed background behind the tab: other windows behind it (a second tray, a browser) change and make noise.
        using var backdrop = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            TopMost = true, BackColor = Color.FromArgb(0xe9, 0xe6, 0xe1), Bounds = area,
        };
        backdrop.Show();
        tab.ToTop();
        await Task.Delay(300);
        // The bubble side: the strip left of the tab.
        var side = Math.Max(1, tab.RingBounds.Left - area.Left - (int)(12 * tab.DeviceDpi / 96f));
        using var before = await Task.Run(() => Copy(area));
        // The load of a live store: a session posts an update each 100 ms, as the sessions of the live demo do.
        using var stop = new CancellationTokenSource();
        var session = tray.Store.Sessions().FirstOrDefault();
        Console.WriteLine($"notice probe: load each {LoadMs} ms on {session}");
        var load = session is null || LoadMs <= 0 ? Task.CompletedTask : Task.Run(async () =>
        {
            var n = 0;
            while (!stop.IsCancellationRequested)
            {
                tray.Store.Events.Post(new PostInput(EventKinds.Update, Parties.Session(session),
                    Parties.User, Messages.Fields(("step", $"load {n}"), ("progress", n % 100), ("summary", "The live load of the self-test"))) { Session = session });
                n++;
                try { await Task.Delay(LoadMs, stop.Token); } catch (OperationCanceledException) { }
            }
            return;
        });
        var counts = System.Diagnostics.Stopwatch.StartNew();
        _ = tray.Counts();
        var countsMs = counts.Elapsed.TotalMilliseconds;
        Console.WriteLine($"notice probe: Counts() {countsMs:0.0} ms");

        var now = tray.Counts();
        var more = now.Counts with { Blocked = now.Counts.Blocked + 1 };
        var log = new List<double>();
        tab.FrameLog = log;
        var start = LayeredForm.FrameClock.Elapsed.TotalMilliseconds;
        tray.Notify((more, "Blocked · Pattern engine spike", "The build needs a key"));
        // V2: the spin runs 800 ms. A capture at the middle of the spin.
        await Task.Delay(400);
        var mid = Path.Combine(folder, "tray-v2-spin.png");
        await Task.Run(() => { using var b = Copy(area); b.Save(mid, System.Drawing.Imaging.ImageFormat.Png); });
        await Task.Delay(600);
        double[] spin;
        lock (log) spin = log.Where(t => t - start <= 800).ToArray();
        var gaps = spin.Zip(spin.Skip(1), (a, b) => b - a).ToArray();
        var mean = gaps.Length > 0 ? gaps.Average() : 0;
        var worst = gaps.Length > 0 ? gaps.Max() : 0;
        var late = gaps.Count(g => g > 25);
        // An even frame rate: at least 40 frames in 800 ms (50 per second), and no gap over 40 ms.
        checks.Add(new Check("v2-spin-frames", spin.Length >= 40 && worst <= 40,
            $"{spin.Length} frames in the 800 ms spin; mean gap {mean:0.0} ms, worst {worst:0.0} ms, {late} gaps over 25 ms; one Counts() call {countsMs:0.0} ms", mid));

        // V1: the bubble shows for 5 s, then fades. A burst of screen copies after the fade start.
        var fadeAt = start + EdgeTab.PeekMs;
        while (LayeredForm.FrameClock.Elapsed.TotalMilliseconds < fadeAt - 100) await Task.Delay(50);
        var shots = await Task.Run(() =>
        {
            var list = new List<(double T, Bitmap B)>();
            var end = fadeAt + 2500;
            while (LayeredForm.FrameClock.Elapsed.TotalMilliseconds < end)
            {
                var t = LayeredForm.FrameClock.Elapsed.TotalMilliseconds - fadeAt;
                list.Add((t, Copy(area)));
                Thread.Sleep(60);
            }
            return list;
        });
        var lines = new List<string>();
        double? clear = null;
        foreach (var (t, b) in shots)
        {
            var d = Diff(before, b, side);
            if (d == 0 && clear is null && t > 0) clear = t;
            if (d > 0) clear = null;
            lines.Add($"{t:0}:{d}");
        }
        // The capture of the first frame after the fade with a mark, and the last frame.
        var ghost = shots.Where(s => s.T > 300).FirstOrDefault(s => Diff(before, s.B, side) > 0);
        var ghostPng = Path.Combine(folder, "tray-v1-fade.png");
        (ghost.B ?? shots[^1].B).Save(ghostPng, System.Drawing.Imaging.ImageFormat.Png);
        var strip = Path.Combine(folder, "tray-v1-fade-strip.png");
        Strip(shots.Where(s => s.T >= -60 && s.T <= 1500).Select(s => s.B).ToList(), strip);
        foreach (var (_, b) in shots) b.Dispose();
        tab.FrameLog = null;
        stop.Cancel();
        await load;
        checks.Add(new Check("v1-fade-clear", clear is { } c && c <= 400,
            $"the bubble side is clear {(clear is { } v ? $"{v:0} ms" : "never")} after the fade start (expect 400 ms or less); changed pixels by time: {string.Join(" ", lines)}", strip));
        tab.SetCounts(now.Counts);
        Cursor.Position = back;
        return checks;
    }

    /// <summary>Puts the copies one below the other in one image.</summary>
    static void Strip(List<Bitmap> frames, string png)
    {
        if (frames.Count == 0) return;
        var w = frames.Max(f => f.Width);
        using var all = new Bitmap(w, frames.Sum(f => f.Height + 2));
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.Magenta);
            var y = 0;
            foreach (var f in frames)
            {
                g.DrawImage(f, 0, y);
                y += f.Height + 2;
            }
        }
        all.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }
}
