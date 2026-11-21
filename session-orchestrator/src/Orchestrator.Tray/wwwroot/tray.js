// The small script of the tray (Components/Tray): the place of the home button for the clip circle of the panel, and the sizes
// of the tray for the windows. The waits of the tray are orchMotion.wait (motion.js).
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
  },

  // R2: the sizes of the tray in CSS px (TraySizes in C#). The panel sizes are tokens of tray.css. The badge boxes are shadow
  // boxes: the border box plus each outer shadow. Each badge box is measured in its own look (the tab at rest and at the
  // hover, the bubble open) on a hidden copy of the element, so the measure never touches a motion of the real element.
  sizes: async function () {
    if (document.fonts && document.fonts.ready) await document.fonts.ready;
    var root = getComputedStyle(document.documentElement);
    var token = function (name) { return parseFloat(root.getPropertyValue(name)) || 0; };
    var report = { panelWidth: token("--tray-panel-w"), panelHeight: token("--tray-panel-h"), margin: token("--tray-margin"),
      tab: token("--badge-tab"), tabTop: token("--badge-tab-top"),
      width: window.innerWidth, rest: null, hover: null, bubble: null };
    var tab = document.querySelector(".tb-tab"), bubble = document.querySelector(".tb-peek");
    if (!tab || tab.getClientRects().length === 0) return report;
    report.rest = orchTray.shadowBox(tab, { translate: "0 0", width: "var(--badge-tab)" });
    report.hover = orchTray.shadowBox(tab, { translate: "0 0", width: "auto" });
    if (bubble) report.bubble = orchTray.shadowBox(bubble, { scale: "1 1", opacity: "1" });
    return report;
  },

  // The shadow box of an element in a given look: [left, top, right, bottom]. A hidden copy next to the element gets the look
  // with no transition; the copy goes away after the measure.
  shadowBox: function (e, look) {
    var c = e.cloneNode(true);
    [c].concat(Array.prototype.slice.call(c.querySelectorAll("[data-motion]"))).forEach(function (x) { x.removeAttribute("data-motion"); });
    c.removeAttribute("aria-live");
    c.setAttribute("aria-hidden", "true");
    c.style.setProperty("visibility", "hidden");
    c.style.setProperty("transition", "none");
    c.style.setProperty("animation", "none");
    Object.keys(look).forEach(function (k) { c.style.setProperty(k, look[k]); });
    e.parentNode.insertBefore(c, e.nextSibling);
    var r = c.getBoundingClientRect(), b = [r.left, r.top, r.right, r.bottom];
    getComputedStyle(c).boxShadow.split(/,(?![^(]*\))/).forEach(function (s) {
      if (s.trim() === "none" || /\binset\b/.test(s)) return;
      var n = s.replace(/rgba?\([^)]*\)|#[0-9a-f]+\b/gi, " ").replace(/px/g, " ").trim().split(/\s+/).map(Number);
      var x = n[0] || 0, y = n[1] || 0, g = (n[2] || 0) + (n[3] || 0);
      b[0] = Math.min(b[0], r.left + x - g); b[1] = Math.min(b[1], r.top + y - g);
      b[2] = Math.max(b[2], r.right + x + g); b[3] = Math.max(b[3], r.bottom + y + g);
    });
    c.remove();
    return b.map(function (v) { return Math.round(v * 10) / 10; });
  }
};
