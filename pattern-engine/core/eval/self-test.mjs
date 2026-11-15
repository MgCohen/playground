// Test the graders on fixed fixtures. A grader that passes bad output or fails good output is broken.
// Usage: node core/eval/self-test.mjs <domain>
import fs from 'node:fs';
import path from 'node:path';
import YAML from 'yaml';
import { pathToFileURL } from 'node:url';
import { loadDomain, assertValid } from '../lib/domain.mjs';
import { gradeStructure, builtTree } from './graders/structure.mjs';
import { gradeSelection, gradeBuilt } from './graders/selection.mjs';

const domain = process.argv[2] || 'shapes';
const d = await loadDomain(domain);
assertValid(d.patterns);
const fx = path.join(d.dir, 'fixtures');
const expect = YAML.parse(fs.readFileSync(path.join(fx, 'expect.yaml'), 'utf8'));
let bad = 0;
const line = (ok, msg) => { if (!ok) bad++; console.log(`${ok ? 'ok  ' : 'FAIL'}  ${msg}`); };

for (const e of expect) {
  const parsed = d.adapter.parse(fs.readFileSync(path.join(fx, e.file), 'utf8'));
  const s = gradeStructure(parsed, d.patterns, d.adapter);
  const r = d.adapter.lint(parsed);
  line(s.ok === e.structure, `${e.file}: structure ${s.ok} (want ${e.structure})${s.ok ? '' : ' — ' + s.failures[0]}`);
  line(r.ok === e.rules, `${e.file}: rules ${r.ok} (want ${e.rules})${r.ok ? '' : ' — ' + r.failures[0]}`);
  if (e.tree) {
    const b = gradeBuilt(builtTree(parsed.roots[0], d.patterns, d.adapter), { outcome: 'match', tree: e.tree });
    line(b.ok, `${e.file}: built tree matches${b.ok ? '' : ' — ' + b.failures[0]}`);
  }
}

// Known good and known bad, generated: references must pass, mutations must fail for the right reason.
// Case sets, operators and target rates come from eval.config.yaml (grader_tests).
{
  const { loadCases } = await import('./run.mjs');
  const { runGraderTests } = await import('./mutate.mjs');
  const { loadConfig } = await import('../lib/config.mjs');
  const gt = loadConfig(domain).grader_tests || {};
  const sets = gt.case_sets || [{}];
  const seen = new Set();
  const all = sets.flatMap((cs) => loadCases(domain, { gen: cs.gen, seed: cs.seed, maxChildren: cs.max_children })).filter((c) => !seen.has(c.id) && seen.add(c.id));
  const res = await runGraderTests(d, all, gt);
  if (res.skipped) console.log(`skip  grader tests: ${res.skipped}`);
  else {
    const rate = (a, b) => (b ? a / b : 1);
    const pct = (a, b) => `${Math.round(100 * rate(a, b))}% (${a}/${b})`;
    const want = (k) => gt[k] ?? 1;
    res.refs.failures.slice(0, 5).forEach((x) => console.log(`        reference fails: ${x}`));
    line(rate(res.refs.pass, res.refs.n) >= want('reference_pass_rate'), `known good: ${pct(res.refs.pass, res.refs.n)} of reference drawings pass (target ${100 * want('reference_pass_rate')}%)`);
    console.log('\n        mutation            applied  caught  right reason');
    for (const x of Object.values(res.byOp)) {
      console.log(`        ${x.op.id.padEnd(19)} ${String(x.applied).padStart(7)}  ${String(x.caught).padStart(6)}  ${String(x.right).padStart(12)}${x.missed.length ? '   MISSED e.g. ' + x.missed[0] : ''}${x.wrong.length ? '   WRONG REASON e.g. ' + x.wrong[0] : ''}`);
    }
    const t = res.total;
    line(rate(t.caught, t.applied) >= want('mutation_catch_rate'), `known bad: ${pct(t.caught, t.applied)} of mutations caught (target ${100 * want('mutation_catch_rate')}%)`);
    line(rate(t.right, t.applied) >= want('right_reason_rate'), `known bad: ${pct(t.right, t.applied)} caught by the grader that the mutation names (target ${100 * want('right_reason_rate')}%)`);
  }
}

// Selection grader: core cases, no domain words needed.
const tree = { p: 'a', v: 'x', parts: [{ p: 'b' }, { p: 'c', params: { k: 'red' } }] };
const sel = [
  ['same tree', { outcome: 'match', tree }, { outcome: 'match', tree }, true],
  ['wrong child', { outcome: 'match', tree: { ...tree, parts: [{ p: 'b' }, { p: 'b' }] } }, { outcome: 'match', tree }, false],
  ['wrong param', { outcome: 'match', tree: { ...tree, parts: [{ p: 'b' }, { p: 'c', params: { k: 'blue' } }] } }, { outcome: 'match', tree }, false],
  ['forced pattern on no-fit', { outcome: 'match', tree }, { outcome: 'no-fit', nearest: 'a' }, false],
  ['good no-fit', { outcome: 'no-fit', nearest: 'a', deltas: ['add x'] }, { outcome: 'no-fit', nearest: ['a', 'b'] }, true],
  ['no-fit without deltas', { outcome: 'no-fit', nearest: 'a', deltas: [] }, { outcome: 'no-fit', nearest: 'a' }, false],
];
for (const [name, trace, exp, want] of sel) line(gradeSelection(trace, exp).ok === want, `selection: ${name} -> ${want}`);

console.log(bad ? `\n${bad} grader check(s) failed.` : '\nAll grader checks passed.');
process.exit(bad ? 1 : 0);
