using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>The E2 checks of the header: the path of the sub-pages (T3) and the window buttons (T4).</summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Header(WidgetForm form, CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();
        const string PathText = "[...document.querySelectorAll('.hd .path > *')].map(e => e.textContent.trim()).join(' ')";

        // T3: Sessions › a session › Stream › a document. With three parents, the first one becomes "…".
        await Home(web, "sessions");
        await web.ExecuteScriptAsync($"{Go}(document.querySelector('a.srow').getAttribute('href'))");
        await Task.Delay(900);
        var two = await Eval<string>(web, PathText);
        var twoPng = await Snap(web, folder, "t3-path-session", "window.scrollTo(0, 0)", 200);
        await web.ExecuteScriptAsync($"{Go}(document.querySelector('.sticky-acts a.pri, .pagebar a.pri').getAttribute('href'))");
        await Task.Delay(900);
        var three = await Eval<string>(web, PathText);
        await web.ExecuteScriptAsync($"{Go}(document.querySelector('.tli a.ev').getAttribute('href'))");
        await Task.Delay(900);
        var four = await Eval<string>(web, PathText);
        var fourPng = await Snap(web, folder, "t3-path-document", "window.scrollTo(0, 0)", 200);
        // A tap on a part of the path goes back to it, and the path is cut there.
        await web.ExecuteScriptAsync("[...document.querySelectorAll('.hd .path .cr')].at(-1).click()");
        await Task.Delay(900);
        var back = await Eval<string>(web, PathText);
        var ok = two.StartsWith("Sessions ›") && three.StartsWith("Sessions ›") && three.EndsWith("› Stream") && four.StartsWith("… ›") && back == three;
        checks.Add(new Check("t3-path", ok, $"session \"{two}\"; locked stream \"{three}\"; document \"{four}\"; back \"{back}\"", $"{twoPng};{fourPng}"));

        // T4: pin, minimize and close are always in the header. Close hides the widget; the tray icon shows it again.
        await Home(web);
        var buttons = await Eval<string>(web, "[...document.querySelectorAll('.hd.win .wb .wbtn')].map(b => b.getAttribute('aria-label')).join(', ')");
        var t4Png = await Snap(web, folder, "t4-window-buttons", "", 0);
        await web.ExecuteScriptAsync("document.querySelector('.hd.win .wbtn.x').click()");
        await Task.Delay(150);
        await Settle(form);
        var closed = !form.Visible;
        form.ShowWidget();
        await Settle(form);
        await Task.Delay(400);
        checks.Add(new Check("t4-window-buttons", buttons == "Pin on screen, Minimize, Close" && closed && form.Visible,
            $"buttons {buttons}; close hides the widget {closed}; shown again {form.Visible}", t4Png));
        return checks;
    }
}
