using System.Text.Json;

namespace Orchestrator.Tray;

/// <summary>
/// The box checks of the badge (I7, I18): the shadow box of each drawn part must be inside the click area, because the window
/// region clips the drawing. The script here is the measure of the test. It does not use the measure of the product, so a defect
/// in the product measure cannot hide a cut.
/// </summary>
public static partial class SelfTest
{
    // The boxes in CSS px of the window: [left, top, right, bottom]. A shadow box is the border box plus each outer shadow
    // (offset, blur and spread). The ring has no shadow: its box is the ring itself.
    const string BoxScript = """
        (() => {
          const shadows = s => !s || s === 'none' ? [] : s.split(/,(?![^(]*\))/).map(p => p.trim()).filter(p => !/\binset\b/.test(p))
            .map(p => p.replace(/rgba?\([^)]*\)|#[0-9a-f]+\b/gi, ' ').replace(/px/g, ' ').trim().split(/\s+/).map(Number));
          const box = (e, withShadow) => {
            const r = e.getBoundingClientRect(), b = [r.left, r.top, r.right, r.bottom];
            if (withShadow) for (const [x = 0, y = 0, blur = 0, spread = 0] of shadows(getComputedStyle(e).boxShadow)) {
              const g = blur + spread;
              b[0] = Math.min(b[0], r.left + x - g); b[1] = Math.min(b[1], r.top + y - g);
              b[2] = Math.max(b[2], r.right + x + g); b[3] = Math.max(b[3], r.bottom + y + g);
            }
            return b.map(v => Math.round(v * 10) / 10);
          };
          const o = { tab: box(document.querySelector('.tb-tab'), true), ring: box(document.querySelector('.tb-ring'), false) };
          const p = document.querySelector('.tb-peek.on');
          if (p) o.bubble = box(p, true);
          return JSON.stringify(o);
        })()
        """;

    /// <summary>The measured boxes of the badge page, as JSON.</summary>
    static Task<string> BadgeBoxes(BadgeWindow b) => Js(b, BoxScript);

    /// <summary>
    /// Each box inside the click area and the window, in CSS px (±0.5). The tab is flush with the screen edge, so its right side
    /// (off the screen) is not checked. The detail names each cut: the state, the part, the side and the size.
    /// </summary>
    static (bool Fit, string Detail) BoxesFit(BadgeWindow b, string state, string json)
    {
        var ds = b.DeviceDpi / 96f;
        var area = b.HitArea;
        double[] clip = [area.Left / ds, area.Top / ds, Math.Min(area.Right, b.ClientSize.Width) / ds, Math.Min(area.Bottom, b.ClientSize.Height) / ds];
        Dictionary<string, double[]>? boxes;
        try
        {
            boxes = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json);
        }
        catch (JsonException)
        {
            boxes = null;
        }
        if (boxes is null || !boxes.ContainsKey("tab")) return (false, $"{state}: no boxes ({json})");
        var cuts = new List<string>();
        foreach (var (part, x) in boxes)
        {
            if (x[0] < clip[0] - .5) cuts.Add($"{part} left {x[0]:0.#} < {clip[0]:0.#} ({clip[0] - x[0]:0.#} px)");
            if (x[1] < clip[1] - .5) cuts.Add($"{part} top {x[1]:0.#} < {clip[1]:0.#} ({clip[1] - x[1]:0.#} px)");
            if (part != "tab" && x[2] > clip[2] + .5) cuts.Add($"{part} right {x[2]:0.#} > {clip[2]:0.#} ({x[2] - clip[2]:0.#} px)");
            if (x[3] > clip[3] + .5) cuts.Add($"{part} bottom {x[3]:0.#} > {clip[3]:0.#} ({x[3] - clip[3]:0.#} px)");
        }
        var all = string.Join(", ", boxes.Select(p => $"{p.Key} [{string.Join(" ", p.Value.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)))}]"));
        var where = $"click area [{string.Join(" ", clip.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)))}]";
        return (cuts.Count == 0, cuts.Count == 0 ? $"{state}: fits; {all}; {where}" : $"{state}: {string.Join("; ", cuts)}; {all}; {where}");
    }

    /// <summary>
    /// R2: the shadow box of the open panel (the frame) is inside the panel window, so the window edge cuts no shadow. In CSS px;
    /// the window is the page.
    /// </summary>
    static async Task<(bool Fit, string Detail)> PanelBoxFits(TrayWindow w)
    {
        var json = await Js(w, BoxScript.Replace("const o = { tab: box(document.querySelector('.tb-tab'), true), ring: box(document.querySelector('.tb-ring'), false) };",
            "const o = { frame: box(document.querySelector('.tray-frame'), true), page: [0, 0, innerWidth, innerHeight] };", StringComparison.Ordinal));
        var boxes = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json);
        if (boxes is null || !boxes.TryGetValue("frame", out var f) || !boxes.TryGetValue("page", out var p)) return (false, $"no boxes ({json})");
        string[] sides = ["left", "top", "right", "bottom"];
        var cuts = sides.Select((side, i) => (side, over: i < 2 ? p[i] - f[i] : f[i] - p[i])).Where(c => c.over > .5).Select(c => $"{c.side} {c.over:0.#} px").ToList();
        var text = $"frame [{string.Join(" ", f)}], window [{string.Join(" ", p)}] CSS px, margin {w.Sizes?.Margin}";
        return (cuts.Count == 0, cuts.Count == 0 ? $"fits; {text}" : $"cut: {string.Join(", ", cuts)}; {text}");
    }

    /// <summary>Waits until the tab and its children have no running motion (at most 2 s).</summary>
    static Task<bool> TabSettled(BadgeWindow b) =>
        Until(async () => await Js(b, "document.querySelector('.tb-tab').getAnimations({ subtree: true }).filter(a => a.playState === 'running').length") == "0", 2000);
}
