using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The scenario run (--selftest folder --scenario name): a few focused actions for the proof of one issue, repeated a few times,
/// 5 to 10 s in total. Window messages only (the pointer of the user does not move). It writes scenario.txt with the time of
/// each step, so the frames of a recording can be matched to the steps.
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
        void Step(string text) => log.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        Step($"scenario {name}");
        await run(tray, Step);
        Step(Cursor.Position == pointer ? "pointer: not moved" : $"pointer: moved from {pointer} to {Cursor.Position}");
        File.WriteAllLines(Path.Combine(folder, "scenario.txt"), log);
        return Faults.Count == 0 ? 0 : 1;
    }

    /// <summary>A pointer move onto the element, with no click: a window message.</summary>
    static async Task<string> Point(TrayWindow w, string selector)
    {
        if (await Center(w, selector) is not { } at) return $"no element {selector}";
        Post(w, 0x200, 0, at);
        return "moved (window message)";
    }
}
