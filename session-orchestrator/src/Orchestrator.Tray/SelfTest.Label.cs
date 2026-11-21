using System.Text.Json;

namespace Orchestrator.Tray;

public static partial class SelfTest
{
    /// <summary>The text that the label of the tray must show (Program sets it): for example "I17 · 3da7df8".</summary>
    public static string? ExpectedLabel { get; set; }

    /// <summary>
    /// The label check: the label in the bottom-left corner of the panel has the expected text, has a size, is fully inside the
    /// panel, is below the app bar with a gap, and no other element covers its center. The label has no pointer events, so the check turns them on for the hit test only.
    /// </summary>
    static async Task<(bool Pass, string Message)> LabelCheck(TrayWindow w)
    {
        var json = await Js(w, """
            (() => { const e = document.querySelector('.tray-panel > .run-label'); if (!e) return JSON.stringify({ found: false });
              const r = e.getBoundingClientRect(), p = document.querySelector('.tray-panel')?.getBoundingClientRect(), n = document.querySelector('.tray-view .nav2')?.getBoundingClientRect();
              const old = e.style.pointerEvents; e.style.pointerEvents = 'auto';
              const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); e.style.pointerEvents = old;
              const s = getComputedStyle(e);
              return JSON.stringify({ found: true, text: e.textContent.trim(), width: Math.round(r.width), height: Math.round(r.height),
                inside: !p || (r.left >= p.left && r.right <= p.right && r.top >= p.top && r.bottom <= p.bottom),
                onTop: !!top && (top === e || e.contains(top)), cover: top ? top.className.toString() : 'none', gap: n ? Math.round(r.top - n.bottom) : 99, left: p ? Math.round(r.left - p.left) : 0,
                visible: s.visibility === 'visible' && s.display !== 'none' && parseFloat(s.opacity) >= .3 }); })()
            """);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.GetProperty("found").GetBoolean()) return (false, "no label (.tray-panel > .run-label) on the page");
        var text = r.GetProperty("text").GetString();
        var pass = text == ExpectedLabel && r.GetProperty("width").GetInt32() > 0 && r.GetProperty("inside").GetBoolean()
            && r.GetProperty("onTop").GetBoolean() && r.GetProperty("visible").GetBoolean() && r.GetProperty("gap").GetInt32() >= 1;
        return (pass, $"text \"{text}\" (expect \"{ExpectedLabel}\"), size {r.GetProperty("width")} x {r.GetProperty("height")}, inside the panel {r.GetProperty("inside")}, "
            + $"on top {r.GetProperty("onTop")} (element at the center: {r.GetProperty("cover")}), visible {r.GetProperty("visible")}, "
            + $"gap below the app bar {r.GetProperty("gap")} px (expect 1 or more), left {r.GetProperty("left")} px");
    }
}
