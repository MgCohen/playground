// The helpers of S7: one wait for the end of all motion in a block (as orchMotion.settle), frames, and the probe meters.
window.s7 = {
  // Resolves when each finite animation and transition in the block ends, or after 1 s. One frame first, so the new class
  // is in the styles.
  settle: function (selector) {
    return new Promise(function (resolve) {
      var el = document.querySelector(selector);
      var timer = setTimeout(resolve, 1000);
      requestAnimationFrame(function () {
        var all = el ? el.getAnimations({ subtree: true }) : [];
        var finite = all.filter(function (a) { return a.effect && a.effect.getComputedTiming().endTime !== Infinity; });
        Promise.all(finite.map(function (a) { return a.finished.catch(function () { }); })).then(function () { clearTimeout(timer); resolve(); });
      });
    });
  },
  frames: function (n) {
    return new Promise(function (resolve) {
      var timer = setTimeout(resolve, 100);
      function step() { if (--n <= 0) { clearTimeout(timer); resolve(); } else requestAnimationFrame(step); }
      requestAnimationFrame(step);
    });
  },
  // The probe: the time between display frames for a time span, in ms.
  meter: function (ms) {
    return new Promise(function (resolve) {
      var times = [], start = performance.now(), last = start;
      function step(now) {
        times.push(now - last);
        last = now;
        if (now - start < ms) requestAnimationFrame(step);
        else resolve(times.slice(1));
      }
      requestAnimationFrame(step);
    });
  }
};
// The probe: the time of the last change of the page visibility, and of the first frame after it.
window.s7seen = { visible: null, frame: null };
document.addEventListener("visibilitychange", function () {
  if (document.visibilityState !== "visible") return;
  window.s7seen = { visible: performance.timeOrigin + performance.now(), frame: null };
  requestAnimationFrame(function () { requestAnimationFrame(function () { window.s7seen.frame = performance.timeOrigin + performance.now(); }); });
});
