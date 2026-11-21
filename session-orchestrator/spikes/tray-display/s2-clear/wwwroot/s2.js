// The frame meter: the time between display frames for a time span, in ms.
window.s2 = {
  frames: function (ms) {
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
