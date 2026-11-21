using System.Text.Json;

namespace Orchestrator.Tray;

public static partial class SelfTest
{
    /// <summary>
    /// The check motion-times (#60): each motion that a wait of the run saw has the time of a token of motion.css. The page
    /// keeps the motions (orchMotion.times) and the token values; C# only compares them. A duration or a delay that is not
    /// 0 and not equal to a token (±1 ms) is a fault. The full data goes to motion-times.json in the result folder.
    /// </summary>
    static async Task MotionTimes(string folder, (string Page, ClearHost Host)[] pages, Action<string, bool, string> add)
    {
        var faults = new List<string>();
        var count = 0;
        var data = new Dictionary<string, JsonElement>();
        foreach (var (page, host) in pages)
        {
            var json = await Js(host, "JSON.stringify(window.orchMotion ? orchMotion.times() : null)");
            if (json is "null" or "")
            {
                faults.Add($"{page}: no orchMotion.times");
                continue;
            }

            using var doc = JsonDocument.Parse(json);
            data[page] = doc.RootElement.Clone();
            var tokens = doc.RootElement.GetProperty("tokens").EnumerateObject().Select(t => (t.Name, Ms: t.Value.GetDouble())).ToList();
            foreach (var m in doc.RootElement.GetProperty("seen").EnumerateArray())
            {
                count++;
                var name = $"{page} {m.GetProperty("step").GetString()} {m.GetProperty("motion").GetString()} on {m.GetProperty("target").GetString()}";
                foreach (var part in new[] { "duration", "delay" })
                {
                    var v = m.GetProperty(part).GetDouble();
                    if (v != 0 && !tokens.Any(t => Math.Abs(t.Ms - v) <= 1)) faults.Add($"{name}: {part} {v} ms is no token");
                }
            }
        }

        File.WriteAllText(Path.Combine(folder, "motion-times.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        add("motion-times", count > 0 && faults.Count == 0,
            faults.Count == 0 ? $"{count} motions, each time is a token" : $"{faults.Count} of {count}: {string.Join(" | ", faults.Take(6))}");
    }
}
