// The browser side of spike 10. The same work runs in two ways: as commands of the event channel (an element
// i[data-orch-req] in the outlet; the result comes back as "orch-done" events), and as functions for IJSRuntime calls.
export function afterWebStarted(blazor) {
  blazor.registerCustomEventType("orchdone", {
    browserEventName: "orch-done",
    createEventArgs: e => ({ id: e.detail.id, phase: e.detail.phase, by: e.detail.by || "", ms: e.detail.ms || 0 }),
  });
  const seen = new WeakSet();
  const run = el => {
    if (seen.has(el)) return;
    seen.add(el);
    const id = +el.dataset.orchReq, verb = el.dataset.verb, arg = el.dataset.arg;
    const send = (phase, r) => el.dispatchEvent(new CustomEvent("orch-done", { bubbles: true, detail: { id, phase, by: r && r.by, ms: r && r.ms } }));
    if (verb === "settle") settle(document.querySelector(arg), 2000).then(r => send("end", r));
    else if (verb === "vt") {
      const t = begin(arg);
      t.captured.then(() => send("captured", { by: "captured", ms: 0 }));
      t.finished.then(r => send("finished", r));
    }
    else if (verb === "throw") throw new Error("lab error");
    else if (verb === "back") { history.back(); send("end", { by: "back", ms: 0 }); }
    else send("end", { by: "unknown", ms: 0 });
  };
  const scan = root => root.querySelectorAll && root.querySelectorAll("i[data-orch-req]").forEach(run);
  new MutationObserver(list => list.forEach(m => m.addedNodes.forEach(n => {
    if (n.nodeType !== 1) return;
    if (n.matches("i[data-orch-req]")) run(n); else scan(n);
  }))).observe(document.body, { childList: true, subtree: true });
  scan(document.body);
}

// The settle wait of motion.js, cut down.
function settle(el, limitMs) {
  const start = performance.now();
  return new Promise(resolve => {
    if (!el) { resolve({ by: "missing", ms: 0 }); return; }
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

// Resolves when Blazor changes the page (the next DOM change in the stage), or after 600 ms, as motion.js does.
function rendered() {
  return new Promise(resolve => {
    let done = false;
    const end = () => { if (!done) { done = true; observer.disconnect(); resolve(); } };
    const observer = new MutationObserver(() => setTimeout(end, 0));
    observer.observe(document.querySelector(".stage"), { childList: true, subtree: true });
    setTimeout(end, 600);
  });
}

// A view transition of the kind. "captured": the browser has the picture of the old page. "finished": the motion ended.
function begin(kind) {
  const start = performance.now();
  const root = document.documentElement;
  root.classList.add("vt");
  root.classList.toggle("vt-back", kind === "back");
  let captured;
  const c = new Promise(r => { captured = r; });
  const t = document.startViewTransition(() => { captured(); return rendered(); });
  const finished = t.finished.then(() => "end", () => "skipped").then(by => {
    root.classList.remove("vt", "vt-back");
    return { by, ms: Math.round(performance.now() - start) };
  });
  window.labLast = finished;
  return { captured: c, finished };
}

window.lab = {
  begin: kind => begin(kind).captured,
  finished: () => window.labLast,
  wait: (selector, limitMs) => settle(document.querySelector(selector), limitMs || 2000),
  back: () => history.back(),
  throw: () => { throw new Error("lab error"); },
};
