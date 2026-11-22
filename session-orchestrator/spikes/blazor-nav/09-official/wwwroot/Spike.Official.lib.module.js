// The browser side of the official custom events, and the helpers that the lab measures with.
export function afterWebStarted(blazor) {
  const motion = e => ({ name: e.animationName || e.propertyName || "", target: e.target.dataset?.id || "", elapsed: e.elapsedTime || 0, at: performance.now() });
  blazor.registerCustomEventType("motionend", { browserEventName: "animationend", createEventArgs: motion });
  blazor.registerCustomEventType("motioncancel", { browserEventName: "animationcancel", createEventArgs: motion });
  blazor.registerCustomEventType("motiontransend", { browserEventName: "transitionend", createEventArgs: motion });
  // A synthetic event that our own settle wait dispatches: JS owns the wait, Blazor delivers the end.
  blazor.registerCustomEventType("motionsettled", { browserEventName: "orch-settled", createEventArgs: e => ({ name: e.detail?.by || "", target: e.target.dataset?.id || "", elapsed: e.detail?.ms || 0, at: performance.now() }) });
  blazor.registerCustomEventType("tracedclick", { browserEventName: "click", createEventArgs: e => ({ test: e.target.closest?.("[data-test]")?.dataset.test || "", at: performance.now() }) });
  window.labEvents = { enhancedload: 0, enhancednavigationstart: 0, enhancednavigationend: 0 };
  for (const name of Object.keys(window.labEvents)) blazor.addEventListener(name, () => window.labEvents[name]++);
}

// The settle wait of motion.js, cut down: each finite animation of the element and its children ends, or a limit.
function settle(el, limitMs) {
  const start = performance.now();
  return new Promise(resolve => {
    let done = false;
    const finish = by => { if (!done) { done = true; resolve({ by, ms: Math.round(performance.now() - start) }); } };
    setTimeout(() => finish("limit"), limitMs);
    const look = frames => {
      const mine = el.getAnimations({ subtree: true }).filter(a => a.effect && a.effect.getComputedTiming().endTime !== Infinity);
      if (mine.length === 0 && frames > 0) { requestAnimationFrame(() => look(frames - 1)); return; }
      if (mine.length === 0) { finish("end"); return; }
      let canceled = false;
      Promise.all(mine.map(a => a.finished.catch(() => { canceled = true; }))).then(() => finish(canceled ? "cancel" : "end"));
    };
    requestAnimationFrame(() => look(2));
  });
}

window.lab = {
  // Called from OnAfterRenderAsync: does the DOM already have the class of the render, and does the browser see its animations?
  probe: (el, cls) => ({ hasClass: el.classList.contains(cls), animations: el.getAnimations({ subtree: true }).length, at: performance.now() }),
  wait: (el, limitMs) => settle(el, limitMs || 2000),
  // The hybrid: our wait, then a DOM event that Blazor delivers as @onmotionsettled.
  settleAndDispatch: (el, limitMs) => { settle(el, limitMs || 2000).then(r => el.dispatchEvent(new CustomEvent("orch-settled", { bubbles: true, detail: r }))); },
  now: () => performance.now(),
  back: () => history.back(),
  events: () => window.labEvents,
};
