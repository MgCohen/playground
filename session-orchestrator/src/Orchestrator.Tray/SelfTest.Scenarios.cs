using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Contract;
using Orchestrator.Core.Events;
using Orchestrator.Store;
using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The scenario run (--selftest folder --scenario name): a few focused actions for the proof of one issue, repeated a few times,
/// 5 to 15 s in total. Window messages only (the pointer of the user does not move). It writes scenario.txt with the time of
/// each step and of each phase change, so the frames of a recording can be matched to the steps.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The scenarios by name. Each one gets the tray and a step log.</summary>
    static readonly Dictionary<string, Func<TrayApp, Action<string>, Task>> Scenarios = new()
    {
        // I14: the status bubble. It shows after a click on a link. A hover alone with messages does not hold (WM_MOUSELEAVE comes at
        // once, see I8), so the steps are clicks: cards, wait, home, wait; three times.
        ["status-bubble"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 3; i++)
            {
                step($"{i}: click tab cards: {await Press(w, ".nav2 a[href=\"cards\"]")}");
                await Task.Delay(1200);
                step($"{i}: click tab home: {await Press(w, ".nav2 a[href=\"\"]")}");
                await Task.Delay(1200);
            }
        },

        // H2, I6, I9: dock and open. The log has the tab translate at the first poll with phase badge.
        ["dock-open"] = (tray, step) => DockOpen(tray, step, 1500),

        // I13: the same flow with reduced motion (start the run with --reduced).
        ["dock-open-reduced"] = (tray, step) => DockOpen(tray, step, 1000),

        // H1: the fold on a page with no app bar (events/<id>).
        ["fold-no-app-bar"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 3; i++)
            {
                var link = await Js(w, "document.querySelector('a[href^=\"events/\"]')?.getAttribute('href') ?? ''");
                if (link.Length == 0)
                {
                    await Js(w, $"{Go}('stream')");
                    await Task.Delay(600);
                    link = await Js(w, "document.querySelector('a[href^=\"events/\"]')?.getAttribute('href') ?? ''");
                }
                await Js(w, $"{Go}('{link}')");
                step($"{i}: go {link}");
                await Task.Delay(800);
                step($"{i}: dock, app bar shows {await Js(w, "(e => !!e && e.getClientRects().length > 0)(document.querySelector('.tray-view .nav2'))")}");
                w.Request(TrayRequest.Dock);
                await Task.Delay(1200);
                step($"{i}: open");
                w.Request(TrayRequest.Open);
                await Task.Delay(1200);
            }
        },

        // H4: a new card while a hide runs.
        ["notice-while-busy"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 3; i++)
            {
                step($"{i}: hide");
                w.Request(TrayRequest.Hide);
                await Task.Delay(50);
                step($"{i}: post a card: {PostCard(tray, $"Scenario card {i}")}");
                await Task.Delay(2500);
                step($"{i}: badge shows {tray.Badge.Visible}; open");
                w.Request(TrayRequest.Open);
                await Task.Delay(1200);
            }
        },

        // The reference look of the spin and the peek of the badge.
        ["badge-peek"] = async (tray, step) =>
        {
            var w = tray.Window;
            step("hide");
            w.Request(TrayRequest.Hide);
            await Task.Delay(600);
            for (var i = 1; i <= 2; i++)
            {
                step($"{i}: post a card: {PostCard(tray, $"Peek card {i}")}");
                await Task.Delay(6000);
            }
        },

        // The reference look of the throw and the tuck of the deck.
        ["deck-throw-tuck"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 6; i++) PostCard(tray, $"Deck card {i}");
            await Js(w, $"{Go}('cards')");
            await Until(async () => await Js(w, "!!document.querySelector('.acts [data-act=\"skip\"]')") == "true", 3000);
            await Task.Delay(600);
            for (var i = 1; i <= 3; i++)
            {
                step($"{i}: skip: {await Press(w, ".acts [data-act=\"skip\"]")}");
                await Task.Delay(1000);
                step($"{i}: throw: {await Press(w, ".acts [data-throw]")}");
                await Task.Delay(1000);
            }
        },

        // The reference look of the tab move, the sheet, the push and the back move.
        ["page-moves"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 2; i++)
            {
                step($"{i}: tab cards: {await Press(w, ".nav2 a[href=\"cards\"]")}");
                await Task.Delay(800);
                step($"{i}: sheet open: {await Press(w, ".acts [data-act=\"left\"]")}");
                await Task.Delay(800);
                step($"{i}: sheet close: {await Press(w, ".presence .scrim", 0.5, 0.1)}");
                await Task.Delay(800);
                step($"{i}: tab stream: {await Press(w, ".nav2 a[href=\"stream\"]")}");
                await Task.Delay(800);
                step($"{i}: push: {await Press(w, "a[href^=\"events/\"]")}");
                await Task.Delay(800);
                await Js(w, "history.back()");
                step($"{i}: back");
                await Task.Delay(800);
                step($"{i}: tab home: {await Press(w, ".nav2 a[href=\"\"]")}");
                await Task.Delay(800);
            }
        },
    };

    static async Task DockOpen(TrayApp tray, Action<string> step, int wait)
    {
        var w = tray.Window;
        for (var i = 1; i <= 3; i++)
        {
            step($"{i}: dock");
            w.Request(TrayRequest.Dock);
            await Task.Delay(wait);
            step($"{i}: open");
            w.Request(TrayRequest.Open);
            await Task.Delay(wait);
        }
    }

    /// <summary>A blocked report, as the report tool posts it: a new card. It returns the session, or "no session".</summary>
    static string PostCard(TrayApp tray, string summary)
    {
        var session = tray.Store.Sessions().FirstOrDefault();
        if (session is null) return "no session";
        tray.Store.Events.Post(new PostInput(EventKinds.Report, Parties.Session(session), Parties.User,
            new Dictionary<string, object?> { ["state"] = ReportStates.Blocked, ["summary"] = summary }) { Session = session, Needs = Needs.Ack });
        return session;
    }

    public static async Task<int> Scenario(TrayApp tray, string folder, string? name)
    {
        Directory.CreateDirectory(folder);
        if (name is null || !Scenarios.TryGetValue(name, out var run))
        {
            File.WriteAllText(Path.Combine(folder, "scenario.txt"), $"no scenario '{name}'; names: {string.Join(", ", Scenarios.Keys)}");
            return 2;
        }
        var w = tray.Window;
        await Until(() => w.Phase == TrayPhase.Open, 8000);
        await Task.Delay(800);
        var log = new List<string>();
        var pointer = Cursor.Position;
        void Step(string text)
        {
            lock (log) log.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        }
        Step($"scenario {name}");
        using var stop = new CancellationTokenSource();
        var phases = Phases(tray, Step, stop.Token);
        var trace = tray.Services.GetRequiredService<Orchestrator.Web.Plugins.Js.Infrastructure.JsTrace>();
        void OnTrace(string line) => Step($"trace {line[13..]}");
        trace.Written += OnTrace;
        await run(tray, Step);
        await stop.CancelAsync();
        await phases;
        trace.Written -= OnTrace;
        Step(Cursor.Position == pointer ? "pointer: not moved" : $"pointer: moved from {pointer} to {Cursor.Position}");
        File.WriteAllLines(Path.Combine(folder, "scenario.txt"), log);
        return Faults.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Polls the phase each 10 ms and logs each change with its time. At each change to phase badge it also logs the tab translate
    /// (0 means that the slide of the tab ended before the phase).
    /// </summary>
    static async Task Phases(TrayApp tray, Action<string> step, CancellationToken stop)
    {
        var w = tray.Window;
        var last = w.Phase;
        step($"phase {last}");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (w.Phase == last) continue;
            last = w.Phase;
            if (last != TrayPhase.Badge)
            {
                step($"phase {last}");
                continue;
            }
            var tab = await Js(tray.Badge, "(e => e ? getComputedStyle(e).translate : 'no tab')(document.querySelector('.tb-tab'))");
            step($"phase {last}, tab translate {tab}");
        }
    }

    /// <summary>
    /// --reduced: the page of each window gets prefers-reduced-motion: reduce, as the system setting gives it. DevTools emulation,
    /// so the setting of the user does not change.
    /// </summary>
    public static Task Reduced(ClearHost w) =>
        w.Web!.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", """{"features":[{"name":"prefers-reduced-motion","value":"reduce"}]}""");

    /// <summary>A pointer move onto the element, with no click: a window message.</summary>
    static async Task<string> Point(TrayWindow w, string selector)
    {
        if (await Center(w, selector) is not { } at) return $"no element {selector}";
        Post(w, 0x200, 0, at);
        return "moved (window message)";
    }
}
