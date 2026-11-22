// The measurement script of spike 09: drives each lab in headless Chromium and prints the trace lines of each test.
// Run: node flows.cjs (the app runs on port 5109).
const path = require("path");
const { chromium } = require(path.join(process.env.APPDATA, "npm/node_modules/playwright"));
const base = "http://localhost:5109/";
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const trace = async () => (await page.textContent("[data-test=full-trace]")).split("\n");
  let mark = 0;
  const since = async label => { const t = await trace(); console.log(`--- ${label}`); t.slice(mark).forEach(l => console.log("   " + l)); mark = t.length; };
  const ready = async () => { await page.waitForFunction(() => window.Blazor && document.querySelector("[data-test=full-trace]")?.textContent.includes("OnAfterRender(first)")); await wait(300); };

  await page.goto(base + "motion"); await ready(); await since("load /motion");
  for (const t of ["single", "single", "single", "stagger", "none", "cancel", "trans"]) {
    await page.click(`[data-test=m-${t}]`); await wait(t === "none" ? 2500 : 1200); await since(`motion ${t}`);
  }

  // Input: the official click hooks.
  await page.click("[data-test=tab-input]"); await wait(500); await since("tab to /input");
  for (let i = 0; i < 2; i++) { await page.click("[data-test=i-plain]"); await wait(300); }
  await since("input plain x2");
  for (let i = 0; i < 3; i++) { await page.click("[data-test=i-stop]"); await wait(300); }
  await since("input stop x3");
  for (let i = 0; i < 2; i++) { await page.click("[data-test=i-check]"); await wait(300); console.log("   checked =", await page.isChecked("[data-test=i-check]")); }
  await since("input check x2");

  // Navigation: state, order of the two handlers, the guard, a second move during the guard, browser back.
  await page.click("[data-test=tab-home]"); await wait(500); await since("tab to /");
  await page.click("[data-test=go-guard-state]"); await wait(600); await since("NavigateTo guard with state depth=1");
  await page.click("[data-test=go-deep]"); await wait(600); await since("NavigateTo guard/deep with state depth=2");
  console.log("   deep-state:", await page.textContent("[data-test=deep-state]"));
  await page.goBack(); await wait(600); await since("browser back to /guard");
  await page.goForward(); await wait(600); await since("browser forward to /guard/deep");
  await page.click("[data-test=back]"); await wait(600); await since("history.back from code to /guard");
  await page.fill("[data-test=note]", "abc"); await wait(200);
  await page.click("[data-test=tab-home]"); await wait(600); await since("tab home with note: guard bar?");
  console.log("   bar shows:", await page.isVisible("[data-test=guard]"), "url:", page.url());
  await page.click("[data-test=tab-input]"); await wait(800); await since("second move (tab input) while the bar waits");
  console.log("   bar shows:", await page.isVisible("[data-test=guard]"), "url:", page.url());
  if (await page.isVisible("[data-test=guard]")) { await page.click("[data-test=guard-stay]"); await wait(500); await since("guard-stay"); console.log("   url:", page.url()); }
  if (!page.url().endsWith("/guard")) { await page.goto(base + "guard"); await ready(); await page.fill("[data-test=note]", "abc"); mark = 0; }
  await page.goBack(); await wait(800); await since("browser back with note: guard bar?");
  console.log("   bar shows:", await page.isVisible("[data-test=guard]"), "url:", page.url());
  if (await page.isVisible("[data-test=guard]")) { await page.click("[data-test=guard-stay]"); await wait(600); await since("guard-stay on browser back"); console.log("   url:", page.url()); }
  console.log("enhanced events:", JSON.stringify(await page.evaluate(() => window.labEvents)));
  await browser.close();
})();
