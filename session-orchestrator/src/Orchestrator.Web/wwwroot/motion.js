// T2: a move to a deeper page pushes it in from the right; a move back plays it in reverse. The View Transitions API of
// Chromium takes a picture of the old page, Blazor renders the new page, and CSS (app.css, "T2") moves the two pictures.
// T1 (a tab change) is CSS only. This script only picks the direction and starts the transition.
// V6: the rings draw only one time. The end of the first ring draw sets "drawn" on the root (app.css stops the draw then).
(function () {
  function done(e) {
    if (e.animationName !== "m-sweep" && e.animationName !== "m-arc") return;
    document.documentElement.classList.add("drawn");
    document.removeEventListener("animationend", done, true);
  }
  document.addEventListener("animationend", done, true);
})();

(function () {
  if (!document.startViewTransition || !window.matchMedia || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  // The depth of a page: 0 for a tab page, 1 for a session or a session stream, 2 for a document.
  function depth(url) {
    var p = new URL(url, document.baseURI);
    var path = p.pathname.replace(/^\/+/, "");
    if (path.startsWith("events/")) return 2;
    if (path.startsWith("sessions/")) return 1;
    if (path === "stream" && p.searchParams.has("session")) return 1.5;
    return 0;
  }

  function rendered() {
    // Resolve when Blazor changes the page, or after 600 ms.
    return new Promise(function (resolve) {
      var page = document.querySelector(".app") || document.body;
      var done = false;
      function end() { if (!done) { done = true; observer.disconnect(); resolve(); } }
      var observer = new MutationObserver(function () { requestAnimationFrame(end); });
      observer.observe(page, { childList: true, subtree: true });
      setTimeout(end, 600);
    });
  }

  window.addEventListener("click", function (e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    var a = e.target.closest && e.target.closest("a[href]");
    if (!a || a.target || a.hasAttribute("download") || !window.Blazor || !Blazor.navigateTo) return;
    var to = new URL(a.getAttribute("href"), document.baseURI);
    if (to.origin !== location.origin) return;
    var from = depth(location.href), next = depth(to.href);
    // A tab change (both depth 0) is T1; the same depth is no move.
    if (from === next || (from === 0 && next === 0)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    var root = document.documentElement;
    root.classList.toggle("vt-back", next < from);
    root.classList.add("vt");
    var t = document.startViewTransition(function () {
      Blazor.navigateTo(to.pathname.replace(/^\//, "") + to.search);
      return rendered();
    });
    t.finished.finally(function () { root.classList.remove("vt", "vt-back"); });
  }, true);
})();
