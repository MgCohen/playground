using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;
using Color = System.Drawing.Color;
using Form = System.Windows.Forms.Form;
using System.Windows.Media.Imaging;

namespace Recorder;

/// <summary>
/// Records one screen area as a looping GIF: a picture at a fixed rate, with the pointer drawn in, made smaller by a scale.
/// With --backdrop, a plain grey window covers the area behind the recorded windows (they must be on top), so no other window
/// shows; use it only when a check needs it (an empty frame count, a shadow at the edge). With no backdrop, the screen stays as it
/// is and the user can work. The frames at the start that do not change (the time before the recorded app shows) go. The WPF GIF encoder writes the frames;
/// then the file gets the delay of each frame and the loop block.
/// </summary>
static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // "mp4 in.gif out.mp4 fps": the frames of the GIF as an MP4 video (Media Foundation), for a player that shows no GIF motion.
        if (args.Length == 4 && args[0] == "mp4") return Mp4(args[1], args[2], int.Parse(args[3])).GetAwaiter().GetResult();
        // "trim in.gif out.gif fps": the frames at the end that do not change go.
        if (args.Length == 4 && args[0] == "trim") return Trim(args[1], args[2], int.Parse(args[3]));
        // The target: "x y width height" (a fixed screen area) or "--area-file file" (the area that a test tray writes for its slot).
        // A later target can be one window (window capture); the rest of the arguments stay the same.
        // I2: --backdrop-key uses the key color #FF00FF, which the tray never draws, for the empty frame count.
        var withKey = args.Contains("--backdrop-key");
        var withBackdrop = withKey || args.Contains("--backdrop");
        args = args.Where(a => a is not "--backdrop" and not "--backdrop-key").ToArray();
        var fileTarget = args.Length >= 1 && args[0] == "--area-file";
        if (args.Length < (fileTarget ? 6 : 8))
        {
            Console.Error.WriteLine("Usage: recorder [--backdrop | --backdrop-key] (x y width height | --area-file instance.json) scale fps seconds out.gif");
            return 2;
        }
        SetProcessDpiAwarenessContext(-4); // Per monitor v2: the area is in real screen pixels.
        string? ready = null;
        string? run = null;
        var pid = 0;
        Rectangle area;
        if (fileTarget)
        {
            if (AreaFromFile(args[1], TimeSpan.FromSeconds(180)) is not { } found)
            {
                Console.Error.WriteLine($"no area: {args[1]} did not come in 180 s. Did the test tray start?");
                return 4;
            }
            (area, run, pid) = found;
            ready = args[1] + ".ready";
            args = args[2..];
        }
        else
        {
            area = new Rectangle(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]));
            args = args[4..];
        }
        var scale = double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
        var fps = int.Parse(args[1]);
        var seconds = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
        var output = Path.GetFullPath(args[3]);
        Console.WriteLine($"area {area.X} {area.Y} {area.Width} {area.Height}");
        var size = new Size((int)(area.Width * scale), (int)(area.Height * scale));
        var frames = new List<byte[]>();
        var times = new List<string>();
        if (withBackdrop) StartBackdrop(area, withKey ? Key : Color.FromArgb(0xcc, 0xcc, 0xcc));
        // The test tray waits for this file (--wait-recorder), so the recording has the first show of its windows.
        if (ready is not null) File.WriteAllText(ready, DateTime.Now.ToString("HH:mm:ss.fff"));
        byte[]? first = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var step = 1000.0 / fps;
        for (var n = 0; clock.Elapsed.TotalSeconds < seconds; n++)
        {
            while (clock.Elapsed.TotalMilliseconds < n * step) Thread.Sleep(1);
            // I2: other windows of the screen can be above the backdrop. Keep it directly under the lowest window of the tray.
            if (withBackdrop && pid != 0 && n % 10 == 0) UnderTray(pid);
            using var shot = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(shot))
            {
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                Pointer(g, area.Location);
            }
            var keyShare = KeyShare(shot);
            using var small = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(shot, 0, 0, size.Width, size.Height);
            }
            using var png = new MemoryStream();
            small.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = png.ToArray();
            // The start: no frame until the area changes.
            first ??= bytes;
            if (frames.Count == 0 && bytes.AsSpan().SequenceEqual(first)) continue;
            frames.Add(bytes);
            times.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + Mean(small).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + " " + keyShare.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        Console.WriteLine($"{frames.Count} frames in {clock.Elapsed.TotalSeconds:0.0} s");
        Record(run, output, frames.Count, clock.Elapsed.TotalSeconds, frames.Count == 0 ? 3 : 0);
        // I17: no frame means that the area did not change, so the recorded app did not show. Fail at once with no empty GIF.
        if (frames.Count == 0)
        {
            Console.Error.WriteLine("no frame: the area did not change. Did the recorded app start and show in the area?");
            return 3;
        }
        // The time of each frame, for a match with a trace of the recorded app.
        File.WriteAllLines(output + ".times.txt", times);
        var encoder = new GifBitmapEncoder();
        foreach (var f in frames) encoder.Frames.Add(BitmapFrame.Create(new MemoryStream(f), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
        using var gif = new MemoryStream();
        encoder.Save(gif);
        File.WriteAllBytes(output, Loop(gif.ToArray(), (int)Math.Round(100.0 / fps)));
        Console.WriteLine($"{output}: {new FileInfo(output).Length / 1024} KB");
        return 0;
    }

    /// <summary>
    /// The record of this recording: one new file for each recording in %LOCALAPPDATA%\Orchestrator.Tray\runs, next to the record of
    /// the tray run (the same run id). The file name has the process id of the recorder, so no two recorders write the same file.
    /// </summary>
    static void Record(string? run, string output, int frames, double seconds, int result)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orchestrator.Tray", "runs");
            Directory.CreateDirectory(folder);
            var id = run ?? $"{DateTime.Now:yyyyMMdd-HHmmss.fff}-pid{Environment.ProcessId}";
            var file = Path.Combine(folder, $"{id}.rec-pid{Environment.ProcessId}.json");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new
            {
                run = id, source = "recorder", kind = "recording", name = Path.GetFileNameWithoutExtension(output),
                end = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), seconds = Math.Round(seconds, 1), frames, result,
            }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: no run record: {e.Message}");
        }
    }

    /// <summary>Waits for the instance file of a test tray and reads its area and run id. Null when the file does not come in time.</summary>
    static (Rectangle Area, string? Run, int Pid)? AreaFromFile(string file, TimeSpan wait)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < wait)
        {
            if (File.Exists(file))
            {
                try
                {
                    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                    var a = json.RootElement.GetProperty("area");
                    var run = json.RootElement.TryGetProperty("run", out var r) ? r.GetString() : null;
                    var pid = json.RootElement.TryGetProperty("pid", out var p) ? p.GetInt32() : 0;
                    return (new Rectangle(a.GetProperty("x").GetInt32(), a.GetProperty("y").GetInt32(), a.GetProperty("width").GetInt32(), a.GetProperty("height").GetInt32()), run, pid);
                }
                catch (IOException)
                {
                    // The tray writes the file at this time: read it again.
                }
            }
            Thread.Sleep(50);
        }
        return null;
    }

    static async Task<int> Mp4(string input, string output, int fps)
    {
        var folder = Path.Combine(Path.GetTempPath(), "recorder-frames-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(folder);
        int width, height, count;
        using (var file = File.OpenRead(input))
        {
            var decoder = new GifBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            count = decoder.Frames.Count;
            // The encoder needs an even width and height.
            width = decoder.Frames[0].PixelWidth & ~1;
            height = decoder.Frames[0].PixelHeight & ~1;
            for (var i = 0; i < count; i++)
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(decoder.Frames[i], new System.Windows.Int32Rect(0, 0, width, height))));
                using var o = File.Create(Path.Combine(folder, $"{i:00000}.png"));
                png.Save(o);
            }
        }
        var composition = new Windows.Media.Editing.MediaComposition();
        var frame = TimeSpan.FromSeconds(1.0 / fps);
        for (var i = 0; i < count; i++)
        {
            var image = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(folder, $"{i:00000}.png"));
            composition.Clips.Add(await Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(image, frame));
        }
        var target = Path.GetFullPath(output);
        File.WriteAllBytes(target, []);
        var outFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(target);
        var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = (uint)fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = 2_000_000;
        profile.Audio = null;
        var result = await composition.RenderToFileAsync(outFile, Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);
        Directory.Delete(folder, true);
        Console.WriteLine($"{count} frames, {width} x {height}: {result}; {target}: {new FileInfo(target).Length / 1024} KB");
        return result == Windows.Media.Transcoding.TranscodeFailureReason.None ? 0 : 1;
    }

    static int Trim(string input, string output, int fps)
    {
        using var file = File.OpenRead(input);
        var decoder = new GifBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        byte[] Pixels(BitmapSource f)
        {
            var c = new FormatConvertedBitmap(f, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var px = new byte[c.PixelWidth * c.PixelHeight * 4];
            c.CopyPixels(px, c.PixelWidth * 4, 0);
            return px;
        }
        var last = decoder.Frames.Count - 1;
        var end = Pixels(decoder.Frames[last]);
        // Keep one second of the still end.
        while (last > 0 && Pixels(decoder.Frames[last - 1]).AsSpan().SequenceEqual(end)) last--;
        last = Math.Min(decoder.Frames.Count - 1, last + fps);
        var encoder = new GifBitmapEncoder();
        for (var i = 0; i <= last; i++) encoder.Frames.Add(decoder.Frames[i]);
        using var gif = new MemoryStream();
        encoder.Save(gif);
        File.WriteAllBytes(output, Loop(gif.ToArray(), (int)Math.Round(100.0 / fps)));
        Console.WriteLine($"{decoder.Frames.Count} frames to {last + 1}; {output}: {new FileInfo(output).Length / 1024} KB");
        return 0;
    }

    static nint backdropHandle;

    /// <summary>Puts the backdrop directly under the lowest visible top window of the tray process, with no activation.</summary>
    static void UnderTray(int pid)
    {
        if (backdropHandle == 0) return;
        nint lowest = 0;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner == pid && IsWindowVisible(h)) lowest = h;
            return true;
        }, 0);
        // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS
        if (lowest != 0) SetWindowPos(backdropHandle, lowest, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x4000);
    }

    /// <summary>The key color of --backdrop-key (I2). The tray draws no pixel of exactly this color.</summary>
    static readonly Color Key = Color.FromArgb(0xff, 0x00, 0xff);

    /// <summary>
    /// The share (0 to 100) of the pixels of the full size frame that are exactly the key color, from each 4th pixel of each
    /// 4th row. A shadow or a scrim over the key changes the color, so these pixels count as the tray. With no key backdrop, 0.
    /// </summary>
    static double KeyShare(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var px = new byte[data.Stride * b.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            long key = 0, n = 0;
            for (var y = 0; y < b.Height; y += 4)
                for (var x = 0; x < b.Width; x += 4)
                {
                    var i = y * data.Stride + x * 4;
                    if (px[i] == Key.B && px[i + 1] == Key.G && px[i + 2] == Key.R) key++;
                    n++;
                }
            return n == 0 ? 0 : 100.0 * key / n;
        }
        finally
        {
            b.UnlockBits(data);
        }
    }

    /// <summary>The backdrop window behind the recorded windows, on its own UI thread; it shows 500 ms before the first frame.</summary>
    static void StartBackdrop(Rectangle area, Color color)
    {
        var backdrop = new Thread(() =>
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = area,
                BackColor = color, ShowInTaskbar = false,
            };
            form.HandleCreated += (_, _) => backdropHandle = form.Handle;
            Application.Run(form);
        }) { IsBackground = true };
        backdrop.SetApartmentState(ApartmentState.STA);
        backdrop.Start();
        Thread.Sleep(500);
    }

    /// <summary>The mean of the blue channel of each 8th pixel: a frame with no recorded window has the level of the backdrop.</summary>
    static double Mean(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var px = new byte[data.Stride * b.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            long sum = 0, n = 0;
            for (var i = 0; i < px.Length; i += 32) { sum += px[i]; n++; }
            return (double)sum / n;
        }
        finally
        {
            b.UnlockBits(data);
        }
    }

    /// <summary>Draws the pointer into the picture at its screen place.</summary>
    static void Pointer(Graphics g, Point origin)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & 1) == 0) return;
        var dc = g.GetHdc();
        try
        {
            _ = DrawIconEx(dc, info.X - origin.X, info.Y - origin.Y, info.Cursor, 0, 0, 0, 0, 3);
        }
        finally
        {
            g.ReleaseHdc(dc);
        }
    }

    /// <summary>
    /// Writes the GIF again with a loop block after the header and a graphic control block (the delay in 1/100 s) before each
    /// image; the control blocks of the encoder go.
    /// </summary>
    static byte[] Loop(byte[] gif, int delay)
    {
        var o = new MemoryStream();
        var p = 13;
        if ((gif[10] & 0x80) != 0) p += 3 * (1 << ((gif[10] & 7) + 1));
        o.Write(gif, 0, p);
        o.Write([0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00]);
        while (p < gif.Length)
        {
            switch (gif[p])
            {
                case 0x21:
                    {
                        var start = p;
                        var label = gif[p + 1];
                        p += 2;
                        while (gif[p] != 0) p += gif[p] + 1;
                        p++;
                        // The control blocks and the loop block of the encoder go; the other blocks stay.
                        if (label != 0xF9 && label != 0xFF) o.Write(gif, start, p - start);
                        break;
                    }
                case 0x2C:
                    {
                        o.Write([0x21, 0xF9, 0x04, 0x00, (byte)(delay & 0xFF), (byte)(delay >> 8), 0x00, 0x00]);
                        var start = p;
                        var flags = gif[p + 9];
                        p += 10;
                        if ((flags & 0x80) != 0) p += 3 * (1 << ((flags & 7) + 1));
                        p++; // the LZW code size
                        while (gif[p] != 0) p += gif[p] + 1;
                        p++;
                        o.Write(gif, start, p - start);
                        break;
                    }
                case 0x3B:
                    o.WriteByte(0x3B);
                    return o.ToArray();
                default:
                    p++;
                    break;
            }
        }
        o.WriteByte(0x3B);
        return o.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Cursor;
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int w, int h, int step, nint brush, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint context);

    delegate bool EnumWindowsProc(nint window, nint data);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, nint data);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out int process);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
