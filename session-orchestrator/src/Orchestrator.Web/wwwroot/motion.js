// V13: one navigation path for each link. The View Transitions API of Chromium takes a picture of the old page, Blazor renders
// the new page, and CSS (app.css, "T1" and "T2") moves the two pictures. A move to a deeper page pushes it in from the right
// (T2), a move back plays it in reverse, and a tab change fades and rises (T1). No page has an entrance animation of its own,
// so nothing plays again after the transition. This script only picks the kind and starts the transition.
// V12: a ring draws only the first time its key shows. A mutation observer runs before the next paint: a new ring element
// with a key that was drawn before gets data-drawn (app.css stops its draw); a new key draws and goes into the list.
// Blazor keeps an attribute that a script adds, so the mark stays when the ring renders again.
(function () {
  var seen = new Set();
  function mark(node) {
    var rings = node.matches && node.matches("[data-ring]") ? [node] : [];
    if (node.querySelectorAll) rings = rings.concat(Array.prototype.slice.call(node.querySelectorAll("[data-ring]")));
    rings.forEach(function (r) {
      var key = r.getAttribute("data-ring");
      if (!key || r.hasAttribute("data-drawn")) return;
      if (seen.has(key)) r.setAttribute("data-drawn", "");
      else seen.add(key);
    });
  }
  new MutationObserver(function (changes) {
    changes.forEach(function (c) {
      c.addedNodes.forEach(function (n) { if (n.nodeType === 1) mark(n); });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
  mark(document.documentElement);
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
    // Resolve when Blazor changes the page, or after 600 ms. V13: watch the body, because Blazor replaces the .app element
    // itself; a watch on the old element saw no change and always waited the 600 ms.
    return new Promise(function (resolve) {
      var page = document.body;
      var done = false;
      function end() { if (!done) { done = true; observer.disconnect(); resolve(); } }
      // V13: not requestAnimationFrame. The browser pauses the rendering during the update of a view transition, so a frame
      // callback waits for the time limit. The next task comes after the Blazor batch of the render.
      var observer = new MutationObserver(function () { setTimeout(end, 0); });
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
    // The same page is no move. A tab change (both depth 0) is T1; another move at the same depth is no move.
    var same = to.pathname + to.search === location.pathname + location.search;
    var tab = from === 0 && next === 0;
    if (same || (from === next && !tab)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    var root = document.documentElement;
    root.classList.add("nav");
    root.classList.toggle("vt-tab", tab);
    root.classList.toggle("vt-back", !tab && next < from);
    root.classList.add("vt");
    var t = document.startViewTransition(function () {
      Blazor.navigateTo(to.pathname.replace(/^\//, "") + to.search);
      return rendered();
    });
    t.finished.finally(function () { root.classList.remove("vt", "vt-back", "vt-tab"); });
  }, true);
})();
