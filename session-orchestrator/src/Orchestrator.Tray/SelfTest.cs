using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The self-test of the tray (--selftest folder). It runs the flow of the tray stage and reads the state of the window and of the
/// page: no mouse, so it does not depend on the screen. With --mouse it adds checks with real mouse input; each one first makes
/// sure that the point is on the tray window, so no click goes to another window. The result is selftest.json in the folder.
/// </summary>
public static partial class SelfTest
{
    public sealed record Check(string Name, bool Pass, string Message);

    // Go(path) clicks a temporary link, so the Blazor router opens the view as a user click does.
    const string Go = "(p => { const a = document.createElement('a'); a.href = p; document.body.appendChild(a); a.click(); a.remove(); })";

    /// <summary>The place files of the self-test: new files in the self-test folder, so the panel and the badge start at their home places.</summary>
    public static string FreshPlaceFile(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "place.json", "badge.txt" })
        {
            var file = Path.Combine(folder, name);
            if (File.Exists(file)) File.Delete(file);
        }
        return Path.Combine(folder, "place.json");
    }

    public static async Task<int> Run(TrayApp tray, string folder, bool mouse, bool reduced = false)
    {
        Directory.CreateDirectory(folder);
        var w = tray.Window;
        var checks = new List<Check>();
        void Add(string name, bool pass, string message)
        {
            checks.Add(new Check(name, pass, message));
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {message}");
        }

        // 1. The start: the ball pops in, the panel grows, the panel is at its home place.
        var open = await Until(() => w.Phase == TrayPhase.Open, 8000);
        await Task.Delay(900);
        var stage = await Js(w, "document.querySelector('.tray-stage')?.className");
        var size = await Js(w, "(r => [Math.round(r.width), Math.round(r.height)])(document.querySelector('.tray-panel').getBoundingClientRect())");
        (await Picture(w)).Save(Path.Combine(folder, "1-open.png"));
        Add("start", open && w.Visible && w.Bounds == w.PanelBounds() && stage.Contains("is-open"),
            $"phase {w.Phase}, visible {w.Visible}, window {w.Bounds} (expect {w.PanelBounds()}), stage \"{stage}\", panel {size} CSS px");

        // 1b. The number of the Home ring counts up on its first show: the rule is valid (an invalid steps() drops the animation).
        var count = await Js(w, "(e => e ? [getComputedStyle(e, '::after').animationName, getComputedStyle(e, '::after').animationTimingFunction, e.textContent].join(' | ') : 'none')(document.querySelector('.ring-n[data-count]'))");
        var probe = await Js(w, """
            (() => { const e = document.createElement('i'); e.style.cssText = '--to: 5; animation: m-count-up 1s steps(var(--to), end)'; document.body.appendChild(e);
              const v = getComputedStyle(e).animationName + ' / ' + getComputedStyle(e).animationTimingFunction; e.remove();
              const d = document.querySelector('.dring'); return 'drawn ' + (d ? d.hasAttribute('data-drawn') : 'no ring') + '; steps(var) test: ' + v; })()
            """);
        var stageReady = await Js(w, "document.querySelector('.tray-stage').classList.contains('is-ready')");
        Add("ring-count-up", count.StartsWith("m-count-up | steps(") && probe.StartsWith("drawn false") && stageReady == "true", $"after part: {count}; {probe}; stage ready {stageReady}");

        // 2. The home button is the ball: the clip circle of the fold is centered on it.
        await Js(w, "orchTray.home(document.querySelector('.tray-stage'))");
        var ball = await Js(w, """
            (() => { const s = document.querySelector('.tray-stage'), p = document.querySelector('.tray-panel').getBoundingClientRect(),
              h = document.querySelector('.tray-view .nav2 .home').getBoundingClientRect();
              return [h.left + h.width / 2 - p.left, h.top + h.height / 2 - p.top, parseFloat(s.style.getPropertyValue('--hx')), parseFloat(s.style.getPropertyValue('--hy'))].map(Math.round); })()
            """);
        var hb = Ints(ball);
        Add("home-ball", hb.Length == 4 && hb[0] == hb[2] && hb[1] == hb[3] && hb[0] > 0, $"home center and clip center [x, y, hx, hy] = {ball}");

        // 3. A sheet is fixed to the panel, not to the window with its margin.
        await Js(w, $"{Go}('cards')");
        await Until(async () => await Js(w, "!!document.querySelector('.acts [data-act=\"left\"]')") == "true", 3000);
        await Js(w, "document.querySelector('.acts [data-act=\"left\"]').click()");
        await Until(async () => await Js(w, "!!document.querySelector('.presence .scrim')") == "true", 3000);
        await Task.Delay(500);
        var scrim = await Js(w, "(s => (p => [s.left - p.left, s.top - p.top, s.width - p.width, s.height - p.height].map(Math.round))(document.querySelector('.tray-panel').getBoundingClientRect()))(document.querySelector('.presence .scrim').getBoundingClientRect())");
        Add("sheet-in-panel", Ints(scrim).All(v => v == 0), $"scrim minus panel [left, top, width, height] = {scrim} (expect all 0)");
        await Js(w, "document.querySelector('.presence .scrim').click()");
        var gone = await Until(async () => await Js(w, "!!document.querySelector('.sheet')") == "false", 2000);
        Add("sheet-close", gone, gone ? "the sheet left and went" : "the sheet is still there after 2 s");

        // 4. A page move stays in the panel: in the middle of a push, the margin of the window shows no page.
        await Js(w, $"{Go}('stream')");
        await Task.Delay(700);
        var edge = Edge(await Picture(w), w);
        var link = await Js(w, "document.querySelector('a[href^=\"events/\"]')?.getAttribute('href') ?? ''");
        var moved = false;
        if (link.Length > 0)
        {
            await Js(w, $"setTimeout(() => {Go}('{link}'), 0)");
            await Task.Delay(120);
            var shot = await Picture(w);
            shot.Save(Path.Combine(folder, "push-middle.png"));
            var mid = Edge(shot, w);
            var classes = await Js(w, "document.documentElement.className");
            var path = await Js(w, "(e => e ? getComputedStyle(e).animationName + ' | ' + getComputedStyle(document.querySelector('.hd')).viewTransitionName : 'no title')(document.querySelector('.hd .cur'))");
            Add("header-path-motion", path == "m-path-cur | header", $"current title animation | header name: {path}");
            var group = await Js(w, "(s => [s.overflow, s.borderRadius, s.width, s.height, s.left, s.top].join(' '))(getComputedStyle(document.documentElement, '::view-transition-group(page)'))");
            var named = await Js(w, "getComputedStyle(document.querySelector('.tray-view')).viewTransitionName");
            Console.WriteLine($"push: group {group}, name {named}");
            checks.Add(new Check("push-data", true, $"group (overflow radius width height left top) {group}; name of the view {named}"));
            await Task.Delay(700);
            moved = classes.Contains("vt");
            Add("page-move-in-panel", moved && mid <= edge + 40, $"alpha of the margin before {edge}, in the push {mid} (a page would be 255); classes in the push \"{classes}\"");
            await Js(w, $"{Go}('')");
            await Task.Delay(700);
        }
        else Add("page-move-in-panel", false, "no event link on the stream page");

        // 4b. Tab moves: the shadow of the panel must not change in a move (one root picture, not two), and the pictures of the move
        // to Settings show each step.
        foreach (var (from, to) in new[] { ("", "cards"), ("cards", "stream"), ("stream", "sessions"), ("sessions", "settings"), ("settings", "") })
        {
            await Js(w, $"{Go}('{from}')");
            await Task.Delay(800);
            var rest = Shadow(await Picture(w), w);
            await Js(w, $"setTimeout(() => {Go}('{to}'), 0)");
            var steps = new List<int>();
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(50);
                var pic = await Picture(w);
                if (to == "settings") pic.Save(Path.Combine(folder, $"tab-settings-{(i + 1) * 50}ms.png"));
                steps.Add(Shadow(pic, w));
            }
            await Task.Delay(500);
            Add($"tab-{(from.Length == 0 ? "home" : from)}-to-{(to.Length == 0 ? "home" : to)}", steps.Max() <= rest + 6,
                $"shadow alpha 8 px out of the panel: at rest {rest}, in the move [{string.Join(", ", steps)}]");
        }

        // 4c. The nav dot: hidden on Home, at the center of the current item on each tab, after its motion.
        foreach (var item in new[] { "", "cards", "stream", "sessions", "settings", "" })
        {
            await Js(w, $"{Go}('{item}')");
            await Task.Delay(700);
            var dot = await Js(w, """
                (() => { const n = document.querySelector('.tray-view .nav2'), d = n.querySelector('.ndot'), on = n.querySelector('.it.on');
                  const r = d.getBoundingClientRect(), o = on ? on.getBoundingClientRect() : null;
                  return JSON.stringify({ off: d.classList.contains('off'), opacity: +getComputedStyle(d).opacity, dx: o ? Math.round(r.left + r.width / 2 - (o.left + o.width / 2)) : null }); })()
                """);
            var name = item.Length == 0 ? "home" : item;
            var ok = item.Length == 0 ? dot.Contains("\"opacity\":0") : dot.Contains("\"dx\":0") && dot.Contains("\"opacity\":1");
            Add($"dot-{name}", ok, $"dot {dot}");
        }

        // 5. Minimize: the panel folds, the ball pops out, the badge slides in at the right edge.
        await Js(w, "window.__frames = null");
        var frames = w.Web!.ExecuteScriptAsync(Meter(1400));
        w.Request(TrayRequest.Dock);
        await Task.Delay(180);
        (await Picture(w)).Save(Path.Combine(folder, "2-fold-middle.png"));
        var docked = await Until(() => w.Phase == TrayPhase.Badge, 4000);
        await frames;
        // ExecuteScriptAsync does not wait for the promise of the meter: wait for its result.
        await Until(async () => await Js(w, "!!window.__frames") == "true", 3000);
        var b = tray.Badge;
        // H2: at phase badge the slide of the tab has ended, so the read needs no delay.
        var tab = await Js(b, "getComputedStyle(document.querySelector('.tb-tab')).translate");
        Add("badge-in-ended", tab is "none" or "0px" or "0px 0px", $"tab translate at the first read with phase badge: {tab} (expect 0)");
        var area = Screen.FromRectangle(b.Bounds).WorkingArea;
        await Task.Delay(400);
        (await Picture(b)).Save(Path.Combine(folder, "3-badge.png"));
        Add("dock", docked && !w.Visible && b.Visible && b.Bounds == b.PlaceBounds() && b.Right == area.Right && tab is "none" or "0px" or "0px 0px",
            $"phase {w.Phase}, panel window shows {w.Visible}, badge window {b.Bounds} (expect {b.PlaceBounds()}), right edge {b.Right} = {area.Right}, tab translate {tab}, click area {b.HitArea}; frames of the fold {await Frames(w)}");

        // 5b. The wide tab of the hover fits in the click area of the hover (the window region clips the drawing too). The hover
        // width is the width of the tab with "width: auto", the same rule as :hover.
        var fit = await Js(b, """
            (e => { const old = e.style.width; e.style.transition = 'none'; e.style.width = 'auto';
              const v = Math.round(e.getBoundingClientRect().width); e.style.width = old; e.style.transition = ''; return v; })(document.querySelector('.tb-tab'))
            """);
        var counts = await Js(b, "document.querySelector('.tb-txt small')?.textContent ?? ''");
        Add("badge-hover-fits", int.TryParse(fit, out var wideTab) && wideTab <= BadgeWindow.TabWide,
            $"tab width at the hover {fit} CSS px, click area of the hover {BadgeWindow.TabWide} (+ {BadgeWindow.TabMargin} margin), window {BadgeWindow.BadgeWidth}; text \"{counts}\"");

        // 6. Open from the badge: back at the panel place.
        w.Request(TrayRequest.Open);
        var back = await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Until(() => w.Phase == TrayPhase.Start || w.Phase == TrayPhase.Open, 2000);
        await Task.Delay(330);
        (await Picture(w)).Save(Path.Combine(folder, "5-grow-middle.png"));
        await Task.Delay(600);
        Add("open-from-badge", back && w.Visible && !b.Visible && w.Bounds == w.PanelBounds(), $"phase {w.Phase}, panel window shows {w.Visible} at {w.Bounds} (expect {w.PanelBounds()}), badge window shows {b.Visible}");

        // 7. Close: the tray hides, with no badge.
        w.Request(TrayRequest.Hide);
        var hidden = await Until(() => w.Phase == TrayPhase.Hidden && !w.Visible && !b.Visible, 3000);
        Add("hide", hidden, $"phase {w.Phase}, panel window shows {w.Visible}, badge window shows {b.Visible}");

        // 8. A new card while the tray is hidden: the badge comes in, the ring spins, the bubble peeks and goes after 5 s.
        var session = tray.Store.Sessions().FirstOrDefault();
        // A blocked report, as the report tool posts it: a new card.
        if (session is not null)
            tray.Store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(session), Parties.User,
                new Dictionary<string, object?> { ["state"] = ReportStates.Blocked, ["summary"] = "The self-test is blocked on purpose" }) { Session = session, Needs = Needs.Ack });
        var noticed = await Until(() => w.Phase == TrayPhase.Badge, 5000);
        await Task.Delay(400);
        var peek = await Js(b, "!!document.querySelector('.tb-peek.on')");
        (await Picture(b)).Save(Path.Combine(folder, "4-peek.png"));
        var wideArea = b.HitArea.Width;
        var peekGone = await Until(async () => await Js(b, "!!document.querySelector('.tb-peek.on')") == "false", 7000);
        await Task.Delay(800);
        Add("notice", session is not null && noticed && b.Visible && peek == "true" && wideArea == b.Width && peekGone && b.HitArea.Width < b.Width,
            $"session {session}, phase {w.Phase}, badge window shows {b.Visible}, peek {peek}, click area at the peek {wideArea} (window {b.Width}), peek gone {peekGone}, click area after {b.HitArea.Width}");

        // 9. A saved place of the panel: the next open is there.
        var place = new Point(area.Left + 200, area.Top + 120);
        w.UsePlace(place);
        w.Request(TrayRequest.Open);
        await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(600);
        var m = (int)(TrayWindow.ShadowMargin * w.DeviceDpi / 96f);
        Add("saved-place", w.Left + m == place.X && w.Top + m == place.Y, $"panel at {w.Left + m}, {w.Top + m} (expect {place.X}, {place.Y})");

        // 10. The pin keeps the window on top; at the badge place the window is always on top.
        w.Pinned = true;
        var pinnedTop = w.TopMost;
        w.Pinned = false;
        Add("pin", pinnedTop && !w.TopMost, $"pinned top {pinnedTop}, unpinned top {w.TopMost}");

        // 11. The input of the badge window, with window messages (the real pointer does not move): a move on the tab makes the
        // click area wide (the hover), a press and a release with no move open the panel.
        w.Request(TrayRequest.Dock);
        await Until(() => w.Phase == TrayPhase.Badge, 4000);
        await Task.Delay(500);
        var ds = b.DeviceDpi / 96f;
        var client = new Point(b.Width - (int)(34 * ds), (int)((BadgeWindow.TabMargin + 34) * ds));
        var lp = (nint)((client.Y << 16) | (client.X & 0xffff));
        var atRest = b.HitArea.Width;
        _ = PostMessageBadge(b.Handle, 0x200, 0, lp);
        await Task.Delay(200);
        var hover = b.HitArea.Width;
        _ = PostMessageBadge(b.Handle, 0x201, 1, lp);
        await Task.Delay(60);
        _ = PostMessageBadge(b.Handle, 0x202, 0, lp);
        var opened = await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(600);
        Add("badge-input", atRest == (int)((BadgeWindow.TabRest + BadgeWindow.TabMargin) * ds) && hover == (int)((BadgeWindow.TabWide + BadgeWindow.TabMargin) * ds) && opened && w.Visible && !b.Visible,
            $"click area at rest {atRest}, at the hover {hover}; after the click: phase {w.Phase}, panel window shows {w.Visible}, badge window shows {b.Visible}");

        if (mouse) await Mouse(w, b, Add);

        // S8: a throw from the act button: C# starts the throw, waits for it (step deck-throw), then the next card is on top.
        await Js(w, $"{Go}('cards')");
        await Task.Delay(700);
        for (var i = 0; i < 4 && await Js(w, "!!document.querySelector('.acts [data-throw]')") != "true"; i++)
        {
            await Js(w, "document.querySelector('.acts [data-act=\"skip\"]')?.click()");
            await Task.Delay(700);
        }
        var topBefore = await Js(w, "document.querySelector('[data-deck-card] .q, [data-deck-card] h3, [data-deck-card]')?.textContent?.trim().slice(0, 60) ?? ''");
        await Js(w, "document.querySelector('.acts [data-throw]')?.click()");
        await Task.Delay(800);
        var topAfter = await Js(w, "document.querySelector('[data-deck-card] .q, [data-deck-card] h3, [data-deck-card]')?.textContent?.trim().slice(0, 60) ?? ''");
        var thrown = tray.Services.GetRequiredService<Orchestrator.Web.Plugins.Js.Infrastructure.JsTrace>().Lines
            .Any(l => l.Contains("~=\"deck-throw\"]", StringComparison.Ordinal) && l.Contains("by end", StringComparison.Ordinal));
        Add("deck-throw", thrown && topAfter != topBefore, $"trace of step deck-throw by end {thrown}; top card before \"{topBefore}\", after \"{topAfter}\"");
        await Js(w, $"{Go}('')");
        await Task.Delay(600);

        // H4: a new card during a hide: the notice waits for the hide, then the badge comes in and peeks.
        w.Request(TrayRequest.Hide);
        PostCard(tray, "The self-test posts a card during a hide");
        var busyBadge = await Until(() => w.Phase == TrayPhase.Badge, 3000);
        var busyPeek = await Until(async () => await Js(b, "!!document.querySelector('.tb-peek.on')") == "true", 1500);
        Add("notice-while-busy", busyBadge && busyPeek, $"after a hide with a new card: phase {w.Phase}, badge shows {b.Visible}, peek {busyPeek}");
        w.Request(TrayRequest.Open);
        await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(600);

        // H1: the fold on a page with no app bar ends with the panel, not at once: the badge comes after the fold.
        var doc = await Js(w, "document.querySelector('a[href^=\"events/\"]')?.getAttribute('href') ?? ''");
        if (doc.Length == 0)
        {
            await Js(w, $"{Go}('stream')");
            await Task.Delay(700);
            doc = await Js(w, "document.querySelector('a[href^=\"events/\"]')?.getAttribute('href') ?? ''");
        }
        await Js(w, $"{Go}('{doc}')");
        await Task.Delay(800);
        var foldClock = System.Diagnostics.Stopwatch.StartNew();
        w.Request(TrayRequest.Dock);
        await Until(() => w.Phase == TrayPhase.BadgeOut || w.Phase == TrayPhase.Badge, 4000);
        var foldMs = foldClock.ElapsedMilliseconds;
        Add("fold-no-app-bar", reduced || foldMs is >= 340 and < 900, $"on {doc}: dock to badge-out {foldMs} ms (expect 340 to 900: the fold of the panel, no limit)");
        w.Request(TrayRequest.Open);
        await Until(() => w.Phase == TrayPhase.Open, 4000);
        await Task.Delay(600);
        await Js(w, $"{Go}('')");
        await Task.Delay(500);

        // I13: with reduced motion each duration token is 1 ms, so a dock and an open end at once: no wait runs to its limit.
        if (reduced)
        {
            var times = new List<long>();
            for (var i = 0; i < 2; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                w.Request(TrayRequest.Dock);
                await Until(() => w.Phase == TrayPhase.Badge, 4000);
                times.Add(clock.ElapsedMilliseconds);
                await Task.Delay(300);
                clock.Restart();
                w.Request(TrayRequest.Open);
                await Until(() => w.Phase == TrayPhase.Open, 4000);
                times.Add(clock.ElapsedMilliseconds);
                await Task.Delay(300);
            }
            Add("reduced-flow-time", times.All(t => t < 300), $"dock, open, dock, open in ms: {string.Join(", ", times)} (expect each under 300)");
        }

        // I12: each wait of the run ends by the end of its motion, not by its limit.
        var trace = tray.Services.GetRequiredService<Orchestrator.Web.Plugins.Js.Infrastructure.JsTrace>().Lines;
        var waits = trace.Where(l => l.Contains("js orchMotion.wait", StringComparison.Ordinal)).ToList();
        var late = waits.Where(l => l.Contains("by limit", StringComparison.Ordinal) || l.Contains("by missing", StringComparison.Ordinal) || l.Contains(" error js.failed", StringComparison.Ordinal)).ToList();
        // S4: each step of the tray flow runs through the registry (its data-motion target) and ends by the end of its motion.
        string[] tracedSteps = ["grow", "fold", "badge-in", "badge-out", "spin", "peek-out"];
        var untraced = tracedSteps.Where(s => !waits.Any(l => l.Contains($"~=\"{s}\"]", StringComparison.Ordinal) && l.Contains("by end", StringComparison.Ordinal))).ToList();
        Add("steps-traced", untraced.Count == 0, untraced.Count == 0 ? $"each step ends by end: {string.Join(", ", tracedSteps)}" : $"no wait by end for: {string.Join(", ", untraced)}");
        Add("no-wait-at-limit",waits.Count >= 10 && late.Count == 0, $"{waits.Count} waits, {late.Count} by limit, missing or error{(late.Count > 0 ? ": " + string.Join(" | ", late.Take(4)) : "")}");

        Add("exceptions", Faults.Count == 0, Faults.First ?? "no unhandled exception");
        File.WriteAllText(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.All(c => c.Pass) ? 0 : 1;
    }

    /// <summary>The checks with real mouse input. Each press first makes sure that the point is on the tray window.</summary>
    static async Task Mouse(TrayWindow w, BadgeWindow b, Action<string, bool, string> add)
    {
        var s = w.DeviceDpi / 96f;
        // The header drag: the system move loop moves the window with the pointer.
        var before = w.Location;
        var grip = new Point(w.Left + (int)((TrayWindow.ShadowMargin + 120) * s), w.Top + (int)((TrayWindow.ShadowMargin + 22) * s));
        var dragged = await Drag(w, grip, -120, -80);
        await Task.Delay(400);
        add("mouse-panel-drag", dragged && w.Left - before.X == -120 && w.Top - before.Y == -80, $"moved {w.Left - before.X}, {w.Top - before.Y} (expect -120, -80); press on the window {dragged}");

        w.Request(TrayRequest.Dock);
        await Until(() => w.Phase == TrayPhase.Badge, 4000);
        await Task.Delay(500);
        var ring = new Point(b.Right - (int)(34 * s), b.Top + (int)((BadgeWindow.TabMargin + 34) * s));
        var on = Own(b, ring);
        SetCursorPos(ring.X, ring.Y);
        await Task.Delay(600);
        var wide = await Js(b, "Math.round(document.querySelector('.tb-tab').getBoundingClientRect().width)");
        var area = b.HitArea.Width;
        SetCursorPos(ring.X - (int)(500 * s), ring.Y);
        await Task.Delay(700);
        var rest = await Js(b, "Math.round(document.querySelector('.tb-tab').getBoundingClientRect().width)");
        add("mouse-badge-hover", on && int.TryParse(wide, out var a) && a > BadgeWindow.TabRest && rest == BadgeWindow.TabRest.ToString() && area > b.HitArea.Width,
            $"tab width at the hover {wide}, after {rest}; click area at the hover {area}, after {b.HitArea.Width}; pointer on the window {on}");

        var top = b.Top;
        var moved = await Drag(b, ring, 0, -150);
        await Task.Delay(300);
        add("mouse-badge-drag", moved && b.Top - top == -150 && b.Right == Screen.FromRectangle(b.Bounds).WorkingArea.Right && w.Phase == TrayPhase.Badge,
            $"moved {b.Top - top} (expect -150), right edge {b.Right}, phase {w.Phase}");

        ring = new Point(b.Right - (int)(34 * s), b.Top + (int)((BadgeWindow.TabMargin + 34) * s));
        var clicked = await Drag(b, ring, 0, 0);
        var opened = await Until(() => w.Phase == TrayPhase.Open, 4000);
        add("mouse-badge-click", clicked && opened, $"phase {w.Phase}; press on the window {clicked}");
    }

    static bool Own(ClearHost w, Point at) => WindowFromPoint(new Pt(at.X, at.Y)) == w.Handle;

    /// <summary>A press, a move in steps, a release; only on the tray window. A move of 0, 0 is a click.</summary>
    static async Task<bool> Drag(ClearHost w, Point from, int dx, int dy)
    {
        if (!Own(w, from)) return false;
        SetCursorPos(from.X, from.Y);
        await Task.Delay(120);
        mouse_event(0x2, 0, 0, 0, 0);
        for (var i = 1; i <= 12 && (dx != 0 || dy != 0); i++)
        {
            await Task.Delay(16);
            SetCursorPos(from.X + dx * i / 12, from.Y + dy * i / 12);
        }
        await Task.Delay(60);
        mouse_event(0x4, 0, 0, 0, 0);
        return true;
    }

    static string Meter(int ms) => $$"""
        new Promise(r => { const t = [], s = performance.now(); let l = s;
          const f = n => { t.push(n - l); l = n; if (n - s < {{ms}}) requestAnimationFrame(f); else { window.__frames = t.slice(1); r(); } };
          requestAnimationFrame(f); })
        """;

    static async Task<string> Frames(ClearHost w)
    {
        var t = JsonSerializer.Deserialize<double[]>(await w.Web!.ExecuteScriptAsync("window.__frames || []")) ?? [];
        return t.Length == 0 ? "none" : $"{t.Length} frames, longest {t.Max():0.0} ms, late {t.Count(x => x > 20)}";
    }

    /// <summary>A picture of the page only (WebView2 preview), with no other window over it.</summary>
    static async Task<Bitmap> Picture(ClearHost w)
    {
        var stream = new MemoryStream();
        await w.Web!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    /// <summary>The highest alpha in the left margin of the window, at the middle height (6 px from the edge, 10 rows).</summary>
    static int Edge(Bitmap picture, ClearHost w)
    {
        using (picture)
        {
            var x = (int)(6 * w.DeviceDpi / 96f);
            var y = picture.Height / 2;
            return Enumerable.Range(y - 5, 10).Max(r => picture.GetPixel(x, r).A);
        }
    }

    /// <summary>The alpha of the shadow 8 px left of the panel edge, at the middle height (the mean of 10 rows).</summary>
    static int Shadow(Bitmap picture, ClearHost w)
    {
        using (picture)
        {
            var x = (int)((TrayWindow.ShadowMargin - 8) * w.DeviceDpi / 96f);
            var y = picture.Height / 2;
            return (int)Enumerable.Range(y - 5, 10).Average(r => picture.GetPixel(x, r).A);
        }
    }

    static int[] Ints(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static async Task<string> Js(ClearHost w, string script)
    {
        var json = await w.Web!.ExecuteScriptAsync(script);
        return json.StartsWith('"') ? JsonSerializer.Deserialize<string>(json) ?? "" : json;
    }

    static async Task<bool> Until(Func<bool> test, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            if (test()) return true;
            await Task.Delay(50);
        }
        return test();
    }

    static async Task<bool> Until(Func<Task<bool>> test, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            if (await test()) return true;
            await Task.Delay(50);
        }
        return await test();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Pt(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Pt point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extra);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageBadge(nint hwnd, int message, nint wparam, nint lparam);
}
