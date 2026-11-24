using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using Microsoft.Web.WebView2.Core;

namespace PingPong.Win;

/// <summary>A transparent window: WebView2 in visual hosting on DirectComposition, with a small Blazor host of its own.</summary>
public partial class ClearForm : Form
{
    readonly IServiceProvider services;
    readonly Type root;
    CoreWebView2CompositionController? controller;
    Manager? manager;

    public CoreWebView2? Web => controller?.CoreWebView2;

    public ClearForm(IServiceProvider services, Type root)
    {
        this.services = services;
        this.root = root;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP
            return p;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { await Start(); }
        catch (Exception x) { Console.Error.WriteLine(x); }
    }

    async Task Start()
    {
        var device = Dcomp.CreateDevice();
        var target = Dcomp.CreateTarget(device, Handle);
        var visual = Dcomp.CreateVisual(device);
        Dcomp.SetRoot(target, visual);
        var env = await CoreWebView2Environment.CreateAsync();
        controller = await env.CreateCoreWebView2CompositionControllerAsync(Handle);
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.Bounds = new Rectangle(Point.Empty, ClientSize);
        controller.RootVisualTarget = Marshal.GetObjectForIUnknown(visual);
        controller.IsVisible = true;
        Dcomp.Commit(device);
        var web = controller.CoreWebView2;
        await web.AddScriptToExecuteOnDocumentCreatedAsync("""
            window.external = {
              sendMessage: m => window.chrome.webview.postMessage(m),
              receiveMessage: cb => window.chrome.webview.addEventListener('message', e => cb(e.data))
            };
            """);
        manager = new Manager(web, env, services, new FormDispatcher(this), new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")));
        await manager.AddRootComponentAsync(root, "#app", ParameterView.Empty);
        manager.Navigate("/");
    }

    /// <summary>The part of the window that shows and takes clicks, in CSS pixels.</summary>
    public void SetArea(Rectangle css)
    {
        var s = DeviceDpi / 96.0;
        Region = new Region(new Rectangle((int)(css.X * s), (int)(css.Y * s), (int)(css.Width * s), (int)(css.Height * s)));
    }

    protected override void WndProc(ref Message m)
    {
        if (controller is not null && m.Msg is >= 0x200 and <= 0x20A or 0x2A3) Forward(m);
        base.WndProc(ref m);
    }

    void Forward(Message m)
    {
        CoreWebView2MouseEventKind? kind = m.Msg switch
        {
            0x200 => CoreWebView2MouseEventKind.Move,
            0x201 => CoreWebView2MouseEventKind.LeftButtonDown,
            0x202 => CoreWebView2MouseEventKind.LeftButtonUp,
            0x2A3 => CoreWebView2MouseEventKind.Leave,
            _ => null,
        };
        if (kind is not { } k) return;
        var p = k == CoreWebView2MouseEventKind.Leave ? Point.Empty : new Point((short)(m.LParam & 0xffff), (short)((m.LParam >> 16) & 0xffff));
        controller!.SendMouseInput(k, (CoreWebView2MouseEventVirtualKeys)(int)(m.WParam & 0xffff), 0, p);
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

    sealed class FormDispatcher(Control control) : Dispatcher
    {
        public override bool CheckAccess() => !control.InvokeRequired;
        public override Task InvokeAsync(Action work) => InvokeAsync(() => { work(); return Task.CompletedTask; });
        public override Task InvokeAsync(Func<Task> work) => InvokeAsync<object?>(async () => { await work(); return null; });
        public override Task<T> InvokeAsync<T>(Func<T> work) => InvokeAsync(() => Task.FromResult(work()));

        public override Task<T> InvokeAsync<T>(Func<Task<T>> work)
        {
            if (CheckAccess()) return work();
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            control.BeginInvoke(async () =>
            {
                try { done.SetResult(await work()); }
                catch (Exception e) { done.SetException(e); }
            });
            return done.Task;
        }
    }

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

        public static void SetRoot(nint target, nint visual) => Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Vtbl(target)[3])(target, visual));
    }
}
