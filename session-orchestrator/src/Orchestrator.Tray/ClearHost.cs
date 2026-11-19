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
/// The tray has two of these windows: the panel (<see cref="TrayWindow"/>) and the badge (<see cref="BadgeWindow"/>). They share one
/// WebView2 environment, so one browser process serves both. Each window has its own root component.
/// </summary>
public partial class ClearHost : Form
{
    readonly IServiceProvider services;
    readonly Type root;
    CoreWebView2CompositionController? controller;
    Manager? manager;
    nint device, target, visual;

    /// <summary>The page is loaded and the root components run.</summary>
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The WebView2 control after its start. The self-test uses it for the page checks and the pictures.</summary>
    public CoreWebView2? Web => controller?.CoreWebView2;

    // One environment for all windows of the process. The user data folder comes from WEBVIEW2_USER_DATA_FOLDER (Program.cs),
    // one for each tray instance.
    static Task<CoreWebView2Environment>? shared;

    static Task<CoreWebView2Environment> Environment() => shared ??= CoreWebView2Environment.CreateAsync();

    public ClearHost(IServiceProvider services, Type root)
    {
        this.services = services;
        this.root = root;
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
        var env = await Environment();
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
        await manager.AddRootComponentAsync(root, "#app", ParameterView.Empty);
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
        if (m.Msg == 0x232) MoveEnded?.Invoke(); // WM_EXITSIZEMOVE: the end of a drag of the header.
        if (controller is not null && HostInput(ref m)) return;
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

    /// <summary>The end of a drag of the window with the system move loop (a header drag).</summary>
    public event Action? MoveEnded;

    /// <summary>The input that the window keeps for itself (true): the page does not get it. The badge window uses it.</summary>
    protected virtual bool HostInput(ref Message m) => false;

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
