// The small script of the tray stage (Components/Tray/TrayStage.razor): it measures the place of the home button for the clip
// circle of the panel. The waits of the tray are orchMotion.wait (motion.js).
window.orchTray = {
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
