using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// V7: pictures of the cards and of the card pages of the store, for a review of their layout. Each tab of the cards page
/// (the segment buttons), then the page of each card (up to 8). The web view takes the pictures, so no other window shows.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Cards(CoreWebView2 web, string folder)
    {
        var shots = new List<string>();
        async Task<string> Shot(string name)
        {
            // The rise of the blocks (T1) and the fonts end in less than 2 s.
            await Task.Delay(2000);
            var png = Path.Combine(folder, $"tray-v7-{name}.png");
            await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            shots.Add(png);
            return png;
        }
        await web.ExecuteScriptAsync($"{Go}('cards')");
        var found = await Until(() => Found(web, ".pcard"), TimeSpan.FromSeconds(20)) is not null;
        var tabs = JsonSerializer.Deserialize<string[]>(await web.ExecuteScriptAsync("[...document.querySelectorAll('.seg button')].map(b => b.textContent.trim())")) ?? [];
        for (var i = 0; i < tabs.Length; i++)
        {
            await web.ExecuteScriptAsync($"document.querySelectorAll('.seg button')[{i}].click(); window.scrollTo(0, 0)");
            await Shot($"cards-{i + 1}");
        }
        // The page of each card: the links of the cards of all tabs, the first 8.
        var links = new List<string>();
        for (var i = 0; i < tabs.Length && links.Count < 8; i++)
        {
            await web.ExecuteScriptAsync($"document.querySelectorAll('.seg button')[{i}].click()");
            await Task.Delay(500);
            var here = JsonSerializer.Deserialize<string[]>(await web.ExecuteScriptAsync("[...document.querySelectorAll('.card-link')].map(a => a.getAttribute('href'))")) ?? [];
            links.AddRange(here.Where(h => !links.Contains(h)).Take(8 - links.Count));
        }
        // The stream: two rows of each kind. A store with no open cards still has their pages there.
        await web.ExecuteScriptAsync($"{Go}('stream')");
        await Until(() => Found(web, ".tli"), TimeSpan.FromSeconds(20));
        await Shot("stream");
        var rows = JsonSerializer.Deserialize<string[]>(await web.ExecuteScriptAsync("""
            (() => { const seen = {}, out = [];
              for (const r of document.querySelectorAll('.tli')) { const k = r.dataset.kind, a = r.querySelector('a.ev');
                if (!a || (seen[k] = (seen[k] || 0) + 1) > 2) continue; out.push(a.getAttribute('href')); }
              return out; })()
            """)) ?? [];
        links.AddRange(rows.Where(h => !links.Contains(h)).Take(16));
        for (var i = 0; i < links.Count; i++)
        {
            await web.ExecuteScriptAsync($"{Go}({JsonSerializer.Serialize(links[i])})");
            await Until(() => Found(web, ".doc-page"), TimeSpan.FromSeconds(10));
            await Shot($"page-{i + 1}");
            // A long page: one more picture at the end.
            var tall = JsonSerializer.Deserialize<bool>(await web.ExecuteScriptAsync("document.documentElement.scrollHeight > innerHeight * 1.2"));
            if (tall)
            {
                await web.ExecuteScriptAsync("window.scrollTo(0, 1e6)");
                await Shot($"page-{i + 1}-end");
            }
        }
        await web.ExecuteScriptAsync($"{Go}('')");
        return [new Check("v7-cards", found, $"cards found {found}; {tabs.Length} tabs ({string.Join(", ", tabs)}); {links.Count} card pages; {shots.Count} pictures", string.Join(";", shots))];
    }
}
