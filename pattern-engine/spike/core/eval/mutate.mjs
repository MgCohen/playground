// Grader test with generated known-bad outputs.
// For each case, the pack draws the expected tree (reference.mjs: known good).
// Each operator from the pack (mutations.mjs) breaks that drawing in one way (known bad).
// The graders must fail each broken drawing, and for the reason that the operator names.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { gradeStructure, builtTree } from './graders/structure.mjs';
import { gradeBuilt } from './graders/selection.mjs';

/** Grade one output text with the deterministic graders. Returns the names of the failed graders. */
export function gradeText(text, expect, d) {
  const parsed = d.adapter.parse(text);
  const failed = {};
  const s = gradeStructure(parsed, d.patterns, d.adapter);
  if (!s.ok) failed.structure = s.failures[0];
  const r = d.adapter.lint(parsed);
  if (!r.ok) failed.rules = r.failures[0];
  const b = parsed.roots[0] ? gradeBuilt(builtTree(parsed.roots[0], d.patterns, d.adapter), expect) : { ok: false, failures: ['no top pattern'] };
  if (!b.ok) failed.built = b.failures[0];
  return failed;
}

export async function runGraderTests(d, cases, settings = {}) {
  const ref = path.join(d.dir, 'reference.mjs');
  const mut = path.join(d.dir, 'mutations.mjs');
  if (!fs.existsSync(ref)) return { skipped: 'no reference.mjs in the pack' };
  const { referenceSvg } = await import(pathToFileURL(ref).href);
  let ops = fs.existsSync(mut) ? (await import(pathToFileURL(mut).href)).operators : [];
  if (Array.isArray(settings.mutations)) ops = ops.filter((o) => settings.mutations.includes(o.id));

  const refs = { n: 0, pass: 0, failures: [] };
  const byOp = Object.fromEntries(ops.map((o) => [o.id, { op: o, applied: 0, caught: 0, right: 0, missed: [], wrong: [] }]));
  for (const c of cases) {
    if (c.expect.outcome !== 'match') continue;
    const svg = referenceSvg(c.expect.tree);
    if (!svg) continue;
    refs.n++;
    const f = gradeText(svg, c.expect, d);
    if (Object.keys(f).length) refs.failures.push(`${c.id}: ${Object.entries(f)[0].join(': ')}`); else refs.pass++;
    for (const o of ops) {
      const broken = o.apply({ svg, tree: c.expect.tree, adapter: d.adapter });
      if (broken == null || broken === svg) continue;
      const s = byOp[o.id];
      s.applied++;
      const failed = gradeText(broken, c.expect, d);
      const names = Object.keys(failed);
      if (!names.length) { s.missed.push(c.id); continue; }
      s.caught++;
      if (o.expect.some((g) => names.includes(g))) s.right++;
      else s.wrong.push(`${c.id} (failed in ${names.join(', ')})`);
    }
  }
  const total = Object.values(byOp).reduce((a, s) => ({ applied: a.applied + s.applied, caught: a.caught + s.caught, right: a.right + s.right }), { applied: 0, caught: 0, right: 0 });
  return { refs, byOp, total };
}
