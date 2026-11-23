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

        // M7 ring draw and V6: the rings draw (900 ms) only at the first show. The end of that draw set "drawn" on the root;
        // a tab change after it shows the full rings at once.
        var drawn = await Eval<bool>(web, "document.documentElement.classList.contains('drawn')");
        await Home(web, "stream");
        var rows = await Eval<string>(web, "(f => f ? getComputedStyle(f).animationName : 'no ring')(document.querySelector('.ring .fill'))");
        var ringPng = await Snap(web, folder, "m7-ring-draw", $"{Go}('')", 330);
        var sweep = await Eval<string>(web, "getComputedStyle(document.querySelector('.dring .fill')).animationName");
        checks.Add(new Check("m7-ring-draw", drawn && sweep == "none" && rows == "none",
            $"first draw ended (root class drawn) {drawn}; after a tab change: deep ring animation {sweep}, row ring animation {rows} (expect none)", ringPng));

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
        checks.AddRange(await Pile(web, folder));
        return checks;
    }

    const string TopTitle = "document.querySelector('[data-deck-card] .q')?.textContent ?? ''";

    /// <summary>M1, M2, M3: the card pile. A swipe throws the card out, skip tucks it under, undo drops it in again.</summary>
    static async Task<IEnumerable<Check>> Pile(CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();
        await Home(web, "cards");
        await Task.Delay(400);
        var slots = await Eval<string>(web, "[...document.querySelectorAll('.pile .pcard')].map(c => c.style.getPropertyValue('--i') + ':' + Math.round(c.getBoundingClientRect().width)).join(' ')");
        var pilePng = await Snap(web, folder, "m3-pile", "window.scrollTo(0, 0)", 200);
        checks.Add(new Check("m3-pile", slots.Split(' ').Length == 3, $"slot:width of each card {slots}", pilePng));

        // M2: skip drops the top card and tucks it under the pile; the next card lifts to the top.
        var first = await Eval<string>(web, TopTitle);
        var tuckPng = await Snap(web, folder, "m2-tuck", "document.querySelector('[data-act=skip]').click()", 110);
        await Task.Delay(700);
        var next = await Eval<string>(web, TopTitle);
        var liftPng = await Snap(web, folder, "m2-lifted", "", 0);
        checks.Add(new Check("m2-tuck", next != first && next.Length > 0, $"top card before \"{first}\", after the skip \"{next}\"", $"{tuckPng};{liftPng}"));
        await web.ExecuteScriptAsync("document.querySelector('.toast button')?.click()");
        await Task.Delay(700);

        // M1: a swipe to the right past the limit throws the card out to the right; the act runs at the end of the motion.
        var throwable = await Eval<bool>(web, "!!document.querySelector('[data-act=right][data-throw]')");
        var before = await Eval<string>(web, TopTitle);
        const string swipe = """
            (c => { const r = c.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
              const ev = (t, dx) => c.dispatchEvent(new PointerEvent(t, { bubbles: true, cancelable: true, pointerId: 7, isPrimary: true, clientX: x + dx, clientY: y + dx / 8, button: 0 }));
              ev('pointerdown', 0); ev('pointermove', 40); ev('pointermove', 110); ev('pointermove', 160); ev('pointerup', 160); })(document.querySelector('[data-deck-card]'))
            """;
        await web.ExecuteScriptAsync(swipe);
        // The screenshot takes longer than the throw, so the check holds the throw at its start (the release point, with the stamp) for the screenshot.
        var flying = await Eval<string>(web, "(c => { if (!c) return ''; const a = c.getAnimations().find(x => x.animationName === 'm-fly-r'); a?.pause(); if (a) a.currentTime = 0; const r = c.getBoundingClientRect(); return c.className + ', left ' + Math.round(r.left) + ', opacity ' + Number(getComputedStyle(c).opacity).toFixed(2); })(document.querySelector('.pcard.out-r'))");
        var flyPng = await Snap(web, folder, "m1-fly", "", 50);
        var sideScroll = await Eval<bool>(web, "(window.scrollTo(200, window.scrollY), window.scrollX > 0)");
        await web.ExecuteScriptAsync("document.querySelector('.pcard.out-r')?.getAnimations().find(x => x.animationName === 'm-fly-r')?.play()");
        await Task.Delay(800);
        var after = await Eval<string>(web, TopTitle);
        checks.Add(new Check("m1-fly", throwable && flying.Contains("out-r") && after != before && !sideScroll, $"swipe right: card \"{before}\" class {flying}; top after \"{after}\"; side scroll {sideScroll}", flyPng));

        // M3: undo brings the card back; it drops in from the top with a small spring.
        var arrivePng = await Snap(web, folder, "m3-arrive", "document.querySelector('.toast button')?.click()", 120);
        await Task.Delay(600);
        var back = await Eval<string>(web, TopTitle);
        var arrive = await Eval<bool>(web, "!!document.querySelector('[data-deck-card].arrive')");
        checks.Add(new Check("m3-arrive", back == before && arrive, $"after the undo the top card is \"{back}\", class arrive {arrive}", arrivePng));
        return checks;
    }
}
