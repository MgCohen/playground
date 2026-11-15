// Walk the pattern graph and write eval cases with computed expectations.
// The request text comes from the "phrase" fields, so this file has no domain words.
// Usage: node core/graph/generate-cases.mjs <domain> [count] [seed]
import fs from 'node:fs';
import path from 'node:path';
import { domainDir, loadPatterns, partsFor } from '../lib/patterns.mjs';
import { assertValid } from '../lib/domain.mjs';

function rng(seed) {
  let s = seed >>> 0;
  return () => { s = (s + 0x6d2b79f5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

/** One object instance: random variant, sometimes one param. Returns { tree, text }. */
function objectInstance(p, rand) {
  const variants = p.variants || [];
  const v = variants[Math.floor(rand() * variants.length)];
  const tree = { p: p.id, v: v.id };
  const mods = [];
  if (v.phrase) mods.push(v.phrase);
  const params = Object.entries(p.params || {});
  if (params.length && rand() < 0.5) {
    const [k, def] = params[Math.floor(rand() * params.length)];
    const value = def.values[Math.floor(rand() * def.values.length)];
    tree.params = { [k]: value };
    mods.push(def.phrase.replace('{value}', value));
  }
  // "with a chimney" + "with a red door" -> "with a chimney and a red door"
  const tail = mods.map((m, i) => (i === 0 ? m : m.replace(/^with /, ''))).join(' and ');
  return { tree, text: tail ? `${p.phrase} ${tail}` : p.phrase };
}

function joinList(items) {
  if (items.length < 2) return items.join('');
  return items.slice(0, -1).join('; ') + '; and ' + items[items.length - 1];
}

export function generate(domain, count = 15, seed = 7, maxChildren = 4) {
  const patterns = loadPatterns(path.join(domainDir(domain), 'patterns'), { includeProposed: false });
  assertValid(patterns);
  const rand = rng(seed);
  const objects = [...patterns.values()].filter(p => p.kind === 'object');
  const groups = [...patterns.values()].filter(p => p.kind === 'group');
  const cases = [];
  const seen = new Set();
  let guard = 0;
  while (cases.length < count && guard++ < 1000) {
    let c;
    if (rand() < 0.35 || !groups.length) {
      const p = objects[Math.floor(rand() * objects.length)];
      const o = objectInstance(p, rand);
      c = { request: `Draw ${o.text}.`, expect: { outcome: 'match', tree: o.tree }, tags: ['generated', 'object'] };
    } else {
      const g = groups[Math.floor(rand() * groups.length)];
      const gv = g.variants[0];
      const childTypes = partsFor(g, gv.id).filter(pt => patterns.get(pt.ref)?.kind === 'object').map(pt => patterns.get(pt.ref));
      const n = 2 + Math.floor(rand() * (maxChildren - 1));
      const kids = Array.from({ length: n }, () => objectInstance(childTypes[Math.floor(rand() * childTypes.length)], rand));
      c = {
        request: `Draw ${g.phrase} with, from left to right: ${joinList(kids.map(k => k.text))}.`,
        expect: { outcome: 'match', tree: { p: g.id, v: gv.id, parts: kids.map(k => k.tree) } },
        tags: ['generated', 'group', n >= 4 ? 'deep' : 'medium'],
      };
    }
    if (seen.has(c.request)) continue;
    seen.add(c.request);
    const prefix = seed === 7 ? 'gen' : `gen-s${seed}`;
    if (seed !== 7) c.tags.push('heldout');
    cases.push({ id: `${prefix}-${String(cases.length + 1).padStart(3, '0')}`, source: 'generator', ...c });
  }
  return cases;
}

if (process.argv[1]?.endsWith('generate-cases.mjs')) {
  const domain = process.argv[2] || 'shapes';
  const cases = generate(domain, Number(process.argv[3] || 15), Number(process.argv[4] || 7));
  const out = path.join(domainDir(domain), 'build');
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'cases.generated.json'), JSON.stringify(cases, null, 2) + '\n');
  for (const c of cases) console.log(c.id, '|', c.request);
  console.log(`Wrote ${cases.length} cases to build/cases.generated.json`);
}
