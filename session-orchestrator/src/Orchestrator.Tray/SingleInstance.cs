using System.Runtime.InteropServices;

namespace Orchestrator.Tray;

/// <summary>One tray app at a time. A second start brings the window of the first app to the front, then exits. Each self-test slot has its own instance (<see cref="Slot"/>).</summary>
public sealed partial class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle show;
    readonly RegisteredWaitHandle wait;

    SingleInstance(Mutex mutex, EventWaitHandle show, Action onShow)
    {
        this.mutex = mutex;
        this.show = show;
        wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>The lock name. A named instance (--instance) has its own lock, so a test tray can run next to another tray.</summary>
    public static string Name(bool selfTest, string? instance = null) =>
        $"Orchestrator.Tray.{Environment.UserName}{(selfTest ? ".selftest" : "")}{(instance is null ? "" : "." + instance)}";

    /// <summary>Returns null when another instance runs. With <paramref name="signal"/>, that instance then shows its window.</summary>
    public static SingleInstance? TryStart(bool selfTest, Action onShow, string? instance = null, bool signal = true)
    {
        var name = Name(selfTest, instance);
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{name}", out var created);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Show");
        if (!created)
        {
            // Windows lets only the foreground process give the foreground to another process.
            if (signal)
            {
                _ = AllowSetForegroundWindow(-1);
                show.Set();
            }
            mutex.Dispose();
            show.Dispose();
            return null;
        }
        return new SingleInstance(mutex, show, onShow);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        wait.Unregister(null);
        mutex.ReleaseMutex();
        mutex.Dispose();
        show.Dispose();
    }
}
