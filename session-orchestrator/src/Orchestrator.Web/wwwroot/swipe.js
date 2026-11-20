// Swipe on the top card of the pile: right and left run the card actions, down skips. A tap opens the card.
// The script only follows the pointer (the drag math) and clicks the action buttons. The acts and their motion are in C#
// (Cards.razor, S8): C# sets the motion class (out-r, out-l, tuck), waits for the end through the motion pipeline, then acts.
// M1: at the release of a throw the script writes the fly place (--fx, --fy); M2: a skip clears the drag first.
(function () {
  var minX = 90, minY = 110;
  var drag = null;

  function button(card, name) {
    var screen = card.closest(".deck-screen");
    var b = screen && screen.querySelector('[data-act="' + name + '"]');
    return b && !b.disabled ? b : null;
  }

  // A short drag goes back with a small spring (pointer response, so it stays in the script). The class goes when the
  // transform transition ends (orchMotion.wait, limit --t-limit).
  function reset(card) {
    card.classList.remove("drag");
    card.classList.add("back-in");
    card.style.transform = "";
    card.style.setProperty("--sr", 0);
    card.style.setProperty("--sl", 0);
    card.style.setProperty("--sd", 0);
    orchMotion.wait(card, "property", "transform").then(function () { card.classList.remove("back-in"); });
  }

  // M1 A: from a drag, the card flies on 360 px past the release point and 60 px down. C# adds the throw class.
  function fly(card, dir, dx, dy, b) {
    card.classList.remove("drag", "back-in");
    card.style.setProperty("--fx", (dx + dir * 360) + "px");
    card.style.setProperty("--fy", (Math.max(0, dy) + 60) + "px");
    card.style.setProperty(dir > 0 ? "--sr" : "--sl", 1);
    b.click();
  }

  // M2 A: the drag place goes, then C# tucks the card.
  function tuck(card, b) {
    card.classList.remove("drag", "back-in");
    card.style.transform = "";
    card.style.setProperty("--sd", 0);
    b.click();
  }

  document.addEventListener("pointerdown", function (e) {
    var card = e.target.closest("[data-deck-card]");
    if (!card || e.button > 0) return;
    drag = { card: card, x: e.clientX, y: e.clientY, dx: 0, dy: 0, moved: false, id: e.pointerId };
  });

  document.addEventListener("pointermove", function (e) {
    if (!drag || e.pointerId !== drag.id) return;
    drag.dx = e.clientX - drag.x;
    drag.dy = e.clientY - drag.y;
    if (!drag.moved && Math.abs(drag.dx) < 8 && Math.abs(drag.dy) < 8) return;
    if (!drag.moved) {
      drag.moved = true;
      drag.card.classList.add("drag");
      try { drag.card.setPointerCapture(e.pointerId); } catch (_) { }
    }
    var down = Math.max(0, drag.dy);
    drag.card.style.transform = "translate(" + drag.dx + "px," + down + "px) rotate(" + (drag.dx / 20) + "deg)";
    drag.card.style.setProperty("--sr", Math.max(0, Math.min(1, drag.dx / minX)));
    drag.card.style.setProperty("--sl", Math.max(0, Math.min(1, -drag.dx / minX)));
    drag.card.style.setProperty("--sd", Math.max(0, Math.min(1, (down - Math.abs(drag.dx)) / minY)));
    if (e.cancelable) e.preventDefault();
  }, { passive: false });

  function end(e) {
    if (!drag || e.pointerId !== drag.id) return;
    var d = drag;
    drag = null;
    if (!d.moved) return;
    d.card.addEventListener("click", function stop(c) { c.preventDefault(); c.stopPropagation(); }, { capture: true, once: true });
    var name = d.dx > minX ? "right" : d.dx < -minX ? "left" : d.dy > minY && d.dy > Math.abs(d.dx) ? "skip" : null;
    var b = name && button(d.card, name);
    if (b && name === "skip") tuck(d.card, b);
    else if (b && b.dataset.throw) fly(d.card, name === "right" ? 1 : -1, d.dx, d.dy, b);
    else {
      reset(d.card);
      if (b) b.click();
    }
  }

  document.addEventListener("pointerup", end);
  document.addEventListener("pointercancel", function (e) {
    if (!drag || e.pointerId !== drag.id) return;
    reset(drag.card);
    drag = null;
  });
})();
