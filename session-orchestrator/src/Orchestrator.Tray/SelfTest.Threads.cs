namespace Orchestrator.Tray;

/// <summary>
/// V19: two threads draw the home ball at the same time, as the edge tab thread and the widget thread do. No draw may throw.
/// For proof of the cause, the check also draws one shared Bitmap from two threads and counts its errors.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Threads()
    {
        static int Run(Action<Graphics> draw)
        {
            var errors = 0;
            var go = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
            {
                using var target = new Bitmap(120, 120);
                using var g = Graphics.FromImage(target);
                go.Wait();
                for (var i = 0; i < 300; i++)
                {
                    try
                    {
                        draw(g);
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            return errors;
        }
        var ball = await Task.Run(() => Run(g => EdgeTab.Ball(g, 60, 60, 62)));
        using var shared = new Bitmap(300, 300);
        var old = await Task.Run(() => Run(g => g.DrawImage(shared, 0, 0, 100, 100)));
        return [new Check("v19-ball-threads", ball == 0,
            $"two threads, 300 home ball draws each: {ball} errors (expect 0); one shared Bitmap drawn from two threads, as before the fix: {old} errors", "")];
    }
}
