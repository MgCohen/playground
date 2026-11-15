// Structure grader. Checks the output against the pattern definitions:
// known patterns, valid variants, part counts, leaf form (adapter) and pattern asserts (adapter).
// Adapter contract: parse(text) -> { roots: Node[], errors: string[] }, Node = { p, v, children: Node[], ... }.
import { parseCount, partsFor } from '../../lib/patterns.mjs';

export function gradeStructure(parsed, patterns, adapter, { proposedId } = {}) {
  const failures = [];
  const fail = (where, msg) => failures.push(`${where}: ${msg}`);
  if (parsed.errors?.length) parsed.errors.forEach(e => fail('parse', e));
  if (!parsed.roots?.length) fail('output', 'no pattern found');
  if (parsed.roots?.length > 1) fail('output', `expected one top pattern, got ${parsed.roots.length}`);

  const walk = (node, where) => {
    const here = where ? `${where} > ${node.p}` : node.p;
    const def = patterns.get(node.p);
    if (!def) {
      // A no-fit run builds its proposed pattern. Its own parts must still be known.
      if (node.p === proposedId) { node.children.forEach(c => walk(c, here)); return; }
      fail(here, `unknown pattern "${node.p}"`);
      return;
    }
    if (def.kind === 'leaf') {
      if (node.children.length) fail(here, 'a leaf part has child patterns');
      for (const m of adapter.checkLeaf(node, def)) fail(here, m);
      return;
    }
    const vids = (def.variants || []).map(v => v.id);
    if (!vids.includes(node.v)) fail(here, `variant "${node.v ?? '-'}" is not one of ${vids.join(', ')}`);
    const specs = partsFor(def, node.v);
    const allowed = new Set(specs.map(s => s.ref));
    for (const c of node.children) if (!allowed.has(c.p)) fail(here, `part "${c.p}" is not allowed here`);
    for (const s of specs) {
      const { min, max } = parseCount(s.count);
      const n = node.children.filter(c => c.p === s.ref).length;
      if (n < min || n > max) fail(here, `part "${s.ref}": count ${n}, allowed ${s.count ?? 1}`);
    }
    for (const r of adapter.assert(node, def.asserts || [], { pattern: def, patterns })) {
      if (!r.ok) fail(here, `assert "${r.assert}" failed${r.detail ? ` (${r.detail})` : ''}`);
    }
    node.children.forEach(c => walk(c, here));
  };
  (parsed.roots || []).forEach(r => walk(r, ''));
  return { ok: failures.length === 0, failures };
}

/** The pattern tree of the output, without leaf parts. Params are read by the adapter. */
export function builtTree(node, patterns, adapter) {
  const def = patterns.get(node.p);
  const t = { p: node.p };
  if (node.v) t.v = node.v;
  for (const [k, pdef] of Object.entries(def?.params || {})) {
    const val = adapter.readParam(node, k, pdef);
    if (val !== undefined) (t.params ||= {})[k] = val;
  }
  const kids = node.children.filter(c => patterns.get(c.p)?.kind !== 'leaf' && patterns.has(c.p));
  if (kids.length) t.parts = kids.map(c => builtTree(c, patterns, adapter));
  return t;
}
