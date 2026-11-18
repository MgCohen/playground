using System.Runtime.InteropServices;
using System.Text;

namespace Orchestrator.Tray;

/// <summary>
/// V21: the trace of a named instance for the grey box defect. It logs the window order of the tray windows at each
/// activation and z-order change of the widget, each failure of the WebView2 processes, and a heartbeat each 2 s.
/// </summary>
public sealed partial class WidgetForm
{
    readonly System.Windows.Forms.Timer heartbeat = new() { Interval = 2000 };
    string lastBeat = "";

    void StartTrace()
    {
        if (!Faults.Tracing) return;
        view.BlazorWebViewInitialized += (_, e) =>
        {
            var core = e.WebView.CoreWebView2;
            core.ProcessFailed += (_, f) => Faults.Trace($"webview process failed: {f.ProcessFailedKind}, reason {f.Reason}, exit {f.ExitCode}, {f.ProcessDescription}");
            core.NavigationStarting += (_, n) => Faults.Trace($"webview navigation start: {n.Uri}");
            core.NavigationCompleted += (_, n) => Faults.Trace($"webview navigation end: ok {n.IsSuccess}, {n.WebErrorStatus}");
        };
        heartbeat.Tick += (_, _) =>
        {
            if (!Visible) return;
            var beat = $"heartbeat: {ZOrder()}";
            if (beat == lastBeat) return;
            lastBeat = beat;
            Faults.Trace(beat);
        };
        heartbeat.Start();
    }

    /// <summary>The visible top windows from the front down to the widget, by name; the ones of this process in brackets.</summary>
    string ZOrder()
    {
        var parts = new List<string>();
        _ = GetWindowThreadProcessId(Handle, out var me);
        for (var h = GetTopWindow(0); h != 0 && parts.Count < 40; h = GetWindow(h, 2))
        {
            if (!IsWindowVisible(h)) continue;
            _ = GetWindowThreadProcessId(h, out var pid);
            var text = new StringBuilder(80);
            _ = GetWindowText(h, text, 80);
            var name = text.Length > 0 ? text.ToString() : "(no title)";
            parts.Add(pid == me ? $"[{name}]" : name);
            if (h == Handle) break;
        }
        var web = Web is null ? "no webview" : "webview ok";
        return $"{string.Join(" > ", parts)}; region {(GetWindowRgnBox(Handle, out var box) == 0 ? "none" : $"{box.Right - box.Left}x{box.Bottom - box.Top}")}; {web}";
    }

    void TraceMessage(ref Message m)
    {
        if (!Faults.Tracing) return;
        const int WmActivate = 0x0006, WmWindowPosChanged = 0x0047, WmNcActivate = 0x0086;
        if (m.Msg == WmActivate) Faults.Trace($"widget activate {(int)m.WParam & 0xFFFF}: {ZOrder()}");
        else if (m.Msg == WmNcActivate) Faults.Trace($"widget nc activate {(int)m.WParam}");
        else if (m.Msg == WmWindowPosChanged) BeginInvoke(() => Faults.Trace($"widget pos changed: {ZOrder()}"));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Box
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetTopWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextRaw(nint hwnd, [Out] char[] text, int max);

    static int GetWindowText(nint hwnd, StringBuilder text, int max)
    {
        var buffer = new char[max];
        var n = GetWindowTextRaw(hwnd, buffer, max);
        text.Append(buffer, 0, Math.Max(0, n));
        return n;
    }

    [LibraryImport("gdi32.dll")]
    private static partial int GetRgnBox(nint region, out Box box);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRgnBox(nint hwnd, out Box box);
}
