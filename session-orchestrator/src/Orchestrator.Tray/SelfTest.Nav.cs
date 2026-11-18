using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// V13: a tap on a card opens its document page. The page records the time to the document, each replacement of the page
/// content, and each animation that starts after the view transition ends (the second motion that the user saw).
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Nav(CoreWebView2 web, string folder)
    {
        await web.ExecuteScriptAsync($"{Go}('cards')");
        await Until(() => Found(web, ".pcard .card-link"), TimeSpan.FromSeconds(20));
        await Task.Delay(1500);
        await web.ExecuteScriptAsync("""
            (() => {
              const log = window.__nav = { t0: performance.now(), doc: -1, swaps: 0, vtEnd: -1, late: [] };
              const page = document.querySelector('.app') || document.body;
              new MutationObserver(ms => {
                for (const m of ms) for (const n of m.addedNodes)
                  if (n.nodeType === 1 && (n.matches('.doc-page') || n.querySelector?.('.doc-page'))) { log.swaps++; if (log.doc < 0) log.doc = performance.now() - log.t0; }
              }).observe(page, { childList: true, subtree: true });
              const root = document.documentElement;
              new MutationObserver(() => { if (log.vtEnd < 0 && log.doc >= 0 && !root.classList.contains('vt')) log.vtEnd = performance.now() - log.t0; })
                .observe(root, { attributes: true, attributeFilter: ['class'] });
              document.addEventListener('animationstart', e => {
                const t = performance.now() - log.t0;
                if (log.vtEnd >= 0 && t >= log.vtEnd) log.late.push(e.animationName + '@' + Math.round(t));
              }, true);
              document.querySelector('.pcard .card-link').click();
            })()
            """);
        await Task.Delay(3000);
        var log = JsonSerializer.Deserialize<JsonElement>(await web.ExecuteScriptAsync("JSON.stringify(window.__nav)").ContinueWith(t => JsonSerializer.Deserialize<string>(t.Result)!));
        var doc = log.GetProperty("doc").GetDouble();
        var swaps = log.GetProperty("swaps").GetInt32();
        var vtEnd = log.GetProperty("vtEnd").GetDouble();
        var late = log.GetProperty("late").EnumerateArray().Select(e => e.GetString()).ToList();
        var png = Path.Combine(folder, "tray-v13-nav.png");
        await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        await web.ExecuteScriptAsync($"{Go}('')");
        return [new Check("v13-nav", doc >= 0 && doc <= 400 && swaps <= 1 && vtEnd >= 0 && vtEnd <= 500 && late.Count == 0,
            $"document after {doc:0} ms (expect 400 ms or less); the document content came {swaps} times (expect 1); push ended at {vtEnd:0} ms (expect 500 ms or less); animations after the push: {(late.Count == 0 ? "none" : string.Join(", ", late))} (expect none)", png)];
    }
}
