using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

// Usage: stream monitor|offscreen <seconds> <out folder>, or stream mp4 <frame folder> <out.mp4>
// Records a stream with Windows.Graphics.Capture: the time of each frame, and every 20th frame as a small PNG.
sealed class LayeredForm : Form
{
    public LayeredForm() { FormBorderStyle = FormBorderStyle.None; }
    protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80000; return c; } }
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(nint h, nint dst, ref PT pos, ref SZ size, nint src, ref PT srcPos, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    public void Push(int n)
    {
        using var bmp = new System.Drawing.Bitmap(400, 200, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            g.FillRectangle(System.Drawing.Brushes.OrangeRed, (n * 6) % 360, 60, 40, 80);
            g.DrawString($"tick {n}", new System.Drawing.Font("Segoe UI", 20), System.Drawing.Brushes.Black, 10, 10);
        }
        var hb = bmp.GetHbitmap(System.Drawing.Color.FromArgb(0)); var dc = CreateCompatibleDC(0); var old = SelectObject(dc, hb);
        var pos = new PT { X = Left, Y = Top }; var size = new SZ { W = 400, H = 200 }; var src = new PT(); var bl = new BLEND { Alpha = 255, Format = 1 };
        UpdateLayeredWindow(Handle, 0, ref pos, ref size, dc, ref src, 0, ref bl, 2);
        SelectObject(dc, old); DeleteObject(hb); DeleteDC(dc);
    }
}

static partial class P
{
    [STAThread]
    static int Main(string[] a)
    {
        SetProcessDpiAwarenessContext(-4);
        if (a[0] == "mp4") return Mp4(a[1], a[2]).GetAwaiter().GetResult();
        var mode = a[0]; var secs = double.Parse(a[1]); var dir = Path.GetFullPath(a[2]); Directory.CreateDirectory(dir);
        Form? form = null;
        GraphicsCaptureItem item;
        if (mode == "offscreen")
        {
            // A layered test window at x = -3000 that pushes a new picture each 16 ms (UpdateLayeredWindow), as a compositor client does.
            var n = 0;
            form = new LayeredForm { StartPosition = FormStartPosition.Manual, Bounds = new System.Drawing.Rectangle(-3000, 300, 400, 200), ShowInTaskbar = false };
            var lf = (LayeredForm)form;
            var t = new System.Windows.Forms.Timer { Interval = 16 };
            t.Tick += (_, _) => { n++; lf.Push(n); };
            form.Show(); lf.Push(0); t.Start();
            Application.DoEvents();
            item = CreateItem(form.Handle, false);
        }
        else item = CreateItem(MonitorFromPoint(0, 0, 1), true);
        Console.WriteLine($"borderless: {GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().Result}");
        Console.WriteLine($"item: {item.DisplayName} {item.Size.Width}x{item.Size.Height}");
        var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        var times = new List<double>(); var saves = new List<Task>(); TimeSpan? first = null; var count = 0;
        pool.FrameArrived += (p, _) =>
        {
            using var f = p.TryGetNextFrame();
            first ??= f.SystemRelativeTime;
            times.Add((f.SystemRelativeTime - first.Value).TotalMilliseconds);
            if (true)
            {
                var bmp = SoftwareBitmap.CreateCopyFromSurfaceAsync(f.Surface, BitmapAlphaMode.Premultiplied).AsTask().Result;
                var file = Path.Combine(dir, $"frame-{count++:D4}.png");
                saves.Add(Save(bmp, file, mode == "monitor" ? 0.25 : 1));
            }
        };
        using var session = pool.CreateCaptureSession(item);
        session.IsBorderRequired = false;
        session.StartCapture();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < secs) { Application.DoEvents(); Thread.Sleep(5); }
        session.Dispose();
        Task.WaitAll(saves.ToArray());
        form?.Close();
        var gaps = times.Zip(times.Skip(1), (x, y) => y - x).ToList();
        Console.WriteLine($"frames: {times.Count} in {secs} s = {times.Count / secs:F1} fps; gap median {(gaps.Count > 0 ? gaps.OrderBy(g => g).ElementAt(gaps.Count / 2) : 0):F1} ms, max {(gaps.Count > 0 ? gaps.Max() : 0):F1} ms");
        File.WriteAllLines(Path.Combine(dir, "times.txt"), times.Select(t => t.ToString("F1")));
        return 0;
    }

    // An MP4 from the saved frames. Each frame stays on the screen until the next frame came, so the video has the real timing.
    static async Task<int> Mp4(string dir, string output)
    {
        dir = Path.GetFullPath(dir);
        var times = File.ReadAllLines(Path.Combine(dir, "times.txt")).Select(double.Parse).ToList();
        var files = Directory.GetFiles(dir, "frame-*.png").Order().ToList();
        var n = Math.Min(times.Count, files.Count);
        var composition = new Windows.Media.Editing.MediaComposition();
        for (var i = 0; i < n; i++)
        {
            var ms = i + 1 < n ? times[i + 1] - times[i] : 33;
            var image = await Windows.Storage.StorageFile.GetFileFromPathAsync(files[i]);
            composition.Clips.Add(await Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(image, TimeSpan.FromMilliseconds(Math.Max(ms, 1))));
        }
        using (var first = System.Drawing.Image.FromFile(files[0]))
        {
            var target = Path.GetFullPath(output);
            File.WriteAllBytes(target, []);
            var outFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(target);
            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.HD720p);
            profile.Video.Width = (uint)(first.Width & ~1); profile.Video.Height = (uint)(first.Height & ~1);
            profile.Video.FrameRate.Numerator = 60; profile.Video.FrameRate.Denominator = 1; profile.Video.Bitrate = 4_000_000; profile.Audio = null;
            var result = await composition.RenderToFileAsync(outFile, Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);
            Console.WriteLine($"{n} frames, {first.Width}x{first.Height}, {times[n - 1] / 1000:F1} s: {result}; {target}: {new FileInfo(target).Length / 1024} KB");
            return result == Windows.Media.Transcoding.TranscodeFailureReason.None ? 0 : 1;
        }
    }

    static async Task Save(SoftwareBitmap bmp, string file, double scale)
    {
        using var stream = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        enc.SetSoftwareBitmap(bmp);
        if (scale < 1) { enc.BitmapTransform.ScaledWidth = (uint)(bmp.PixelWidth * scale); enc.BitmapTransform.ScaledHeight = (uint)(bmp.PixelHeight * scale); }
        await enc.FlushAsync();
        var bytes = new byte[stream.Size]; stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        File.WriteAllBytes(file, bytes);
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop { nint CreateForWindow(nint window, in Guid iid); nint CreateForMonitor(nint monitor, in Guid iid); }

    static GraphicsCaptureItem CreateItem(nint h, bool monitor)
    {
        var interop = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem").AsInterface<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var ptr = monitor ? interop.CreateForMonitor(h, iid) : interop.CreateForWindow(h, iid);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr); Marshal.Release(ptr); return item;
    }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(nint v);
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(long pt, uint flags);
    static nint MonitorFromPoint(int x, int y, uint f) => MonitorFromPoint(((long)y << 32) | (uint)x, f);
    [LibraryImport("d3d11.dll")] private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint numLevels, uint sdk, out nint device, out int level, out nint context);
    [LibraryImport("d3d11.dll")] private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var d3d, out _, out var ctx));
        var dxgiIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in dxgiIid, out var dxgi));
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var insp));
        Marshal.Release(dxgi); Marshal.Release(ctx); Marshal.Release(d3d);
        var dev = MarshalInterface<IDirect3DDevice>.FromAbi(insp); Marshal.Release(insp); return dev;
    }
}
