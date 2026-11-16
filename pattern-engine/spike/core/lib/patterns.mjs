// Load, validate and index the patterns of one domain. This file knows nothing about any domain.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import YAML from 'yaml';

export const ENGINE_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
export const KINDS = ['leaf', 'object', 'group'];
export const STATUSES = ['proposed', 'trial', 'stable'];

export function domainDir(domain) {
  return path.join(ENGINE_ROOT, 'domains', domain);
}

/** Parse one pattern file: YAML front matter plus "## Section" blocks. */
export function parsePatternText(text, file = '<text>') {
  const m = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)$/);
  if (!m) throw new Error(`${file}: no front matter`);
  const data = YAML.parse(m[1]) || {};
  const sections = {};
  let cur = null;
  for (const line of m[2].split(/\r?\n/)) {
    const h = line.match(/^## (.+)$/);
    if (h) { cur = h[1].trim(); sections[cur] = ''; continue; }
    if (cur) sections[cur] += line + '\n';
  }
  for (const k of Object.keys(sections)) sections[k] = sections[k].trim();
  return { ...data, sections, file };
}

/** Load all patterns from a folder of .md files. Returns a Map id -> pattern. */
export function loadPatterns(dir, { includeProposed = true } = {}) {
  const map = new Map();
  if (!fs.existsSync(dir)) return map;
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.md')).sort()) {
    const p = parsePatternText(fs.readFileSync(path.join(dir, f), 'utf8'), f);
    if (!includeProposed && p.status === 'proposed') continue;
    map.set(p.id, p);
  }
  return map;
}

/** "1" -> {min:1,max:1}; "0..4" -> {min:0,max:4}; "1..n" -> {min:1,max:Infinity} */
export function parseCount(spec) {
  const s = String(spec ?? '1');
  const m = s.match(/^(\d+)(?:\.\.(\d+|n))?$/);
  if (!m) throw new Error(`bad count "${s}"`);
  const min = Number(m[1]);
  const max = m[2] === undefined ? min : m[2] === 'n' ? Infinity : Number(m[2]);
  return { min, max };
}

/** Parts that apply to one variant of a pattern. */
export function partsFor(pattern, variant) {
  return (pattern.parts || []).filter(pt => !pt.only || pt.only === variant);
}

/** Check the pattern set. Returns { errors, warnings }. */
export function validate(patterns) {
  const errors = [];
  const warnings = [];
  const err = (p, msg) => errors.push(`${p.file || p.id}: ${msg}`);
  for (const p of patterns.values()) {
    if (!p.id) { err(p, 'no id'); continue; }
    if (p.file && p.file !== `${p.id}.md`) err(p, `file name must be ${p.id}.md`);
    if (!KINDS.includes(p.kind)) err(p, `kind must be one of ${KINDS.join(', ')}`);
    if (!Number.isInteger(p.level)) err(p, 'level must be an integer');
    if (!STATUSES.includes(p.status)) err(p, `status must be one of ${STATUSES.join(', ')}`);
    if (!p.sections?.Intent) err(p, 'needs an "## Intent" section');
    if (p.kind === 'leaf') {
      if (!p.primitive) err(p, 'a leaf needs "primitive"');
      if (p.parts?.length) err(p, 'a leaf has no parts');
      continue;
    }
    if (!Array.isArray(p.use_when) || !p.use_when.length) err(p, 'needs "use_when"');
    if (!p.phrase) err(p, 'needs "phrase" (used by the case generator)');
    if (!Array.isArray(p.parts) || !p.parts.length) err(p, 'needs "parts"');
    if (!p.sections?.Rules) err(p, 'needs a "## Rules" section');
    const vids = (p.variants || []).map(v => v.id);
    if (!vids.length) err(p, 'needs at least one variant');
    if (new Set(vids).size !== vids.length) err(p, 'variant ids must be unique');
    for (const pt of p.parts || []) {
      const ref = patterns.get(pt.ref);
      if (!ref) { err(p, `part "${pt.ref}" does not exist`); continue; }
      if (ref.level <= p.level) err(p, `part "${pt.ref}" must have a higher level than ${p.level}`);
      if (pt.only && !vids.includes(pt.only)) err(p, `part "${pt.ref}" uses unknown variant "${pt.only}"`);
      try { parseCount(pt.count); } catch (e) { err(p, `part "${pt.ref}": ${e.message}`); }
    }
    for (const d of p.do_not_use_when || []) {
      if (!d.when || !d.use) err(p, 'each do_not_use_when entry needs "when" and "use"');
      else if (d.use !== 'no-fit' && !patterns.has(d.use)) err(p, `do_not_use_when points to missing "${d.use}"`);
    }
    for (const r of p.related || []) {
      const o = patterns.get(r);
      if (!o) err(p, `related "${r}" does not exist`);
      else if (!(o.related || []).includes(p.id)) warnings.push(`${p.id}: related "${r}" does not link back`);
    }
    for (const [k, def] of Object.entries(p.params || {})) {
      if (!(p.parts || []).some(pt => pt.ref === def.part)) err(p, `param "${k}" points to a part that is not in parts`);
    }
  }
  // do_not_use_when rules that point at each other: the agent can loop between them.
  for (const p of patterns.values()) for (const d of p.do_not_use_when || []) {
    const back = (patterns.get(d.use)?.do_not_use_when || []).find(x => x.use === p.id);
    if (back && p.id < d.use) warnings.push(`${p.id} and ${d.use}: do_not_use_when rules point at each other. Make sure that the two conditions cannot both be true.`);
  }
  // parts cycles
  const state = new Map();
  const visit = (id, trail) => {
    if (state.get(id) === 2) return;
    if (state.get(id) === 1) { errors.push(`cycle in parts: ${[...trail, id].join(' -> ')}`); return; }
    state.set(id, 1);
    for (const pt of patterns.get(id)?.parts || []) if (patterns.has(pt.ref)) visit(pt.ref, [...trail, id]);
    state.set(id, 2);
  };
  for (const id of patterns.keys()) visit(id, []);
  return { errors, warnings };
}

/** The graph as nodes and typed edges. */
export function toGraph(patterns) {
  const nodes = [];
  const edges = [];
  for (const p of patterns.values()) {
    nodes.push({ id: p.id, kind: p.kind, level: p.level, status: p.status });
    for (const pt of p.parts || []) edges.push({ from: p.id, to: pt.ref, type: 'part', count: String(pt.count ?? '1'), only: pt.only });
    for (const d of p.do_not_use_when || []) if (d.use !== 'no-fit') edges.push({ from: p.id, to: d.use, type: 'instead' });
    for (const r of p.related || []) edges.push({ from: p.id, to: r, type: 'related' });
  }
  return { nodes, edges };
}

/** The index the agent reads first. Proposed patterns are not in it. */
export function toIndexMarkdown(patterns, domain) {
  const rows = [...patterns.values()].filter(p => p.status !== 'proposed').sort((a, b) => a.level - b.level || a.id.localeCompare(b.id));
  const lines = [`# Pattern index: ${domain}`, '', 'Read the full file `patterns/<id>.md` before you use a pattern.', ''];
  lines.push('## Composed patterns', '', '| id | level | status | use when | do not use when | variants |', '|---|---|---|---|---|---|');
  for (const p of rows.filter(p => p.kind !== 'leaf')) {
    const dn = (p.do_not_use_when || []).map(d => (d.use === 'no-fit' ? `${d.when} → no pattern fits` : `${d.when} → use \`${d.use}\``)).join('<br>') || '—';
    const vs = (p.variants || []).map(v => `\`${v.id}\`: ${v.when}`).join('<br>');
    lines.push(`| \`${p.id}\` | ${p.level} | ${p.status} | ${p.use_when.join('<br>')} | ${dn} | ${vs} |`);
  }
  lines.push('', '## Leaf parts', '', '| id | level | primitive |', '|---|---|---|');
  for (const p of rows.filter(p => p.kind === 'leaf')) lines.push(`| \`${p.id}\` | ${p.level} | ${p.primitive} |`);
  return lines.join('\n') + '\n';
}
