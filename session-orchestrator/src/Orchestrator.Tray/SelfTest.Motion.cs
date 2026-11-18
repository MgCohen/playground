using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>The E2 checks of the motion items: each one takes a screenshot in the middle or at the end of a motion.</summary>
public static partial class SelfTest
{
    /// <summary>Runs a script, waits <paramref name="ms"/>, and writes a screenshot of the page to tray-e2-<paramref name="name"/>.png.</summary>
    static async Task<string> Snap(CoreWebView2 web, string folder, string name, string script, int ms)
    {
        await web.ExecuteScriptAsync(script);
        if (ms > 0) await Task.Delay(ms);
        var png = Path.Combine(folder, $"tray-e2-{name}.png");
        await using (var file = File.Create(png)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        return png;
    }

    static async Task<T> Eval<T>(CoreWebView2 web, string script) => JsonSerializer.Deserialize<T>(await web.ExecuteScriptAsync(script))!;

    static async Task Home(CoreWebView2 web, string path = "")
    {
        await web.ExecuteScriptAsync($"{Go}('{path}')");
        await Task.Delay(900);
    }

    /// <summary>M5, M6, M7: sheets and toast, the loading skeleton, press, ring draw and fold.</summary>
    static async Task<IEnumerable<Check>> Motion(CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();

        // M6: the host page has the skeleton in #app, so the widget is never blank before Blazor starts.
        // ExecuteScriptAsync does not wait for a promise, so the read of the host page is a synchronous request.
        var boot = await Eval<bool>(web, "(x => { x.open('GET', 'index.html', false); x.send(); return x.responseText.includes('class=\"boot\"'); })(new XMLHttpRequest())");
        var bootPng = await Snap(web, folder, "m6-skeleton",
            "document.body.insertAdjacentHTML('beforeend', '<div id=\"bootshot\" style=\"position:fixed;inset:0;z-index:99;background:var(--canvas)\"><div class=\"boot\"><i class=\"b-hd\"></i><i class=\"b-ring\"></i><i class=\"b-row\"></i><i class=\"b-row s\"></i><i class=\"b-row s\"></i><i class=\"b-bar\"></i></div></div>')", 500);
        await web.ExecuteScriptAsync("document.getElementById('bootshot').remove()");
        var fade = await Eval<string>(web, "getComputedStyle(document.querySelector('.page')).animationName");
        checks.Add(new Check("m6-skeleton", boot && fade == "m-fade", $"index.html has the skeleton {boot}; the page fades in with {fade}", bootPng));
        // A motion must not put the backdrop of the app bar over the bar. The backdrop is a pseudo-element with no pointer events,
        // so the check looks for a stacking context on an ancestor of the bar: an animation, a transform, an opacity or a filter.
        var context = await Eval<string>(web, """
            (() => { const out = []; for (let e = document.querySelector('.nav2').parentElement; e && e !== document.body; e = e.parentElement) {
              const s = getComputedStyle(e); if (s.animationName !== 'none' || s.transform !== 'none' || s.opacity !== '1' || s.filter !== 'none' || s.isolation === 'isolate') out.push(e.className); }
              return out.join(','); })()
            """);
        checks.Add(new Check("app-bar-on-top", context.Length == 0, $"ancestors of the app bar with a stacking context: {(context.Length == 0 ? "none" : context)}", ""));

        // M7 ring draw: the deep ring sweeps in 900 ms. A screenshot at about 300 ms shows a part of the ring.
        await Home(web, "stream");
        var ringPng = await Snap(web, folder, "m7-ring-draw", $"{Go}('')", 330);
        await Task.Delay(900);
        var sweep = await Eval<string>(web, "getComputedStyle(document.querySelector('.dring .fill')).animationName");
        checks.Add(new Check("m7-ring-draw", sweep == "m-sweep", $"deep ring animation {sweep}", ringPng));

        // M7 fold: the active sessions fold opens its height in 260 ms.
        var foldPng = await Snap(web, folder, "m7-fold", "document.querySelector('details.fold > summary').click()", 120);
        await Task.Delay(400);
        var fold = await Eval<double[]>(web, "(d => [d.open ? 1 : 0, d.getBoundingClientRect().height])(document.querySelector('details.fold'))");
        var foldEnd = await Snap(web, folder, "m7-fold-open", "", 0);
        checks.Add(new Check("m7-fold", fold[0] == 1 && fold[1] > 100, $"fold open {fold[0] == 1}, height {fold[1]:0} px at the end", $"{foldPng};{foldEnd}"));

        // M7 press: a raised button goes into the surface while the mouse button is down.
        var press = await Eval<string>(web, "getComputedStyle(document.querySelector('.nav2 .home')).transitionDuration");
        checks.Add(new Check("m7-press", press.StartsWith("0.08s"), $"home button transition {press}", ""));

        // M5: the answer sheet slides up over a fading scrim; Cancel slides it down, then it goes.
        await Home(web, "cards");
        var upPng = await Snap(web, folder, "m5-sheet-up", "document.querySelector('.acts [data-act=\"left\"]').click()", 140);
        await Task.Delay(400);
        var downPng = await Snap(web, folder, "m5-sheet-down", "[...document.querySelectorAll('.sheet .btn')].find(b => b.textContent.includes('Cancel')).click()", 90);
        await Task.Delay(500);
        var gone = await Eval<bool>(web, "!document.querySelector('.sheet')");
        checks.Add(new Check("m5-sheet", gone, $"sheet up, then down and removed {gone}", $"{upPng};{downPng}"));

        // M5: the undo toast drops from the top, and a bar counts down the 5 s undo time.
        var toastPng = await Snap(web, folder, "m5-toast", "document.querySelector('.acts [data-act=\"right\"]').click()", 1500);
        var bar = await Eval<string>(web, "(b => b ? getComputedStyle(b).animationName + ' ' + getComputedStyle(b).animationDuration : 'none')(document.querySelector('.toast .ubar'))");
        await web.ExecuteScriptAsync("document.querySelector('.toast button')?.click()");
        checks.Add(new Check("m5-toast", bar == "m-count 5s", $"undo bar {bar}; undo clicked", toastPng));
        await Task.Delay(500);
        return checks;
    }
}
