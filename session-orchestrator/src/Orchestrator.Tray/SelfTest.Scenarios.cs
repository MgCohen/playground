using Orchestrator.Web;

namespace Orchestrator.Tray;

/// <summary>
/// The scenario run (--selftest folder --scenario name): a few focused actions for the proof of one issue, repeated a few times,
/// 5 to 10 s in total. Window messages only (the pointer of the user does not move). It writes scenario.txt with the time of
/// each step, so the frames of a recording can be matched to the steps.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The scenarios by name. Each one gets the tray and a step log.</summary>
    static readonly Dictionary<string, Func<TrayApp, Action<string>, Task>> Scenarios = new()
    {
        // I14: the status bubble. It shows after a click on a link. A hover alone with messages does not hold (WM_MOUSELEAVE comes at
        // once, see I8), so the steps are clicks: cards, wait, home, wait; three times.
        ["status-bubble"] = async (tray, step) =>
        {
            var w = tray.Window;
            for (var i = 1; i <= 3; i++)
            {
                step($"{i}: click tab cards: {await Press(w, ".nav2 a[href=\"cards\"]")}");
                await Task.Delay(1200);
                step($"{i}: click tab home: {await Press(w, ".nav2 a[href=\"\"]")}");
                await Task.Delay(1200);
            }
        },
    };

    public static async Task<int> Scenario(TrayApp tray, string folder, string? name)
    {
        Directory.CreateDirectory(folder);
        if (name is null || !Scenarios.TryGetValue(name, out var run))
        {
            File.WriteAllText(Path.Combine(folder, "scenario.txt"), $"no scenario '{name}'; names: {string.Join(", ", Scenarios.Keys)}");
            return 2;
        }
        var w = tray.Window;
        await Until(() => w.Phase == TrayPhase.Open, 8000);
        await Task.Delay(800);
        var log = new List<string>();
        var pointer = Cursor.Position;
        void Step(string text) => log.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        Step($"scenario {name}");
        await run(tray, Step);
        Step(Cursor.Position == pointer ? "pointer: not moved" : $"pointer: moved from {pointer} to {Cursor.Position}");
        File.WriteAllLines(Path.Combine(folder, "scenario.txt"), log);
        return Faults.Count == 0 ? 0 : 1;
    }

    /// <summary>A pointer move onto the element, with no click: a window message.</summary>
    static async Task<string> Point(TrayWindow w, string selector)
    {
        if (await Center(w, selector) is not { } at) return $"no element {selector}";
        Post(w, 0x200, 0, at);
        return "moved (window message)";
    }
}
