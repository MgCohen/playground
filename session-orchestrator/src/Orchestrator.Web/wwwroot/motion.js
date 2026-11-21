// The small script of the motion layer (Components/Motion). C# decides each motion; this script only does what C# cannot:
// wait for the end of the CSS motion of a block, start a view transition, and mark the rings that were drawn before.
(function () {
  // V12: a ring draws only the first time its key shows. A mutation observer runs before the next paint: a new ring element
  // with a key that was drawn before gets data-drawn (motion.css stops its draw); a new key draws and goes into the list.
  // Blazor keeps an attribute that a script adds, so the mark stays when the ring renders again.
  // The same element can come in two changes (Blazor adds a node, then its parent moves): only a different element with a key
  // that was drawn before is a repeat.
  var seen = new Set(), handled = new WeakSet();
  function mark(node) {
    var rings = node.matches && node.matches("[data-ring]") ? [node] : [];
    if (node.querySelectorAll) rings = rings.concat(Array.prototype.slice.call(node.querySelectorAll("[data-ring]")));
    rings.forEach(function (r) {
      var key = r.getAttribute("data-ring");
      if (!key || r.hasAttribute("data-drawn") || handled.has(r)) return;
      handled.add(r);
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

  // A time token of motion.css (for example "--t-limit"), in ms. An unknown token is 0.
  function ms(name) {
    var v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    var n = parseFloat(v);
    return isNaN(n) ? 0 : v.endsWith("ms") ? n : n * 1000;
  }
  function limit() { return ms("--t-limit"); }

  // Resolves when Blazor changes the page, or after --t-vt-render. V13: watch the body, because Blazor replaces the .app element
  // itself. Not requestAnimationFrame: the browser pauses the rendering during the update of a view transition, so a frame
  // callback waits for the time limit. The next task comes after the Blazor batch of the render.
  function rendered() {
    return new Promise(function (resolve) {
      var done = false;
      function end() { if (!done) { done = true; observer.disconnect(); resolve(); } }
      var observer = new MutationObserver(function () { setTimeout(end, 0); });
      observer.observe(document.body, { childList: true, subtree: true });
      setTimeout(end, ms("--t-vt-render"));
    });
  }

  var moves = !!document.startViewTransition && !(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);

  window.orchMotion = {
    // A time token of motion.css in ms, for C# and the other scripts.
    ms: ms,
    // The one wait of the motion pipeline (R1, I12). C# sends it as the command MotionWait. The modes:
    //   settle    each finite CSS animation and transition of the target and its children ends.
    //   property  the transitions of the properties (or the animations of the names) in arg end, on the target and its
    //             children. arg is one name or a list ("clip-path scale").
    //   frames    arg display frames (a number) pass. No target.
    //   step      a motion step of the registry: the target has data-motion, and its CSS rule owns the wait. The custom
    //             property --motion-wait names the properties (or the animations) to wait for; with no value, the wait settles.
    // The target is an element or a selector. One frame first, so the state class of the render is in the styles. The result
    // is { by, ms }:
    //   end      the motions ended (settle with no motion also ends here).
    //   cancel   the browser stopped a motion: the state changed during the wait.
    //   none     the wait names motions, but none started in 3 frames: a wrong name, or a state with no motion.
    //   limit    --t-limit (or --t-frame-limit for frames) ended the wait.
    //   hidden   the page is hidden, or it hides during the wait: a hidden page moves nothing, so the wait stops at once.
    //   missing  no target.
    wait: function (target, mode, arg) {
      mode = String(mode || "settle").toLowerCase();
      var el = typeof target === "string" ? document.querySelector(target) : target;
      var start = performance.now();
      return new Promise(function (resolve) {
        var done = false, timer = 0;
        function hide() { if (document.visibilityState === "hidden") finish("hidden"); }
        function finish(by) {
          if (done) return;
          done = true;
          clearTimeout(timer);
          document.removeEventListener("visibilitychange", hide);
          resolve({ by: by, ms: Math.round(performance.now() - start) });
        }
        if (mode === "frames") {
          var frames = parseInt(arg, 10) || 1;
          timer = setTimeout(function () { finish("limit"); }, ms("--t-frame-limit"));
          var step = function () { if (--frames <= 0) finish("end"); else requestAnimationFrame(step); };
          requestAnimationFrame(step);
          return;
        }
        if (!el) { finish("missing"); return; }
        if (mode === "step") {
          arg = getComputedStyle(el).getPropertyValue("--motion-wait").trim();
          mode = arg ? "property" : "settle";
        }
        if (document.visibilityState === "hidden") { finish("hidden"); return; }
        document.addEventListener("visibilitychange", hide);
        timer = setTimeout(function () { finish("limit"); }, limit());
        // The state class can come in the render after this call (the render batch and the call are two messages), so with no
        // motion yet the wait looks again for 2 more frames.
        var names = String(arg || "").split(/[\s,]+/).filter(Boolean);
        var look = function (frames) {
          if (done) return;
          var all = el.getAnimations ? el.getAnimations({ subtree: true }) : [];
          var mine = all.filter(function (a) {
            if (!a.effect || a.effect.getComputedTiming().endTime === Infinity) return false;
            return mode === "settle" || names.indexOf(a.transitionProperty) >= 0 || names.indexOf(a.animationName) >= 0;
          });
          if (mine.length === 0 && frames > 0) { requestAnimationFrame(function () { look(frames - 1); }); return; }
          if (mine.length === 0) { finish(mode === "settle" ? "end" : "none"); return; }
          var canceled = false;
          Promise.all(mine.map(function (a) { return a.finished.catch(function () { canceled = true; }); }))
            .then(function () { finish(canceled ? "cancel" : "end"); });
        };
        requestAnimationFrame(function () { look(2); });
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
        setTimeout(started, ms("--t-vt-start"));
      });
    }
  };
})();
