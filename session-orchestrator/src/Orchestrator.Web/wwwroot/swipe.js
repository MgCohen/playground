// Swipe on a decision card: right accepts the default, left opens the free-text answer.
(function () {
  var minDistance = 80;
  var start = null;

  document.addEventListener("touchstart", function (e) {
    var card = e.target.closest("[data-swipe-card]");
    if (!card || e.target.closest("textarea, button, a")) return;
    start = { card: card, x: e.touches[0].clientX, y: e.touches[0].clientY };
  }, { passive: true });

  document.addEventListener("touchmove", function (e) {
    if (!start) return;
    var dx = e.touches[0].clientX - start.x;
    if (Math.abs(dx) > Math.abs(e.touches[0].clientY - start.y)) start.card.style.transform = "translateX(" + dx / 3 + "px)";
  }, { passive: true });

  document.addEventListener("touchend", function (e) {
    if (!start) return;
    var card = start.card;
    var dx = e.changedTouches[0].clientX - start.x;
    var dy = e.changedTouches[0].clientY - start.y;
    card.style.transform = "";
    start = null;
    if (Math.abs(dx) < minDistance || Math.abs(dx) < Math.abs(dy) * 2) return;
    if (dx > 0) {
      var accept = card.querySelector("[data-swipe-right]");
      if (accept) accept.click();
    } else {
      var answer = card.querySelector("[data-swipe-left]");
      if (answer) answer.focus();
    }
  });
})();
