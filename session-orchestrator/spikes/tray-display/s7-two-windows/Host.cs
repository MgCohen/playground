using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using Microsoft.Web.WebView2.Core;

namespace S7;

/// <summary>
/// A transparent top window that hosts WebView2 in composition mode. DirectComposition puts the page on the screen with
/// per-pixel alpha, so the empty parts of the page show the desktop. The window region sets where the clicks go to the
/// page; outside the region the clicks go to the window below. The host sends the mouse input to the page, and the page
/// header (app-region: drag) moves the window with the system move loop.
/// </summary>
public sealed partial class ClearForm : Form
{
    readonly IServiceProvider services;
    readonly Type root;
    CoreWebView2CompositionController? controller;
    Manager? manager;
    nint device, target, visual;

    public readonly TaskCompletionSource Ready = new();
    public CoreWebView2? Web => controller?.CoreWebView2;

    /// <summary>The shared environment, after the start.</summary>
    public CoreWebView2Environment? WebEnv { get; private set; }

    /// <summary>Shows or hides the WebView content. A hidden controller draws nothing and its page is "hidden".</summary>
    public void SetContentVisible(bool on)
    {
        if (controller is not null) controller.IsVisible = on;
    }

    /// <summary>One WebView2 environment for all windows of the process: one browser process, shared by the controllers.</summary>
    static Task<CoreWebView2Environment>? shared;

    public static Task<CoreWebView2Environment> Shared() =>
        shared ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "s7-two-windows-webview"));

    /// <summary>The name of the window in the trace: "panel" or "badge".</summary>
    public string Role { get; }

    public ClearForm(IServiceProvider services, Type root, string name)
    {
        this.services = services;
        this.root = root;
        Role = name;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "S7 " + name;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP: only DirectComposition draws the window.
            return p;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        device = Dcomp.CreateDevice();
        target = Dcomp.CreateTarget(device, Handle);
        visual = Dcomp.CreateVisual(device);
        Dcomp.SetRoot(target, visual);
        var env = await Shared();
        WebEnv = env;
        controller = await env.CreateCoreWebView2CompositionControllerAsync(Handle);
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.Bounds = new Rectangle(Point.Empty, ClientSize);
        controller.RootVisualTarget = Marshal.GetObjectForIUnknown(visual);
        controller.IsVisible = true;
        controller.CursorChanged += (_, _) => Cursor.Current = new Cursor(controller.Cursor);
        Dcomp.Commit(device);
        var web = controller.CoreWebView2;
        web.Settings.IsNonClientRegionSupportEnabled = true;
        web.NavigationCompleted += (_, _) => Ready.TrySetResult();
        await web.AddScriptToExecuteOnDocumentCreatedAsync("""
            window.external = {
              sendMessage: m => window.chrome.webview.postMessage(m),
              receiveMessage: cb => window.chrome.webview.addEventListener('message', e => cb(e.data))
            };
            """);
        var files = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        manager = new Manager(web, env, services, new FormDispatcher(this), files);
        await manager.AddRootComponentAsync(root, "#app", ParameterView.Empty);
        manager.Navigate("/");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (controller is not null) controller.Bounds = new Rectangle(Point.Empty, ClientSize);
    }

    /// <summary>The measure mode: the window and the WebView sizes.</summary>
    public object Sizes() => new
    {
        window = Bounds.ToString(),
        client = ClientSize.ToString(),
        controllerBounds = controller?.Bounds.ToString(),
        rasterizationScale = controller?.RasterizationScale,
        boundsMode = controller?.BoundsMode.ToString(),
        dpi = DeviceDpi,
    };

    /// <summary>The probe: the kind of the page region at a client point (caption for app-region: drag).</summary>
    public string RegionAt(Point client) => controller?.GetNonClientRegionAtPoint(client).ToString() ?? "none";

    /// <summary>The probe: the badge input steps.</summary>
    public readonly List<string> Steps = [];

    /// <summary>The trace file: each place change, enter, leave and click area change, with the time and the place.</summary>
    public static readonly string TraceFile = Path.Combine(AppContext.BaseDirectory, "s7-trace.log");

    public void Trace(string step)
    {
        try
        {
            File.AppendAllText(TraceFile, $"{DateTime.Now:HH:mm:ss.fff} [{Role}] {step}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Sets where the page takes the clicks, in window pixels. Outside these parts the clicks go to the window below.</summary>
    public void SetHitArea(Rectangle[] parts)
    {
        Trace($"click area: {string.Join(" + ", parts.Select(r => r.ToString()))} (window {Size.Width} x {Size.Height})");
        var region = new Region(Rectangle.Empty);
        foreach (var r in parts) region.Union(r);
        Region = region;
    }

    // The composition controller gets no input by itself: the host sends the mouse input to it.
    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x84, WmSetCursor = 0x20, WmMouseWheel = 0x20A, WmMouseLeave = 0x2A3, WmExitSizeMove = 0x232;
        if (m.Msg == WmExitSizeMove && !BadgeMode) PanelMoved?.Invoke();
        if (!BadgeMode && m.Msg == 0x2A3) Trace($"leave not seen by the badge code (over stays {over}) at {Cursor.Position}");
        if (controller is not null && BadgeMode && Badge(ref m)) return;
        if (controller is not null)
        {
            switch (m.Msg)
            {
                case WmNcHitTest:
                    {
                        // app-region: drag in the page is the caption: the system move loop drags the window.
                        var screen = new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
                        var kind = controller.GetNonClientRegionAtPoint(PointToClient(screen));
                        if (kind == CoreWebView2NonClientRegionKind.Caption)
                        {
                            m.Result = 2; // HTCAPTION
                            return;
                        }
                        break;
                    }
                case WmSetCursor when (m.LParam & 0xffff) == 1: // HTCLIENT
                    SetCursor(controller.Cursor);
                    m.Result = 1;
                    return;
                case >= 0x200 and <= 0x20E or WmMouseLeave:
                    Forward(m);
                    break;
            }
        }
        base.WndProc(ref m);
        if (m.Msg == WmMouseWheel) m.Result = 0;
    }

    // ===== The badge: the window is the badge at the right screen edge =====

    /// <summary>True while the window is the badge. The left button is then for the host: a click opens, a drag moves up and down.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool BadgeMode { get; set; }

    /// <summary>A click on the badge (no drag).</summary>
    public event Action? BadgeClicked;

    /// <summary>The end of a vertical drag of the badge.</summary>
    public event Action? BadgeMoved;

    /// <summary>The pointer comes onto the badge (true) or leaves it (false).</summary>
    public event Action<bool>? BadgeHover;

    /// <summary>The end of a drag of the panel (the system move loop of the header).</summary>
    public event Action? PanelMoved;

    /// <summary>
    /// A new place starts with no badge input: no hover, no press, no drag. The hover flag must not carry over from the last
    /// time in the badge place: its leave can come while the window is in the panel place, where the badge code does not see it.
    /// </summary>
    public void ResetBadgeInput()
    {
        over = false;
        down = null;
        dragging = false;
    }

    Point? down;
    int downTop;
    bool dragging, over;

    bool Badge(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x201: // WM_LBUTTONDOWN: the host keeps it; the page gets no click.
                down = Cursor.Position;
                downTop = Top;
                dragging = false;
                Capture = true;
                return true;
            case 0x200 when down is { } d && MouseButtons == MouseButtons.Left:
                {
                    var dy = Cursor.Position.Y - d.Y;
                    if (!dragging && Math.Abs(dy) < 4 * DeviceDpi / 96) return true;
                    dragging = true;
                    var area = Screen.FromHandle(Handle).WorkingArea;
                    Top = Math.Clamp(downTop + dy, area.Top, area.Bottom - Height);
                    return true;
                }
            case 0x200 when !over:
                Steps.Add("enter");
                Trace($"enter (over was {over}) at {Cursor.Position}");
                over = true;
                BadgeHover?.Invoke(true);
                return false;
            case 0x2A3: // WM_MOUSELEAVE
                Steps.Add("leave");
                Trace($"leave (over was {over}) at {Cursor.Position}");
                over = false;
                BadgeHover?.Invoke(false);
                return false;
            case 0x202 when down is not null: // WM_LBUTTONUP
                Capture = false;
                down = null;
                if (dragging) BadgeMoved?.Invoke();
                else BadgeClicked?.Invoke();
                dragging = false;
                return true;
        }
        return false;
    }

    bool tracking;

    void Forward(Message m)
    {
        var kind = m.Msg switch
        {
            0x200 => CoreWebView2MouseEventKind.Move,
            0x201 => CoreWebView2MouseEventKind.LeftButtonDown,
            0x202 => CoreWebView2MouseEventKind.LeftButtonUp,
            0x203 => CoreWebView2MouseEventKind.LeftButtonDoubleClick,
            0x204 => CoreWebView2MouseEventKind.RightButtonDown,
            0x205 => CoreWebView2MouseEventKind.RightButtonUp,
            0x207 => CoreWebView2MouseEventKind.MiddleButtonDown,
            0x208 => CoreWebView2MouseEventKind.MiddleButtonUp,
            0x20A => CoreWebView2MouseEventKind.Wheel,
            0x2A3 => CoreWebView2MouseEventKind.Leave,
            _ => (CoreWebView2MouseEventKind?)null,
        };
        if (kind is not { } k) return;
        var keys = (CoreWebView2MouseEventVirtualKeys)(int)(m.WParam & 0xffff);
        var data = k == CoreWebView2MouseEventKind.Wheel ? (uint)(short)((m.WParam >> 16) & 0xffff) : 0u;
        var p = new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
        if (k == CoreWebView2MouseEventKind.Wheel) p = PointToClient(p);
        if (k == CoreWebView2MouseEventKind.Leave) p = Point.Empty;
        if (k is CoreWebView2MouseEventKind.LeftButtonDown or CoreWebView2MouseEventKind.RightButtonDown) Capture = true;
        if (k is CoreWebView2MouseEventKind.LeftButtonUp or CoreWebView2MouseEventKind.RightButtonUp) Capture = false;
        if (k == CoreWebView2MouseEventKind.Move && !tracking)
        {
            var track = new TrackMouse { Size = Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = Handle };
            tracking = TrackMouseEvent(ref track);
        }
        if (k == CoreWebView2MouseEventKind.Leave) tracking = false;
        controller!.SendMouseInput(k, keys, data, p);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrackMouse
    {
        public int Size;
        public int Flags;
        public nint Window;
        public int Hover;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(ref TrackMouse track);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    /// <summary>The Blazor host on the composition controller: it serves the files and carries the messages.</summary>
    sealed class Manager : WebViewManager
    {
        static readonly Uri Base = new("https://0.0.0.1/");
        readonly CoreWebView2 web;

        public Manager(CoreWebView2 web, CoreWebView2Environment env, IServiceProvider services, Dispatcher dispatcher, IFileProvider files)
            : base(services, dispatcher, Base, files, new JSComponentConfigurationStore(), "index.html")
        {
            this.web = web;
            web.AddWebResourceRequestedFilter(Base + "*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) =>
            {
                var document = e.ResourceContext == CoreWebView2WebResourceContext.Document;
                if (!TryGetResponseContent(e.Request.Uri, document, out var code, out var text, out var content, out var headers)) return;
                e.Response = env.CreateWebResourceResponse(content, code, text, string.Join("\r\n", headers.Select(h => $"{h.Key}: {h.Value}")));
            };
            web.WebMessageReceived += (_, e) => MessageReceived(new Uri(e.Source), e.TryGetWebMessageAsString());
        }

        protected override void NavigateCore(Uri absoluteUri) => web.Navigate(absoluteUri.ToString());

        protected override void SendMessage(string message) => web.PostWebMessageAsString(message);
    }

    /// <summary>The Blazor dispatcher on the UI thread of the window.</summary>
    sealed class FormDispatcher(Control control) : Dispatcher
    {
        public override bool CheckAccess() => !control.InvokeRequired;

        public override Task InvokeAsync(Action work) => InvokeAsync(() => { work(); return Task.CompletedTask; });

        public override Task InvokeAsync(Func<Task> work) => InvokeAsync<object?>(async () => { await work(); return null; });

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> work) => InvokeAsync(() => Task.FromResult(work()));

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> work)
        {
            if (CheckAccess()) return work();
            var done = new TaskCompletionSource<TResult>();
            control.BeginInvoke(async () =>
            {
                try
                {
                    done.SetResult(await work());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            });
            return done.Task;
        }
    }
}

/// <summary>The DirectComposition calls of the host (from spike E-5).</summary>
static unsafe partial class Dcomp
{
    static readonly Guid DesktopDevice = new("5f4633fe-1e08-4cb8-8c75-ce24333f5602");

    [LibraryImport("dcomp.dll")]
    private static partial int DCompositionCreateDevice2(nint renderingDevice, in Guid iid, out nint device);

    static nint* Vtbl(nint obj) => *(nint**)obj;

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public static nint CreateDevice()
    {
        Check(DCompositionCreateDevice2(0, DesktopDevice, out var device));
        return device;
    }

    // IDCompositionDesktopDevice: IUnknown (3), IDCompositionDevice2 (Commit = 3, CreateVisual = 6), CreateTargetForHwnd = 24.
    public static void Commit(nint device) => Check(((delegate* unmanaged[Stdcall]<nint, int>)Vtbl(device)[3])(device));

    public static nint CreateVisual(nint device)
    {
        nint visual;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(device)[6])(device, &visual));
        return visual;
    }

    public static nint CreateTarget(nint device, nint hwnd)
    {
        nint target;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Vtbl(device)[24])(device, hwnd, 1, &target));
        return target;
    }

    // IDCompositionTarget: SetRoot = 3.
    public static void SetRoot(nint target, nint visual) => Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Vtbl(target)[3])(target, visual));
}
