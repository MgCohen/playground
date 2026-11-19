using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// A transparent top window that hosts the Blazor views with WebView2 in composition mode. DirectComposition puts the page on the
/// screen with per-pixel alpha, so the empty parts of the page show the desktop. The window region sets where the window takes
/// the clicks (and what it shows); outside the region the clicks go to the window below. The host sends the mouse input to the
/// page, and an <c>app-region: drag</c> part of the page (the header) moves the window with the system move loop.
/// In the badge mode the left button is for the host: a click is <see cref="BadgeClicked"/>, a drag moves the window up and down.
/// </summary>
public partial class ClearHost : Form
{
    readonly IServiceProvider services;
    CoreWebView2CompositionController? controller;
    Manager? manager;
    nint device, target, visual;

    /// <summary>The page is loaded and the root components run.</summary>
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The WebView2 control after its start. The self-test uses it for the page checks and the pictures.</summary>
    public CoreWebView2? Web => controller?.CoreWebView2;

    public ClearHost(IServiceProvider services)
    {
        this.services = services;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
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
        try
        {
            await Start();
        }
        catch (Exception x) when (x is COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            Faults.Add(x, "WebView2 start");
            Ready.TrySetException(x);
        }
    }

    async Task Start()
    {
        device = Dcomp.CreateDevice();
        target = Dcomp.CreateTarget(device, Handle);
        visual = Dcomp.CreateVisual(device);
        Dcomp.SetRoot(target, visual);
        // The user data folder comes from WEBVIEW2_USER_DATA_FOLDER (Program.cs), one for each tray instance.
        var env = await CoreWebView2Environment.CreateAsync();
        controller = await env.CreateCoreWebView2CompositionControllerAsync(Handle);
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.Bounds = new Rectangle(Point.Empty, ClientSize);
        controller.RootVisualTarget = Marshal.GetObjectForIUnknown(visual);
        controller.IsVisible = true;
        controller.CursorChanged += (_, _) => Cursor.Current = new Cursor(controller.Cursor);
        Dcomp.Commit(device);
        var web = controller.CoreWebView2;
        web.Settings.IsNonClientRegionSupportEnabled = true;
        web.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
        web.ProcessFailed += (_, f) => Faults.Trace($"webview process failed: {f.ProcessFailedKind}, reason {f.Reason}, exit {f.ExitCode}");
        web.NavigationCompleted += (_, _) => Ready.TrySetResult();
        await web.AddScriptToExecuteOnDocumentCreatedAsync("""
            window.external = {
              sendMessage: m => window.chrome.webview.postMessage(m),
              receiveMessage: cb => window.chrome.webview.addEventListener('message', e => cb(e.data))
            };
            """);
        manager = new Manager(web, env, services, new FormDispatcher(this), StaticFiles.Provider());
        await manager.AddRootComponentAsync(typeof(Orchestrator.Web.Components.Tray.TrayRoot), "#app", ParameterView.Empty);
        await manager.AddRootComponentAsync(typeof(HeadOutlet), "head::after", ParameterView.Empty);
        manager.Navigate("/");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (controller is not null) controller.Bounds = new Rectangle(Point.Empty, ClientSize);
    }

    // The page has the focus of the keyboard when the window is active.
    protected override void OnActivated(EventArgs e)
    {
        Faults.Trace("window activated");
        base.OnActivated(e);
        controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    /// <summary>Sets where the window takes the clicks and what it shows, in window pixels.</summary>
    public void SetHitArea(Rectangle area)
    {
        Faults.Trace($"click area {area} (window {Bounds})");
        Region = new Region(area);
        HitArea = area;
    }

    /// <summary>The click area of the last <see cref="SetHitArea"/>. The self-test reads it.</summary>
    public Rectangle HitArea { get; private set; }

    // ===== Input: the composition controller gets no input by itself; the host sends the mouse input to it =====

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x84, WmSetCursor = 0x20, WmMouseLeave = 0x2A3, WmSysCommand = 0x112, ScMaximize = 0xF030;
        if (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScMaximize) return;
        if (m.Msg == 0x232) Faults.Trace("move loop end");
        if (m.Msg == 0x232 && !BadgeMode) PanelMoved?.Invoke(); // WM_EXITSIZEMOVE: the end of a drag of the header.
        if (controller is not null && BadgeMode && Badge(ref m)) return;
        if (controller is not null)
        {
            switch (m.Msg)
            {
                case WmNcHitTest:
                    {
                        // app-region: drag in the page is the caption: the system move loop drags the window.
                        var screen = new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
                        if (controller.GetNonClientRegionAtPoint(PointToClient(screen)) == CoreWebView2NonClientRegionKind.Caption)
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
    }

    bool tracking;

    void Forward(Message m)
    {
        CoreWebView2MouseEventKind? kind = m.Msg switch
        {
            0x200 => CoreWebView2MouseEventKind.Move,
            0x201 => CoreWebView2MouseEventKind.LeftButtonDown,
            0x202 => CoreWebView2MouseEventKind.LeftButtonUp,
            0x203 => CoreWebView2MouseEventKind.LeftButtonDoubleClick,
            0x204 => CoreWebView2MouseEventKind.RightButtonDown,
            0x205 => CoreWebView2MouseEventKind.RightButtonUp,
            0x206 => CoreWebView2MouseEventKind.RightButtonDoubleClick,
            0x207 => CoreWebView2MouseEventKind.MiddleButtonDown,
            0x208 => CoreWebView2MouseEventKind.MiddleButtonUp,
            0x20A => CoreWebView2MouseEventKind.Wheel,
            0x20E => CoreWebView2MouseEventKind.HorizontalWheel,
            0x2A3 => CoreWebView2MouseEventKind.Leave,
            _ => null,
        };
        if (kind is not { } k) return;
        var keys = (CoreWebView2MouseEventVirtualKeys)(int)(m.WParam & 0xffff);
        var wheel = k is CoreWebView2MouseEventKind.Wheel or CoreWebView2MouseEventKind.HorizontalWheel;
        var data = wheel ? (uint)(short)((m.WParam >> 16) & 0xffff) : 0u;
        var p = new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
        // A wheel message has a screen point; the other messages have a client point.
        if (wheel) p = PointToClient(p);
        if (k == CoreWebView2MouseEventKind.Leave) p = Point.Empty;
        if (k is CoreWebView2MouseEventKind.LeftButtonDown or CoreWebView2MouseEventKind.RightButtonDown or CoreWebView2MouseEventKind.MiddleButtonDown) Capture = true;
        if (k is CoreWebView2MouseEventKind.LeftButtonUp or CoreWebView2MouseEventKind.RightButtonUp or CoreWebView2MouseEventKind.MiddleButtonUp) Capture = false;
        if (k == CoreWebView2MouseEventKind.Move && !tracking)
        {
            var track = new TrackMouse { Size = Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = Handle };
            tracking = TrackMouseEvent(ref track);
        }
        if (k == CoreWebView2MouseEventKind.Leave) tracking = false;
        controller!.SendMouseInput(k, keys, data, p);
    }

    // ===== The badge mode =====

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

    Point? down;
    int downTop;
    bool dragging, over;

    /// <summary>
    /// A new place starts with no badge input: no hover, no press, no drag. The hover flag must not carry over from the last
    /// time at the badge place: its leave can come while the window is at the panel place, where the badge code does not see it.
    /// </summary>
    public void ResetBadgeInput()
    {
        over = false;
        down = null;
        dragging = false;
    }

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
                    // N1: a move of 5 px starts the drag; a smaller move is still a click.
                    if (!dragging && Math.Abs(dy) < 5 * DeviceDpi / 96) return true;
                    dragging = true;
                    var area = Screen.FromHandle(Handle).WorkingArea;
                    Top = Math.Clamp(downTop + dy, area.Top, Math.Max(area.Top, area.Bottom - Height));
                    return true;
                }
            case 0x200 when !over:
                over = true;
                BadgeHover?.Invoke(true);
                return false;
            case 0x2A3: // WM_MOUSELEAVE
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            controller?.Close();
            if (manager is not null) _ = manager.DisposeAsync().AsTask();
        }
        base.Dispose(disposing);
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

        public override Task InvokeAsync(Action workItem) => InvokeAsync(() => { workItem(); return Task.CompletedTask; });

        public override Task InvokeAsync(Func<Task> workItem) => InvokeAsync<object?>(async () => { await workItem(); return null; });

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => InvokeAsync(() => Task.FromResult(workItem()));

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            if (CheckAccess()) return workItem();
            var done = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            control.BeginInvoke(async () =>
            {
                try
                {
                    done.SetResult(await workItem());
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

/// <summary>The DirectComposition calls of the host (spike E-5).</summary>
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
