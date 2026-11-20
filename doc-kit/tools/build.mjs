// Make a folder that holds one doc: index.html (the doc template), kit.css, kit.js and doc.json.
// Usage: node tools/build.mjs <doc.json> <out folder> [--doctype]
// --doctype adds <!doctype html> for a local preview. Do not use it for an artifact: the host adds it.
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const [src, out, flag] = process.argv.slice(2);
if (!src || !out) { console.error("Usage: node tools/build.mjs <doc.json> <out folder> [--doctype]"); process.exit(2); }
const doc = JSON.parse(readFileSync(src, "utf8")); // fails on bad JSON before anything is copied
mkdirSync(out, { recursive: true });
let html = readFileSync(join(root, "doc.html"), "utf8").replace("<title>Doc</title>", "<title>" + (doc.title || "Doc").replace(/</g, "&lt;") + "</title>");
if (flag === "--doctype") html = '<!doctype html>\n<meta name="viewport" content="width=device-width, initial-scale=1">\n' + html;
writeFileSync(join(out, "index.html"), html);
copyFileSync(join(root, "kit.css"), join(out, "kit.css"));
copyFileSync(join(root, "kit.js"), join(out, "kit.js"));
writeFileSync(join(out, "doc.json"), JSON.stringify(doc));
console.log(`built ${out}: "${doc.title}", ${doc.sections.length} sections`);
