// T2: a move to a deeper page pushes it in from the right; a move back plays it in reverse. The View Transitions API of
// Chromium takes a picture of the old page, Blazor renders the new page, and CSS (app.css, "T2") moves the two pictures.
// T1 (a tab change) is CSS only. This script only picks the direction and starts the transition.
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
