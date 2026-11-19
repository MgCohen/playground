// The small script of the tray stage (Components/Tray/TrayStage.razor). The stage decides each step; this script only waits for
// the end of a CSS motion, waits for frames, and measures the place of the home button for the clip circle of the panel.
window.orchTray = {
  // Resolves when the transition of a property, or the animation of a name, ends on the element; or after the time limit.
  // A hidden page moves no motion, so it resolves at once.
  end: function (selector, name, ms) {
    return new Promise(function (resolve) {
      var el = document.querySelector(selector), done = false;
      function finish() {
        if (done) return;
        done = true;
        if (el) { el.removeEventListener("transitionend", on); el.removeEventListener("animationend", on); }
        resolve();
      }
      function on(e) { if (e.target === el && (e.propertyName === name || e.animationName === name)) finish(); }
      if (!el || document.visibilityState === "hidden") { finish(); return; }
      el.addEventListener("transitionend", on);
      el.addEventListener("animationend", on);
      setTimeout(finish, ms);
    });
  },
  // Resolves after n display frames, so the browser drew a state before the next change. A limit keeps a hidden page going.
  frames: function (n) {
    return new Promise(function (resolve) {
      var timer = setTimeout(resolve, 100);
      function step() { if (--n <= 0) { clearTimeout(timer); resolve(); } else requestAnimationFrame(step); }
      requestAnimationFrame(step);
    });
  },
  // Sets --hx and --hy on the stage: the center of the home button in the panel. With no home button (a page with no app bar),
  // the stage keeps its default (the middle of the bottom bar).
  home: function (stage) {
    var panel = stage && stage.querySelector(".tray-panel");
    var home = stage && stage.querySelector(".tray-view .nav2 .home");
    if (!panel || !home) return;
    var p = panel.getBoundingClientRect(), h = home.getBoundingClientRect();
    if (h.width === 0) { stage.style.removeProperty("--hx"); stage.style.removeProperty("--hy"); return; }
    stage.style.setProperty("--hx", (h.left + h.width / 2 - p.left) + "px");
    stage.style.setProperty("--hy", (h.top + h.height / 2 - p.top) + "px");
  }
};
