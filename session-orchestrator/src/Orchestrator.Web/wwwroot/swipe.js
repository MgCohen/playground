// Swipe on the top card of the pile: right and left run the card actions, down skips. A tap opens the card.
// The script only moves the card and clicks the action buttons; the acts stay in Blazor.
// M1: an act that sends the card away (data-throw) throws it out in the direction of the act, also from a button.
// M2: skip tucks the card under the pile first. Both click the button again when the motion ends.
(function () {
  var minX = 90, minY = 110;
  var drag = null;

  function button(card, name) {
    var screen = card.closest(".deck-screen");
    var b = screen && screen.querySelector('[data-act="' + name + '"]');
    return b && !b.disabled ? b : null;
  }

  // A click of the script itself: the capture listener below lets it through to Blazor.
  function go(b) {
    b.dataset.go = "1";
    b.click();
  }

  function reset(card) {
    card.classList.remove("drag");
    card.classList.add("back-in");
    card.style.transform = "";
    card.style.setProperty("--sr", 0);
    card.style.setProperty("--sl", 0);
    card.style.setProperty("--sd", 0);
    setTimeout(function () { card.classList.remove("back-in"); }, 450);
  }

  // Wait for the end of the motion, with a time limit when the motion does not run (reduced motion).
  function after(card, type, ms, then) {
    var done = false;
    function end() { if (!done) { done = true; then(); } }
    card.addEventListener(type, end, { once: true });
    setTimeout(end, ms + 80);
  }

  function fly(card, dir, dx, dy, b) {
    card.classList.remove("drag", "back-in");
    // M1 A: from a drag, the card flies on 360 px past the release point and 60 px down; a button throw (no drag) uses the
    // keyframe defaults of app.css (120 % to the side, 30 px down).
    if (dx || dy) {
      card.style.setProperty("--fx", (dx + dir * 360) + "px");
      card.style.setProperty("--fy", (Math.max(0, dy) + 60) + "px");
    } else {
      card.style.removeProperty("--fx");
      card.style.removeProperty("--fy");
    }
    card.style.setProperty(dir > 0 ? "--sr" : "--sl", 1);
    card.classList.add(dir > 0 ? "out-r" : "out-l");
    after(card, "animationend", 300, function () { go(b); });
  }

  function tuck(card, b) {
    card.classList.remove("drag", "back-in");
    card.style.transform = "";
    card.style.setProperty("--sd", 0);
    card.classList.add("tuck");
    after(card, "transitionend", 180, function () {
      go(b);
      // Blazor sets the class of a card that moves to a back slot. A card that stays on top (a pile of one) rises again.
      setTimeout(function () { card.classList.remove("tuck"); }, 60);
    });
  }

  // A button click on the deck: throw or tuck the top card first, then let the click through.
  window.addEventListener("click", function (e) {
    var b = e.target.closest && e.target.closest(".deck-screen .acts [data-act]");
    if (!b) return;
    if (b.dataset.go) { delete b.dataset.go; return; }
    var card = document.querySelector(".deck-screen [data-deck-card]");
    var name = b.dataset.act;
    if (!card || (name !== "skip" && !b.dataset.throw)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    if (name === "skip") tuck(card, b);
    else fly(card, name === "right" ? 1 : -1, 0, 0, b);
  }, true);

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
      if (b) go(b);
    }
  }

  document.addEventListener("pointerup", end);
  document.addEventListener("pointercancel", function (e) {
    if (!drag || e.pointerId !== drag.id) return;
    reset(drag.card);
    drag = null;
  });
})();
