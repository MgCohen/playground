using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>The E2 checks of the screen transitions: a tab change (T1) and a push to a document and back (T2).</summary>
public static partial class SelfTest
{
    // Holds each running animation of the page (also the view transition pictures) at a time, for a screenshot.
    static string Hold(int ms) =>
        $"(() => {{ const all = document.getAnimations(); all.forEach(a => {{ a.pause(); a.currentTime = {ms}; }}); return all.length; }})()";

    const string Play = "document.getAnimations().forEach(a => a.play())";

    static async Task<IEnumerable<Check>> Transitions(CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();

        // T1 and V13: a tap on the app bar is a view transition (vt-tab): the old page fades out and the new page rises. No
        // block of the page has an entrance animation of its own.
        await Home(web);
        await web.ExecuteScriptAsync("document.querySelector('.nav2 a[href=stream]').click()");
        await Task.Delay(40);
        var tabClass = await Eval<string>(web, "document.documentElement.className");
        await Until(() => Found(web, ".tl"), TimeSpan.FromSeconds(10));
        var count = await Eval<int>(web, Hold(120));
        var t1 = await Snap(web, folder, "t1-tab-rise", "", 80);
        var blocks = await Eval<string>(web, "[...new Set([...document.querySelectorAll('.page > *, .page > .stack > *')].map(e => getComputedStyle(e).animationName))].join(' ')");
        await web.ExecuteScriptAsync(Play);
        await Task.Delay(500);
        checks.Add(new Check("t1-tab-change", tabClass.Contains("vt-tab") && count > 0 && blocks == "none",
            $"classes at the tap \"{tabClass}\" (expect vt-tab); {count} animations held at 120 ms; block animations: {blocks} (expect none)", t1));

        // T2: a tap on a row pushes the document in from the right; a tap on the path goes back in reverse.
        await web.ExecuteScriptAsync("document.querySelector('.tli a.ev').click()");
        await Task.Delay(60);
        var push = await Eval<string>(web, "document.documentElement.className");
        await Eval<int>(web, Hold(130));
        var t2 = await Snap(web, folder, "t2-push", "", 80);
        await web.ExecuteScriptAsync(Play);
        await Until(() => Found(web, ".doc-page"), TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        var opened = await Eval<string>(web, "location.pathname");
        await web.ExecuteScriptAsync("document.querySelector('.hd .path .cr').click()");
        await Task.Delay(60);
        var pop = await Eval<string>(web, "document.documentElement.className");
        await Eval<int>(web, Hold(130));
        var t2b = await Snap(web, folder, "t2-back", "", 80);
        await web.ExecuteScriptAsync(Play);
        await Until(() => Found(web, ".tl"), TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        var backTo = await Eval<string>(web, "location.pathname");
        checks.Add(new Check("t2-push", push.Contains("vt") && !push.Contains("vt-back") && opened.StartsWith("/events/") && pop.Contains("vt-back") && backTo == "/stream",
            $"open: classes \"{push}\", page {opened}; back: classes \"{pop}\", page {backTo}", $"{t2};{t2b}"));
        return checks;
    }
}
