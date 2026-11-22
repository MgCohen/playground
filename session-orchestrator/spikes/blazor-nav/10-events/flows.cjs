// The measurement script of spike 10: 5 push-and-back rounds in each mode (event channel, IJSRuntime calls).
// Run: node flows.cjs (the app runs on port 5110). It prints the trace lines and a summary table.
const path = require("path");
const { chromium } = require(path.join(process.env.APPDATA, "npm/node_modules/playwright"));
const base = "http://localhost:5110/";
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", e => errors.push(e.message));
  const lines = async () => (await page.textContent("[data-test=full-trace]")).split("\n");
  await page.goto(base);
  await page.waitForFunction(() => document.querySelector("[data-test=full-trace]")?.textContent.includes("OnAfterRender(first)"));
  await wait(500);
  for (const mode of ["events", "interop", "events", "interop"]) {
    await page.click(`[data-test=mode-${mode}]`); await wait(200);
    for (let i = 0; i < 5; i++) {
      await page.click("[data-test=go-deep]"); await wait(900);
      await page.click("[data-test=back]"); await wait(900);
    }
  }
  await page.click("[data-test=err-events]"); await wait(2500);
  await page.click("[data-test=err-interop]"); await wait(800);
  const all = await lines();
  all.forEach(l => console.log(l));
  console.log("page errors:", JSON.stringify(errors));
  await browser.close();
})();
