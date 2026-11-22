// The guard part of spike 09 again, after the fix of the shared bar: a second move during the bar, and the browser back.
const path = require("path");
const { chromium } = require(path.join(process.env.APPDATA, "npm/node_modules/playwright"));
const base = "http://localhost:5109/";
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  let mark = 0;
  const since = async label => { const t = (await page.textContent("[data-test=full-trace]")).split("\n"); console.log(`--- ${label}`); t.slice(mark).forEach(l => console.log("   " + l)); mark = t.length; };
  const state = async () => console.log("   bar:", await page.isVisible("[data-test=guard]"), "url:", page.url().replace(base, "/"));
  await page.goto(base); await wait(1500);
  await page.click("[data-test=go-guard-state]"); await wait(600);
  await page.fill("[data-test=note]", "abc"); await wait(200); await since("prepare: / then /guard, note abc");
  await page.click("[data-test=tab-home]"); await wait(600); await since("tab home"); await state();
  await page.click("[data-test=tab-input]"); await wait(600); await since("tab input while the bar waits"); await state();
  await page.click("[data-test=guard-stay]"); await wait(600); await since("guard-stay"); await state();
  await page.goBack(); await wait(600); await since("browser back"); await state();
  await page.click("[data-test=guard-stay]"); await wait(800); await since("guard-stay on browser back"); await state();
  console.log("   history.length:", await page.evaluate(() => history.length));
  await page.goBack(); await wait(600); await since("browser back again"); await state();
  await page.click("[data-test=guard-leave]"); await wait(800); await since("guard-leave on browser back"); await state();
  await browser.close();
})();
