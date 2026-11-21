// The small helpers of the flow: wait for the end of one CSS transition, wait for frames, and the frame meter of the probe.
window.s6 = {
  // Resolves when the transition of one property on one element ends, or after the time limit.
  end: function (selector, property, ms) {
    return new Promise(function (resolve) {
      var el = document.querySelector(selector), done = false;
      function finish() { if (!done) { done = true; if (el) el.removeEventListener("transitionend", on); resolve(); } }
      function on(e) { if (e.target === el && e.propertyName === property) finish(); }
      if (el) el.addEventListener("transitionend", on);
      setTimeout(finish, ms);
    });
  },
  // Resolves after n display frames: the browser drew the state before the next change.
  frames: function (n) {
    return new Promise(function (resolve) {
      function step() { if (--n <= 0) resolve(); else requestAnimationFrame(step); }
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
