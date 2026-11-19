// The small script of the motion layer (Components/Motion). C# decides each motion; this script only does what C# cannot:
// wait for the end of the CSS motion of a block, start a view transition, and mark the rings that were drawn before.
(function () {
  // V12: a ring draws only the first time its key shows. A mutation observer runs before the next paint: a new ring element
  // with a key that was drawn before gets data-drawn (motion.css stops its draw); a new key draws and goes into the list.
  // Blazor keeps an attribute that a script adds, so the mark stays when the ring renders again.
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

  // The longest wait of a block for its motion (--t-limit in motion.css), in ms.
  function limit() {
    var v = getComputedStyle(document.documentElement).getPropertyValue("--t-limit").trim();
    var ms = parseFloat(v);
    return isNaN(ms) ? 1000 : v.endsWith("ms") ? ms : ms * 1000;
  }

  // Resolves when Blazor changes the page, or after 600 ms. V13: watch the body, because Blazor replaces the .app element
  // itself. Not requestAnimationFrame: the browser pauses the rendering during the update of a view transition, so a frame
  // callback waits for the time limit. The next task comes after the Blazor batch of the render.
  function rendered() {
    return new Promise(function (resolve) {
      var done = false;
      function end() { if (!done) { done = true; observer.disconnect(); resolve(); } }
      var observer = new MutationObserver(function () { setTimeout(end, 0); });
      observer.observe(document.body, { childList: true, subtree: true });
      setTimeout(end, 600);
    });
  }

  var moves = !!document.startViewTransition && !(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);

  window.orchMotion = {
    // Presence: resolves when each finite CSS animation and transition in the block ends, or after the time limit.
    // One frame first, so the phase class of the render is in the styles. A hidden page draws no frames and moves no
    // animation, so it resolves at once; the limit starts before the frame, so a page that hides during the wait ends too.
    settle: function (el) {
      return new Promise(function (resolve) {
        if (document.visibilityState === "hidden") { resolve(); return; }
        var timer = setTimeout(resolve, limit());
        requestAnimationFrame(function () {
          var all = el && el.getAnimations ? el.getAnimations({ subtree: true }) : [];
          var finite = all.filter(function (a) { return a.effect && a.effect.getComputedTiming().endTime !== Infinity; });
          Promise.all(finite.map(function (a) { return a.finished.catch(function () { }); })).then(function () { clearTimeout(timer); resolve(); });
        });
      });
    },
    // The nav dot of the app bar: it moves to the center of the current tab item, and fades out on Home. The first place has no
    // motion.
    dot: function (nav) {
      var dot = nav && nav.querySelector(".ndot");
      if (!dot) return;
      var on = nav.querySelector(".it.on"), first = !dot.dataset.ready;
      if (first) nav.classList.add("jump");
      if (on) dot.style.setProperty("--dx", (on.offsetLeft + on.offsetWidth / 2) + "px");
      dot.classList.toggle("off", !on);
      if (first) { void dot.offsetWidth; nav.classList.remove("jump"); dot.dataset.ready = "1"; }
    },
    // MotionHost: starts a view transition of the kind ("push" or "back"). Resolves when the browser has the picture of
    // the old page, so Blazor can change the page; the transition then waits for the render.
    begin: function (kind) {
      if (!moves) return Promise.resolve();
      return new Promise(function (started) {
        var root = document.documentElement;
        root.classList.add("nav", "vt");
        root.classList.toggle("vt-back", kind === "back");
        var t = document.startViewTransition(function () { started(); return rendered(); });
        t.ready.catch(function () { });
        t.finished.catch(function () { }).finally(function () { root.classList.remove("vt", "vt-back"); });
        // The browser calls the update also for a skipped transition; this limit only keeps a navigation from a wait with no end.
        setTimeout(started, 300);
      });
    }
  };
})();
