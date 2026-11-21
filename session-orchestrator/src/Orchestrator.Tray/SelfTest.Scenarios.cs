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
        // I3: a press right after a page move. The sheet cycle of the stress run (--sheets), three times, but the sheet press comes
        // 100 ms after the tab click, before the Cards page is ready (in a stress run under load, 700 ms was not always enough).
        ["late-press"] = async (tray, step) =>
        {
            var w = tray.Window;
            // The state of the page after each press: the path, the sheet, and the sheet button (present, its place).
            const string State = "(b => location.pathname + ' | sheet ' + !!document.querySelector('.sheet') + ' | button ' + (b ? (r => Math.round(r.left) + ',' + Math.round(r.top) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height))(b.getBoundingClientRect()) : 'none'))(document.querySelector('.acts [data-act=\\\"left\\\"]'))";
            for (var i = 1; i <= 3; i++)
            {
                step($"{i}: tab cards: {await Press(w, ".nav2 a[href=\"cards\"]")}");
                step($"{i}:   at once: {await Js(w, State)}");
                await Task.Delay(100);
                step($"{i}:   after 100 ms: {await Js(w, State)}");
                step($"{i}: open sheet: {await Press(w, ".acts [data-act=\"left\"]")}");
                await Task.Delay(900);
                step($"{i}:   after 900 ms: {await Js(w, State)}");
                step($"{i}: close sheet: {await Press(w, ".presence .scrim", 0.5, 0.2)}");
                await Task.Delay(700);
                step($"{i}:   after 700 ms: {await Js(w, State)}");
                step($"{i}: tab home: {await Press(w, ".nav2 a[href=\"\"]")}");
                await Task.Delay(700);
                step($"{i}:   after 700 ms: {await Js(w, State)}");
            }
        },
        // I3 proof: the rules of a press, made on purpose. 1: a normal press. 2: a button that comes 1.5 s late (a warning, the run
        // continues). 3: a button that never comes (a miss, the run fails). Each test button records its click in data-hit.
        ["press-rules"] = async (tray, step) =>
        {
            var w = tray.Window;
            const string Add = "((id, ms) => setTimeout(() => { const b = document.createElement('button'); b.id = id; b.textContent = id; b.style.cssText = 'position:fixed;left:60px;top:300px;width:120px;height:40px;z-index:99'; b.onclick = () => b.dataset.hit = 'yes'; document.querySelector('.tray-panel').appendChild(b); }, ms))";
            string Hit(string id) => $"document.getElementById('{id}')?.dataset.hit === 'yes'";
            await Js(w, $"{Add}('t-now', 0)");
            await Task.Delay(200);
            step($"1 normal: {await Press(w, "#t-now")}");
            await Task.Delay(200);
            step($"1   hit {await Js(w, Hit("t-now"))}");
            await Js(w, "document.getElementById('t-now').remove()");
            await Js(w, $"{Add}('t-late', 1500)");
            step($"2 late (1.5 s): {await Press(w, "#t-late")}");
            await Task.Delay(200);
            step($"2   hit {await Js(w, Hit("t-late"))}");
            await Js(w, "document.getElementById('t-late')?.remove()");
            step($"3 never: {await Press(w, "#t-never")}");
        },
        // I5 spike: do posted key messages reach the page? Open the answer sheet, click the reply box, post "abc" to the host window,
        // read the box; then post "xyz" to each child window of WebView2, read the box.
        ["keys"] = async (tray, step) =>
        {
            var w = tray.Window;
            await Js(w, $"{Go}('cards')");
            await Until(async () => await Js(w, "!!document.querySelector('.acts [data-act=\"left\"]')") == "true", 3000);
            step($"open sheet: {await Press(w, ".acts [data-act=\"left\"]")}");
            await Until(async () => await Js(w, "!!document.querySelector('#sheet-text')") == "true", 3000);
            await Task.Delay(600);
            step($"click reply box: {await Press(w, "#sheet-text")}");
            await Task.Delay(300);
            step($"active element: {await Js(w, "document.activeElement && (document.activeElement.id || document.activeElement.tagName)")}");
            step($"focus window (thread of the host): {Focused(w.Handle)}");
            Type(w.Handle, "abc");
            await Task.Delay(400);
            step($"after keys to the host {w.Handle:x}: box = '{await Js(w, "document.querySelector('#sheet-text').value")}'");
            foreach (var (child, cls) in Children(w.Handle))
            {
                step($"child {child:x} class {cls}, focus of its thread {Focused(child)}");
                Type(child, "xyz");
                await Task.Delay(400);
                step($"after keys to child {child:x}: box = '{await Js(w, "document.querySelector('#sheet-text').value")}'");
            }
            if (Children(w.Handle).FirstOrDefault(c => c.Item2.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)).Item1 is var page and not 0)
                for (var i = 1; i <= 3; i++)
                {
                    Key(page, 0x09);
                    await Task.Delay(300);
                    step($"after Tab {i}: active element = {await Js(w, "(e => e ? e.tagName + (e.id ? '#' + e.id : '') + ' ' + (e.textContent || '').trim().slice(0, 20) : 'none')(document.activeElement)")}");
                }
            await Task.Delay(1000);
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

        // N2: one notice for each card that arrives while the tray is closed. A card that the panel showed gets none, also at a
        // minimize; a card that had its notice gets none again.
        ["notice-once"] = async (tray, step) =>
        {
            var w = tray.Window;
            var b = tray.Badge;
            async Task<string> Peek() => await Js(b, "!!document.querySelector('.tb-peek.on')") == "true" ? "peek" : "no peek";
            step($"open: post card A: {PostCard(tray, "Card A arrives while the panel shows")}");
            await Task.Delay(1000);
            step("dock");
            w.Request(TrayRequest.Dock);
            await Task.Delay(1500);
            step($"at the badge: {await Peek()} (expect no peek: the panel showed card A)");
            step($"post card B: {PostCard(tray, "Card B arrives at the badge")}");
            await Task.Delay(1500);
            step($"at the badge: {await Peek()} (expect peek: card B is new)");
            await Task.Delay(5000);
            step("open");
            w.Request(TrayRequest.Open);
            await Task.Delay(1500);
            step("dock");
            w.Request(TrayRequest.Dock);
            await Task.Delay(1500);
            step($"at the badge: {await Peek()} (expect no peek: card B had its notice)");
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

    /// <summary>Posts key down and key up of one virtual key.</summary>
    static void Key(nint hwnd, int vk)
    {
        var scan = (nint)MapVirtualKey((uint)vk, 0) << 16;
        _ = PostMessage(hwnd, 0x100, vk, scan | 1);
        _ = PostMessage(hwnd, 0x101, vk, scan | unchecked((int)0xC0000001));
    }

    /// <summary>Posts key down and key up for each letter (a to z only). The message loop makes the WM_CHAR, as for a real key.</summary>
    static void Type(nint hwnd, string text)
    {
        foreach (var c in text)
        {
            var vk = char.ToUpperInvariant(c);
            var scan = (nint)MapVirtualKey(vk, 0) << 16;
            _ = PostMessage(hwnd, 0x100, vk, scan | 1);
            _ = PostMessage(hwnd, 0x101, vk, scan | unchecked((int)0xC0000001));
        }
    }

    /// <summary>All child windows (all levels), with the class name.</summary>
    static List<(nint, string)> Children(nint parent)
    {
        var list = new List<(nint, string)>();
        for (var c = FindWindowEx(parent, 0, null, null); c != 0; c = FindWindowEx(parent, c, null, null))
        {
            var name = new System.Text.StringBuilder(256);
            _ = GetClassName(c, name, name.Capacity);
            list.Add((c, name.ToString()));
            list.AddRange(Children(c));
        }
        return list;
    }

    /// <summary>The focus window of the GUI thread that owns the window (0 if none).</summary>
    static nint FocusOf(nint hwnd)
    {
        var info = new GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out _), ref info) ? info.Focus : 0;
    }

    /// <summary>The focus window of the GUI thread that owns the window.</summary>
    static string Focused(nint hwnd)
    {
        var info = new GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<GuiThreadInfo>() };
        var thread = GetWindowThreadProcessId(hwnd, out var pid);
        return GetGUIThreadInfo(thread, ref info) ? $"{info.Focus:x} (thread {thread}, pid {pid})" : "unknown";
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct GuiThreadInfo
    {
        public int Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rectangle CaretRect;
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int max);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint type);

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
        // I17: the label in the corner shows the run code and the build, so a recording shows which session owns the tray.
        var (labelPass, labelMessage) = await LabelCheck(w);
        Step($"label: {(labelPass ? "PASS" : "FAIL")} {labelMessage}");
        using var stop = new CancellationTokenSource();
        var phases = Phases(tray, Step, stop.Token);
        var trace = tray.Services.GetRequiredService<Orchestrator.Web.Plugins.Js.Contracts.IJsTrace>();
        void OnTrace(string line) => Step($"trace {line[13..]}");
        trace.Written += OnTrace;
        await run(tray, Step);
        await stop.CancelAsync();
        await phases;
        trace.Written -= OnTrace;
        Step(Cursor.Position == pointer ? "pointer: not moved" : $"pointer: moved from {pointer} to {Cursor.Position}");
        File.WriteAllLines(Path.Combine(folder, "scenario.txt"), log.Concat(PressSummary()));
        return Faults.Count == 0 && PressMisses.Count == 0 && labelPass ? 0 : 1;
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
