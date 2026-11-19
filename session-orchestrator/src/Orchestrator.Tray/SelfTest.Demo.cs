using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The demo run (--selftest folder --demo): the flow of the tray with the real pointer, slow enough for a screen recording.
/// Each press first makes sure that the point is on the tray window, so no click goes to another window. It writes demo.txt
/// with each step and its result.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// The stress run (--selftest folder --demo --stress [--css rule]): 8 cycles of tabs, an answer sheet, a push and back, for a
    /// count of empty frames in a screen recording. The CSS rule (an experiment) goes into the page first.
    /// </summary>
    public static async Task<int> Stress(TrayApp tray, string folder, string? css, bool opens = false)
    {
        Directory.CreateDirectory(folder);
        var w = tray.Window;
        await Until(() => w.Phase == TrayPhase.Open, 8000);
        if (css is not null) await Js(w, $"(s => {{ s.textContent = {System.Text.Json.JsonSerializer.Serialize(css)}; document.head.appendChild(s); }})(document.createElement('style'))");
        var log = new List<string> { $"{DateTime.Now:HH:mm:ss.fff} stress start, css {css ?? "none"}" };
        // --opens: 10 cycles of a dock and an open, for the empty frames at the end of an open.
        if (opens)
        {
            for (var cycle = 0; cycle < 10; cycle++)
            {
                w.Request(TrayRequest.Dock);
                await Until(() => w.Phase == TrayPhase.Badge, 4000);
                await Task.Delay(700);
                log.Add($"{DateTime.Now:HH:mm:ss.fff} open");
                w.Request(TrayRequest.Open);
                await Until(() => w.Phase == TrayPhase.Open, 4000);
                await Task.Delay(1500);
            }
            File.WriteAllLines(Path.Combine(folder, "stress.txt"), log);
            return 0;
        }
        for (var cycle = 0; cycle < 8; cycle++)
        {
            foreach (var tab in new[] { "cards", "stream", "sessions", "settings", "" })
            {
                await Act(log, w, $".nav2 a[href=\"{tab}\"]");
                await Task.Delay(500);
            }
            await Act(log, w, ".nav2 a[href=\"cards\"]");
            await Task.Delay(500);
            await Act(log, w, ".acts [data-act=\"left\"]");
            await Task.Delay(600);
            await Act(log, w, ".presence .scrim", 0.5, 0.2);
            await Task.Delay(500);
            await Act(log, w, ".nav2 a[href=\"stream\"]");
            await Task.Delay(500);
            await Act(log, w, "a[href^=\"events/\"]");
            await Task.Delay(600);
            await Act(log, w, ".hd .path a.cr");
            await Task.Delay(500);
            log.Add($"{DateTime.Now:HH:mm:ss.fff} cycle {cycle + 1} done");
        }
        File.WriteAllLines(Path.Combine(folder, "stress.txt"), log);
        return 0;
    }

    /// <summary>A press of the stress run, with the time of the click in the log.</summary>
    static async Task Act(List<string> log, TrayWindow w, string selector, double fx = 0.5, double fy = 0.5)
    {
        var result = await Press(w, selector, fx, fy);
        log.Add($"{DateTime.Now:HH:mm:ss.fff} {selector}: {result}");
    }

    public static async Task<int> Demo(TrayApp tray, string folder)
    {
        Directory.CreateDirectory(folder);
        var w = tray.Window;
        var log = new List<string>();
        void Step(string text)
        {
            log.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
            Console.WriteLine(text);
        }

        await Until(() => w.Phase == TrayPhase.Open, 8000);
        Step("start: the ball pops in and the panel grows");
        await Task.Delay(1500);

        // The tabs of the app bar, from left to right, and back home.
        foreach (var tab in new[] { "cards", "stream", "sessions", "settings", "" })
        {
            Step($"tab {(tab.Length == 0 ? "home" : tab)}: {await Press(w, $".nav2 a[href=\"{tab}\"]")}");
            await Task.Delay(1100);
        }

        // An answer sheet: up, then down with a click on the scrim.
        Step($"tab cards: {await Press(w, ".nav2 a[href=\"cards\"]")}");
        await Task.Delay(1000);
        Step($"answer: {await Press(w, ".acts [data-act=\"left\"]")}");
        await Task.Delay(1300);
        Step($"close the sheet: {await Press(w, ".presence .scrim", 0.5, 0.2)}");
        await Task.Delay(1100);

        // A push to an event, and back with the path link.
        Step($"tab stream: {await Press(w, ".nav2 a[href=\"stream\"]")}");
        await Task.Delay(1100);
        Step($"open an event: {await Press(w, "a[href^=\"events/\"]")}");
        await Task.Delay(1300);
        Step($"back: {await Press(w, ".hd .path a.cr")}");
        await Task.Delay(1100);
        Step($"tab home: {await Press(w, ".nav2 a.home")}");
        await Task.Delay(1000);

        // A drag of the header moves the panel.
        if (await Center(w, ".hd .title, .hd h1") is { } grip)
        {
            Step($"drag the header: {await Drag(w, grip, -260, -180)}");
            await Task.Delay(1000);
        }

        // Minimize: the panel folds into the ball, the ball pops out, the badge slides in at the right edge.
        Step($"minimize: {await Press(w, ".wbtn.min")}");
        await Until(() => w.Phase == TrayPhase.Badge, 4000);
        await Task.Delay(1200);

        // The hover of the badge: the white tab and its ring slide to the left; the pointer away folds it.
        var s = w.DeviceDpi / 96f;
        var ring = new Point(w.Right - (int)(34 * s), w.Top + (int)((TrayWindow.TabMargin + 34) * s));
        await Glide(ring);
        Step($"hover the badge: tab width {await Js(w, "Math.round(document.querySelector('.tb-tab').getBoundingClientRect().width)")}");
        await Task.Delay(1800);
        await Glide(new Point(ring.X - (int)(260 * s), ring.Y + (int)(80 * s)));
        await Task.Delay(900);

        // A drag of the badge: up and down only, on the right edge.
        ring = new Point(w.Right - (int)(34 * s), w.Top + (int)((TrayWindow.TabMargin + 34) * s));
        Step($"drag the badge up: {await Drag(w, ring, 0, -220)}");
        await Task.Delay(1000);

        // A new card: the ring spins and the peek bubble shows the card.
        if (tray.Store.Sessions().FirstOrDefault() is { } session)
        {
            tray.Store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(session), Parties.User,
                new Dictionary<string, object?> { ["state"] = ReportStates.Blocked, ["summary"] = "The demo needs a decision" }) { Session = session, Needs = Needs.Ack });
            Step("a new card: the ring spins and the bubble peeks");
            await Task.Delay(6500);
        }

        // A click on the badge: it slides out, and the panel opens at the place of the header drag.
        ring = new Point(w.Right - (int)(34 * s), w.Top + (int)((TrayWindow.TabMargin + 34) * s));
        Step($"click the badge: {await Drag(w, ring, 0, 0)}");
        await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(1500);

        // Close: the tray hides; the tray icon (here the request of the icon) opens it again.
        Step($"close: {await Press(w, ".wbtn.x")}");
        await Until(() => w.Phase == TrayPhase.Hidden, 3000);
        await Task.Delay(1200);
        w.Request(TrayRequest.Open);
        Step("the tray icon opens it again");
        await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(1800);

        File.WriteAllLines(Path.Combine(folder, "demo.txt"), log);
        return Faults.Count == 0 ? 0 : 1;
    }

    /// <summary>The screen point of a part of the element (0.5, 0.5 is its center), or null when the element is not there.</summary>
    static async Task<Point?> Center(TrayWindow w, string selector, double fx = 0.5, double fy = 0.5)
    {
        var json = await Js(w, $"(e => e ? (r => [r.left + r.width * {fx.ToString(System.Globalization.CultureInfo.InvariantCulture)}, r.top + r.height * {fy.ToString(System.Globalization.CultureInfo.InvariantCulture)}])(e.getBoundingClientRect()) : null)(document.querySelector('{selector.Replace("'", "\\'")}'))");
        var xy = System.Text.Json.JsonSerializer.Deserialize<double[]?>(json);
        if (xy is not [var x, var y]) return null;
        var s = w.DeviceDpi / 96f;
        return new Point(w.Left + (int)Math.Round(x * s), w.Top + (int)Math.Round(y * s));
    }

    /// <summary>Glides the pointer to the element and clicks it, only when the point is on the tray window.</summary>
    static async Task<string> Press(TrayWindow w, string selector, double fx = 0.5, double fy = 0.5)
    {
        if (await Center(w, selector, fx, fy) is not { } at) return $"no element {selector}";
        await Glide(at);
        await Task.Delay(150);
        return await Drag(w, at, 0, 0) ? "clicked" : "not on the tray window: no click";
    }

    /// <summary>Moves the pointer in steps from where it is, so a recording shows the path.</summary>
    static async Task Glide(Point to)
    {
        var from = Cursor.Position;
        for (var i = 1; i <= 15; i++)
        {
            SetCursorPos(from.X + (to.X - from.X) * i / 15, from.Y + (to.Y - from.Y) * i / 15);
            await Task.Delay(16);
        }
    }
}
