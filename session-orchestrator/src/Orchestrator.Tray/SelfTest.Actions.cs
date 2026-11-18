using Microsoft.Web.WebView2.Core;

namespace Orchestrator.Tray;

/// <summary>The E2 checks of the page actions (E-1): the page bar, the corner button with its speed dial, and the app bar rule.</summary>
public static partial class SelfTest
{
    static async Task<IEnumerable<Check>> Actions(CoreWebView2 web, string folder)
    {
        var checks = new List<Check>();

        // A question that waits: page actions in the bar, its content actions in the corner button; no app bar on a document page.
        await Home(web, "cards");
        await web.ExecuteScriptAsync($"{Go}(document.querySelector('[data-deck-card] .card-link').getAttribute('href'))");
        await Until(() => Found(web, ".pagebar .cfab"), TimeSpan.FromSeconds(10));
        await Task.Delay(700);
        var bar = await Eval<string>(web, "[...document.querySelectorAll('.pagebar > [data-act]')].map(b => b.dataset.act).join(', ')");
        var appBar = await Eval<string>(web, "getComputedStyle(document.querySelector('.nav2')).display");
        var closedPng = await Snap(web, folder, "e1-document", "window.scrollTo(0, 0)", 200);
        await web.ExecuteScriptAsync("document.querySelector('.pagebar .cfab').click()");
        await Task.Delay(500);
        var dial = await Eval<string>(web, "[...document.querySelectorAll('.dial [data-act]')].map(b => b.dataset.act).join(', ')");
        // The main action is next to the corner button: the lowest item of the dial.
        var lowest = await Eval<string>(web, "[...document.querySelectorAll('.dial [data-act]')].sort((a, b) => b.getBoundingClientRect().top - a.getBoundingClientRect().top)[0]?.dataset.act ?? ''");
        var openPng = await Snap(web, folder, "e1-speed-dial", "", 0);
        await web.ExecuteScriptAsync("document.querySelector('.dscrim').click()");
        await Task.Delay(300);
        checks.Add(new Check("e1-document", bar == "session, stream" && appBar == "none" && dial == "accept, other, skip" && lowest == "accept",
            $"page bar {bar}; app bar display {appBar}; dial {dial}, lowest {lowest}", $"{closedPng};{openPng}"));

        // Session status: only page actions, so no corner button. Danger acts are square icon buttons; the app bar shows.
        await Home(web, "sessions");
        await web.ExecuteScriptAsync($"{Go}(document.querySelector('a.srow').getAttribute('href'))");
        await Until(() => Found(web, ".pagebar"), TimeSpan.FromSeconds(10));
        await Task.Delay(700);
        var acts = await Eval<string>(web, "[...document.querySelectorAll('.pagebar > [data-act]')].map(b => b.dataset.act + (b.classList.contains('dg') ? ':danger' : '')).join(', ')");
        var corner = await Eval<bool>(web, "!!document.querySelector('.pagebar .cfab')");
        var shown = await Eval<string>(web, "getComputedStyle(document.querySelector('.nav2')).display");
        var statusPng = await Snap(web, folder, "e1-session", "window.scrollTo(0, 1e6)", 300);
        checks.Add(new Check("e1-session", acts.StartsWith("stream, message") && acts.Contains("archive:danger") && !corner && shown != "none",
            $"page bar {acts}; corner button {corner}; app bar display {shown}", statusPng));
        return checks;
    }
}
