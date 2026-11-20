using System.Runtime.InteropServices;
using System.Text.Json;

namespace Orchestrator.Tray;

/// <summary>
/// A slot is one vertical strip of the screen, from the right edge, for one test tray. Slot 0 is the strip at the right edge,
/// slot 1 the strip to its left, and so on. A tray in slot N uses its strip as its work area: the panel home place, the badge
/// edge, the drag limits and the recorded area. So two test trays in two slots do not overlap on the screen.
/// A self-test takes the first free slot (a lock for each slot) and has its own WebView2 folder for each slot.
/// </summary>
public static partial class Slot
{
    /// <summary>The width of a slot in device-independent pixels: the panel with its shadow and gap, and the badge, fit in it.</summary>
    public const int Width = 460;

    /// <summary>The slot of this tray, or null for the full work area (the normal tray).</summary>
    public static int? Index { get; set; }

    /// <summary>The width of a slot in screen pixels at the system DPI.</summary>
    public static int Pixels => (int)Math.Round(Width * GetDpiForSystem() / 96.0);

    /// <summary>The number of slots on the primary screen: at least 1.</summary>
    public static int Count()
    {
        var work = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        return Math.Max(1, work.Width / Pixels);
    }

    /// <summary>The work area of this tray: the full work area, or the strip of its slot on that screen.</summary>
    public static Rectangle Area(Screen screen) => Cut(screen.WorkingArea, Index, Pixels);

    /// <summary>The work area cut to slot <paramref name="index"/>: the right edge moves left by one slot width for each slot.</summary>
    public static Rectangle Cut(Rectangle work, int? index, int pixels)
    {
        if (index is not { } n || n <= 0) return work;
        var right = Math.Max(work.Left + pixels, work.Right - n * pixels);
        return new Rectangle(work.Left, work.Top, right - work.Left, work.Height);
    }

    /// <summary>The screen area that a recorder of this slot records: the strip of one slot width at the right edge of the slot area.</summary>
    public static Rectangle RecordArea()
    {
        var area = Area(Screen.PrimaryScreen ?? Screen.AllScreens[0]);
        var width = Math.Min(Pixels, area.Width) & ~1;
        return new Rectangle(area.Right - width, area.Top, width, area.Height & ~1);
    }

    /// <summary>
    /// Takes the first free slot of the self-test: a lock for each slot. If <paramref name="wanted"/> is set, only that slot.
    /// If all slots are busy, waits up to <paramref name="wait"/>. Returns null when no slot is free at the end of the wait.
    /// </summary>
    public static (int Index, SingleInstance Lock)? Take(Action onShow, int? wanted, TimeSpan wait)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var said = false;
        while (true)
        {
            var count = Count();
            var slots = wanted is { } w ? [w] : Enumerable.Range(0, count).ToArray();
            foreach (var n in slots)
                if (SingleInstance.TryStart(true, onShow, $"slot{n}", signal: false) is { } taken)
                    return (n, taken);
            if (clock.Elapsed >= wait) return null;
            if (!said)
            {
                Console.WriteLine($"orch tray: all {slots.Length} test slots are busy. Waiting up to {wait.TotalSeconds:0} s for a free slot.");
                said = true;
            }
            Thread.Sleep(500);
        }
    }

    /// <summary>The instance file of a self-test run: the slot and the area to record. A recorder with --area-file reads it.</summary>
    public static void WriteInstanceFile(string folder, int index)
    {
        var area = RecordArea();
        var file = Path.Combine(folder, "instance.json");
        // The ready file of an old run would let the tray start before the recorder.
        File.Delete(file + ".ready");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new
        {
            slot = index,
            pid = Environment.ProcessId,
            area = new { x = area.X, y = area.Y, width = area.Width, height = area.Height },
        }));
        File.Move(file + ".tmp", file, overwrite: true);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
