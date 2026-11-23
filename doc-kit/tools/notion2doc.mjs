// Read a Notion page through the REST API and write doc.json for doc.html.
// Usage: NOTION_TOKEN=... node tools/notion2doc.mjs <page id or url> [out.json]
// Mapping rules: CONVENTIONS.md.
import { writeFileSync } from "node:fs";

const TOKEN = process.env.NOTION_TOKEN;
if (!TOKEN) { console.error("NOTION_TOKEN is not set"); process.exit(2); }
const arg = process.argv[2] || "";
const PAGE = (arg.match(/[0-9a-f]{32}|[0-9a-f-]{36}/i) || [""])[0].replace(/-/g, "");
if (!PAGE) { console.error("Give a page id or url"); process.exit(2); }
const OUT = process.argv[3] || "doc.json";

async function api(path) {
  for (let i = 0; i < 4; i++) {
    const r = await fetch("https://api.notion.com/v1/" + path, {
      headers: { Authorization: "Bearer " + TOKEN, "Notion-Version": "2022-06-28" },
    });
    if (r.status === 429) { await new Promise((s) => setTimeout(s, 1000 * (i + 1))); continue; }
    if (!r.ok) throw new Error(path + ": " + r.status + " " + (await r.text()).slice(0, 300));
    return r.json();
  }
  throw new Error(path + ": rate limited");
}
async function children(id) {
  let out = [], cursor;
  do {
    const r = await api(`blocks/${id}/children?page_size=100` + (cursor ? "&start_cursor=" + cursor : ""));
    out = out.concat(r.results);
    cursor = r.has_more ? r.next_cursor : null;
  } while (cursor);
  for (const b of out) if (b.has_children) b._kids = await children(b.id);
  return out;
}

// Rich text to inline text with `code` and **bold** marks.
const md = (rt = []) => rt.map((t) => {
  let s = t.plain_text;
  if (t.annotations?.code) s = "`" + s + "`";
  else if (t.annotations?.bold && s.trim()) s = "**" + s + "**";
  return s;
}).join("");
const plain = (rt = []) => rt.map((t) => t.plain_text).join("");
const body = (b) => b[b.type] || {};

// ---- kit blocks from text ---------------------------------------------------
function indentOf(line) { return line.match(/^ */)[0].length; }

function parseTree(lines) {
  const rows = [], stack = [];
  lines.forEach((line, i) => {
    if (!line.trim()) return;
    const depth = Math.floor(indentOf(line) / 2);
    let rest = line.trim(), when, kind;
    if (/\s!later\s*$/.test(rest)) { when = "later"; rest = rest.replace(/\s!later\s*$/, ""); }
    const k = rest.match(/\s\[([^\]]+)\]\s*$/);
    if (k) { kind = k[1]; rest = rest.slice(0, k.index); }
    const [name, ...desc] = rest.split(/\s{2,}/);
    stack.length = depth;
    stack[depth] = name.replace(/\/$/, "");
    const row = { path: stack.slice(0, depth + 1).join("/"), desc: desc.join("  ").trim(), order: i };
    if (kind) row.kind = kind;
    if (when) row.when = when;
    rows.push(row);
  });
  return { type: "tree", data: { tags: { later: "warn", kind: "neutral" }, rows } };
}

function parseStack(lines) {
  const root = [], stack = [], foot = [];
  for (const line of lines) {
    if (!line.trim()) continue;
    if (line.trim().startsWith(">")) { foot.push(line.trim().slice(1).trim()); continue; }
    const depth = Math.floor(indentOf(line) / 2);
    let [call, ...note] = line.trim().split("//");
    const node = { call: call.trim() };
    if (node.call.startsWith("*")) { node.call = node.call.slice(1); node.key = true; }
    if (note.length) node.note = note.join("//").trim();
    stack.length = depth;
    (depth && stack[depth - 1] ? (stack[depth - 1].children ||= []) : root).push(node);
    stack[depth] = node;
  }
  const s = { root };
  if (foot.length) s.foot = foot.join(" ");
  return s;
}

function parseMermaid(text) {
  const nodes = new Map(), edges = [], kinds = {};
  const node = (tok) => {
    const m = tok.trim().match(/^([\w-]+)\s*(?:\[\s*"?(.*?)"?\s*\]|\(\s*"?(.*?)"?\s*\))?(?::::(\w+))?$/);
    if (!m) return null;
    const id = m[1], label = m[2] ?? m[3];
    if (!nodes.has(id)) nodes.set(id, { id, label: id });
    const n = nodes.get(id);
    if (label != null) {
      const [l, ...sub] = label.split(/<br\s*\/?>/i);
      n.label = l.trim();
      if (sub.length) n.sub = sub.join(" ").trim();
    }
    if (m[4]) kinds[id] = m[4];
    return id;
  };
  for (let line of text.split("\n")) {
    line = line.trim();
    if (!line || /^(flowchart|graph)\b/.test(line) || line.startsWith("%%")) continue;
    const cls = line.match(/^class\s+([\w,\s-]+?)\s+(\w+)$/);
    if (cls) { cls[1].split(",").forEach((id) => (kinds[id.trim()] = cls[2])); continue; }
    if (/^classDef\b/.test(line)) continue;
    // a -->|label| b, a -- label --> b, a -.-> b, a -.->|label| b
    const parts = line.split(/\s*(-\.->|-->|---|==>)\s*/);
    if (parts.length >= 3) {
      for (let i = 0; i + 2 < parts.length; i += 2) {
        let right = parts[i + 2], label;
        const lm = right.match(/^\|([^|]*)\|\s*(.*)$/);
        if (lm) { label = lm[1].trim(); right = lm[2]; }
        const a = node(parts[i]), b = node(right);
        parts[i + 2] = right;
        if (a && b) edges.push({ from: a, to: b, ...(label ? { label } : {}), ...(parts[i + 1] === "-.->" ? { dashed: true } : {}) });
      }
      continue;
    }
    node(line);
  }
  // rows: the longest path from a node with no incoming edge
  const ids = [...nodes.keys()], rank = Object.fromEntries(ids.map((id) => [id, 0]));
  for (let pass = 0; pass < ids.length; pass++) {
    let changed = false;
    for (const e of edges) {
      if (e.dashed && rank[e.to] <= rank[e.from]) { // a dashed edge puts the target beside the source
        if (rank[e.to] !== rank[e.from]) { rank[e.to] = rank[e.from]; changed = true; }
        continue;
      }
      if (!e.dashed && rank[e.to] < rank[e.from] + 1) { rank[e.to] = rank[e.from] + 1; changed = true; }
    }
    if (!changed) break;
  }
  const rowsMap = {};
  ids.forEach((id) => (rowsMap[rank[id]] ||= []).push(id));
  const rowCount = Math.max(0, ...Object.keys(rowsMap).map(Number)) + 1;
  const rows = [];
  for (let r = 0; r < rowCount; r++) rows.push(Math.max(3, (rowsMap[r] || []).length));
  const out = ids.map((id) => {
    const r = rank[id], list = rowsMap[r], n = nodes.get(id);
    const offset = Math.floor((rows[r] - list.length) / 2);
    return { ...n, kind: kinds[id] || "plain", row: r, col: list.indexOf(id) + offset };
  });
  const legend = [["key", "key types"], ["data", "data"], ["outside", "outside"]].filter(([k]) => out.some((n) => n.kind === k)).map(([kind, text]) => ({ kind, text }));
  return { type: "graph", data: { rows, nodes: out, edges, legend } };
}

function parseColumns(b) {
  const groups = (b._kids || []).filter((c) => c.type === "column").map((col, gi) => {
    const g = { id: "g" + gi, title: "", desc: "", items: [] };
    for (const k of col._kids || []) {
      const t = body(k).rich_text;
      if (!g.title && (k.type.startsWith("heading") || (k.type === "paragraph" && t?.[0]?.annotations?.bold))) { g.title = plain(t); continue; }
      if (k.type === "paragraph" && !g.items.length && plain(t)) { g.desc = md(t); continue; }
      if (k.type === "bulleted_list_item" || k.type === "numbered_list_item") {
        const name = t.filter((x) => x.annotations?.bold).map((x) => x.plain_text).join("").trim();
        const meta = (t.find((x) => x.annotations?.code)?.plain_text || "").trim(); // the first inline code only
        const full = plain(t), dash = full.search(/\s[—–-]\s/);
        g.items.push({ name: name || full.split(/\s[—–-]\s/)[0], ...(meta ? { meta } : {}), desc: dash >= 0 ? full.slice(dash + 3).trim() : "" });
      }
    }
    return g;
  });
  return { type: "columns", data: { groups } };
}

// ---- content blocks ---------------------------------------------------------
function codeBlock(b) {
  const c = body(b), text = plain(c.rich_text), lang = c.language;
  const lines = text.split("\n"), first = lines[0].trim();
  if (lang === "mermaid") return parseMermaid(text.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&amp;/g, "&"));
  if (first === "dk:tree") return parseTree(lines.slice(1));
  if (first === "dk:stack") return { type: "stack", data: { groups: [{ title: "", stacks: [parseStack(lines.slice(1))] }] } };
  const map = { "c#": "cs", cs: "cs", csharp: "cs", json: "json", xml: "xml", html: "xml" };
  const cap = plain(c.caption);
  return { type: "code", lang: map[lang] || "", code: text, ...(cap ? { cap } : {}) };
}

function fold(b) {
  const text = md(body(b).rich_text), i = text.search(/\s[—–]\s/);
  const f = { title: i >= 0 ? text.slice(0, i).trim() : text, summary: i >= 0 ? text.slice(i + 3).trim() : "" };
  const kids = convert(b._kids || []);
  const childFolds = kids.filter((k) => k.type === "folds").flatMap((k) => k.data.items);
  f.body = kids.filter((k) => k.type !== "folds");
  if (childFolds.length) f.children = childFolds;
  if (/[./]/.test(f.title) && !/\s/.test(f.title)) f.mono = true;
  return f;
}

function convert(blocks) {
  const out = [];
  const last = () => out[out.length - 1];
  for (const b of blocks) {
    const t = b.type, c = body(b);
    if (t === "paragraph") { const s = md(c.rich_text); if (s.trim()) out.push({ type: "p", text: s }); }
    else if (t === "heading_3" && !c.is_toggleable) out.push({ type: "h", text: plain(c.rich_text) });
    else if (t === "bulleted_list_item" || t === "numbered_list_item") {
      const ordered = t === "numbered_list_item";
      if (last()?.type === "list" && !!last().ordered === ordered) last().items.push(md(c.rich_text));
      else out.push({ type: "list", ordered, items: [md(c.rich_text)] });
    }
    else if (t === "code") {
      const k = codeBlock(b);
      if (k.type === "stack" && last()?.type === "stack") last().data.groups[0].stacks.push(...k.data.groups[0].stacks);
      else out.push(k);
    }
    else if (t === "table") {
      const rows = (b._kids || []).map((r) => r.table_row.cells.map(md));
      out.push({ type: "table", head: c.has_column_header ? rows.shift() : [], rows });
    }
    else if (t === "callout") out.push({ type: "callout", text: md(c.rich_text), icon: c.icon?.emoji || "" });
    else if (t === "quote") out.push({ type: "callout", text: md(c.rich_text) });
    else if (t === "column_list") out.push(parseColumns(b));
    else if (t === "toggle" || (t.startsWith("heading") && c.is_toggleable)) {
      const f = fold(b);
      if (last()?.type === "folds") last().data.items.push(f);
      else out.push({ type: "folds", data: { items: [f] } });
    }
    else if (t === "divider") { /* no block */ }
    else if (c.rich_text) out.push({ type: "p", text: md(c.rich_text) });
  }
  return out;
}

const page = await api("pages/" + PAGE);
const titleProp = Object.values(page.properties).find((p) => p.type === "title");
const blocks = await children(PAGE);
const doc = { title: plain(titleProp?.title), source: { kind: "notion", url: page.url, edited: page.last_edited_time }, sections: [] };
let section = null, intro = [];
for (const b of blocks) {
  if (b.type === "heading_2" && !b.heading_2.is_toggleable) { section = { title: plain(b.heading_2.rich_text), raw: [] }; doc.sections.push(section); continue; }
  if (b.type === "heading_1") { section = { title: plain(b.heading_1.rich_text), raw: [] }; doc.sections.push(section); continue; }
  (section ? section.raw : intro).push(b);
}
const introBlocks = convert(intro);
if (introBlocks[0]?.type === "p") doc.summary = introBlocks.shift().text;
if (introBlocks.length) doc.sections.unshift({ title: "", blocks: introBlocks });
for (const s of doc.sections) if (s.raw) { s.blocks = convert(s.raw); delete s.raw; }
writeFileSync(OUT, JSON.stringify(doc, null, 1));
const count = (bs) => bs.reduce((n, b) => n + 1, 0);
console.log(`wrote ${OUT}: "${doc.title}", ${doc.sections.length} sections, ${doc.sections.reduce((n, s) => n + count(s.blocks), 0)} blocks: ` +
  doc.sections.flatMap((s) => s.blocks.map((b) => b.type)).join(" "));
