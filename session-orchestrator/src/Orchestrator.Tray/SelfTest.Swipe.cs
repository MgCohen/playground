using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>
/// V17: a real mouse drag on the top card is a swipe. The drag starts on the card link, where a native link drag of the
/// browser cancelled the pointer events. A drag down skips the card: the top card changes, and no page opens.
/// </summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Swipe(WidgetForm form, CoreWebView2 web, string folder)
    {
        await web.ExecuteScriptAsync($"{Go}('cards')");
        await Until(() => Found(web, ".pcard"), TimeSpan.FromSeconds(20));
        await Task.Delay(1200);
        const string Top = "(c => c ? c.querySelector('.card-link')?.getAttribute('href') ?? '' : '')(document.querySelector('[data-deck-card]'))";
        var before = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(Top)) ?? "";
        var rect = JsonSerializer.Deserialize<double[]>(JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(
            "JSON.stringify((r => [r.left + r.width / 2, r.top + r.height * .45])(document.querySelector('.pcard').getBoundingClientRect()))"))!)!;
        var scale = form.DeviceDpi / 96f;
        var client = form.PointToScreen(Point.Empty);
        var from = new Point(client.X + (int)(rect[0] * scale), client.Y + (int)(rect[1] * scale));
        var back = Cursor.Position;
        await Task.Run(() => Mouse.Drag(from, from + new Size(0, (int)(170 * scale))));
        await Task.Delay(900);
        var after = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync(Top)) ?? "";
        var path = JsonSerializer.Deserialize<string>(await web.ExecuteScriptAsync("location.pathname"));
        var png = Path.Combine(folder, "tray-v17-swipe.png");
        await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        Cursor.Position = back;
        return [new Check("v17-swipe-skip", before.Length > 0 && after.Length > 0 && after != before && path == "/cards",
            $"top card before {before}, after a real drag down {after}; page {path} (expect /cards)", png)];
    }
}
