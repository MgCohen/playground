using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

// Usage: capture <hwnd hex> <out.png> [border 0|1] [wait ms]
// Captures one frame of one window with Windows.Graphics.Capture and writes a PNG (with alpha).
static partial class P
{
    static async Task<int> Main(string[] a)
    {
        var hwnd = (nint)Convert.ToInt64(a[0], 16);
        var outFile = Path.GetFullPath(a[1]);
        var border = a.Length > 2 && a[2] == "1";
        var waitMs = a.Length > 3 ? int.Parse(a[3]) : 3000;
        SetProcessDpiAwarenessContext(-4);
        Console.WriteLine($"supported: {GraphicsCaptureSession.IsSupported()}");
        if (!border)
        {
            var access = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            Console.WriteLine($"borderless access: {access}");
        }
        var item = CreateItem(hwnd);
        Console.WriteLine($"item: {item.DisplayName} {item.Size.Width}x{item.Size.Height}");
        var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        var got = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
        pool.FrameArrived += (p, _) => { var f = p.TryGetNextFrame(); frames++; if (frames >= 1 && !got.TrySetResult(f)) f.Dispose(); else if (frames < 1) f.Dispose(); };
        using var session = pool.CreateCaptureSession(item);
        try { session.IsBorderRequired = border; } catch (Exception e) { Console.WriteLine($"IsBorderRequired: {e.Message}"); }
        try { session.IsCursorCaptureEnabled = false; } catch { }
        session.StartCapture();
        var done = await Task.WhenAny(got.Task, Task.Delay(waitMs));
        if (done != got.Task) { Console.WriteLine($"frames: {frames} (timeout {waitMs} ms)"); return 1; }
        using var frame = got.Task.Result;
        await Task.Delay(400);
        try { BorderCheck(hwnd, Path.ChangeExtension(outFile, ".screen.png")); } catch (Exception e) { Console.WriteLine($"screen copy: not possible ({e.GetType().Name})"); }
        Console.WriteLine($"frames: {frames}, content {frame.ContentSize.Width}x{frame.ContentSize.Height}");
        var bmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
        using var stream = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        enc.SetSoftwareBitmap(bmp);
        await enc.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        File.WriteAllBytes(outFile, bytes);
        // Alpha stats: how many pixels are transparent, partial, opaque.
        var buf = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyToBuffer(buf.AsBuffer());
        int t = 0, part = 0, op = 0;
        for (var i = 3; i < buf.Length; i += 4) { if (buf[i] == 0) t++; else if (buf[i] == 255) op++; else part++; }
        Console.WriteLine($"alpha: transparent {t}, partial {part}, opaque {op}");
        Console.WriteLine($"wrote {outFile}");
        return 0;
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    static GraphicsCaptureItem CreateItem(nint hwnd)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
        var ptr = interop.CreateForWindow(hwnd, iid);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
        Marshal.Release(ptr);
        return item;
    }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(nint v);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint h, int a, out RECT r, int size);

    // Copies the screen around the window (12 px outside its bounds) and counts yellow pixels in that band.
    static void BorderCheck(nint hwnd, string file)
    {
        DwmGetWindowAttribute(hwnd, 9, out var r, 16);
        const int m = 12;
        var rect = new System.Drawing.Rectangle(r.L - m, r.T - m, r.R - r.L + 2 * m, r.B - r.T + 2 * m);
        using var bmp = new System.Drawing.Bitmap(rect.Width, rect.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size);
        int yellow = 0, band = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var inside = x >= m + 4 && x < bmp.Width - m - 4 && y >= m + 4 && y < bmp.Height - m - 4;
                if (inside) continue;
                band++;
                var c = bmp.GetPixel(x, y);
                if (c.R > 200 && c.G > 160 && c.B < 90) yellow++;
            }
        bmp.Save(file);
        Console.WriteLine($"window {r.L},{r.T} {r.R - r.L}x{r.B - r.T}; edge band yellow {yellow} of {band} px; screen copy {file}");
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint numLevels, uint sdk, out nint device, out int level, out nint context);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20 /* BGRA */, 0, 0, 7, out var d3d, out _, out var ctx));
        var dxgiIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in dxgiIid, out var dxgi));
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
        Marshal.Release(dxgi); Marshal.Release(ctx); Marshal.Release(d3d);
        var dev = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);
        return dev;
    }
}
