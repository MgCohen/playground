// Swipe on the top card of the pile: right and left run the card actions, down skips. A tap opens the card.
// The script only moves the card and clicks the action buttons; the acts stay in Blazor.
(function () {
  var minX = 90, minY = 110;
  var drag = null;

  function act(card, name) {
    var screen = card.closest(".deck-screen");
    var button = screen && screen.querySelector('[data-act="' + name + '"]');
    if (button && !button.disabled) button.click();
  }

  function reset(card) {
    card.classList.remove("drag");
    card.classList.add("back-in");
    card.style.transform = "";
    card.style.setProperty("--sr", 0);
    card.style.setProperty("--sl", 0);
    card.style.setProperty("--sd", 0);
    setTimeout(function () { card.classList.remove("back-in"); }, 220);
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
    reset(d.card);
    if (d.dx > minX) act(d.card, "right");
    else if (d.dx < -minX) act(d.card, "left");
    else if (d.dy > minY && d.dy > Math.abs(d.dx)) act(d.card, "skip");
  }

  document.addEventListener("pointerup", end);
  document.addEventListener("pointercancel", function (e) {
    if (!drag || e.pointerId !== drag.id) return;
    reset(drag.card);
    drag = null;
  });
})();
