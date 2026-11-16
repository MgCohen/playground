// Write the parity goldens for the C# engine: what the spike gives for the shapes pack.
// Usage: node tools/export-parity.mjs <out dir> [runs dir]
// The C# tests compare their results with these files. No model runs.
import fs from 'node:fs';
import path from 'node:path';
import { domainDir, loadPatterns, validate, toGraph, toIndexMarkdown } from '../core/lib/patterns.mjs';
import { loadDomain } from '../core/lib/domain.mjs';
import { generate } from '../core/graph/generate-cases.mjs';
import { loadCases } from '../core/eval/run.mjs';
import { runGraderTests, gradeText } from '../core/eval/mutate.mjs';
import { referenceSvg } from '../domains/shapes/reference.mjs';

const out = path.resolve(process.argv[2]);
const runsDir = process.argv[3];
fs.mkdirSync(out, { recursive: true });
const write = (name, data) => fs.writeFileSync(path.join(out, name), typeof data === 'string' ? data : JSON.stringify(data, null, 2) + '\n');

// Snapshot of the pack data that these goldens come from.
const snap = path.join(out, 'shapes');
for (const sub of ['patterns', 'cases', 'fixtures']) fs.cpSync(path.join(domainDir('shapes'), sub), path.join(snap, sub), { recursive: true });

const patterns = loadPatterns(path.join(domainDir('shapes'), 'patterns'));
write('validate.json', validate(patterns));
write('graph.json', toGraph(patterns));
write('index.md', toIndexMarkdown(patterns, 'shapes'));
write('cases-seed7.json', generate('shapes', 15, 7, 4));
write('cases-seed42.json', generate('shapes', 60, 42, 6));

const d = await loadDomain('shapes');
const seen = new Set();
const all = [{ gen: 15, seed: 7, maxChildren: 4 }, { gen: 60, seed: 42, maxChildren: 6 }]
  .flatMap((cs) => loadCases('shapes', cs)).filter((c) => !seen.has(c.id) && seen.add(c.id));
const res = await runGraderTests(d, all, {});
write('trust.json', {
  references: { n: res.refs.n, pass: res.refs.pass },
  operators: Object.fromEntries(Object.values(res.byOp).map((s) => [s.op.id, { applied: s.applied, caught: s.caught, right: s.right }])),
  total: res.total,
});
write('references.json', Object.fromEntries(all.filter((c) => c.expect.outcome === 'match').map((c) => [c.id, referenceSvg(c.expect.tree)])));

// Regrade parity: a sample of stored agent outputs and the verdict of the spike graders on each.
if (runsDir && fs.existsSync(runsDir)) {
  const outputs = path.join(out, 'outputs');
  fs.mkdirSync(outputs, { recursive: true });
  const verdicts = [];
  for (const file of fs.readdirSync(path.join(runsDir, '..')).filter((f) => f.endsWith('.json')).sort()) {
    const r = JSON.parse(fs.readFileSync(path.join(runsDir, '..', file), 'utf8'));
    for (const x of r.runs || []) {
      if (x.expect.outcome !== 'match') continue;
      const f = path.join(runsDir, r.runId, `${x.case}-${x.run}`, 'output.svg');
      if (!fs.existsSync(f)) continue;
      const name = `${r.runId}__${x.case}-${x.run}.svg`;
      fs.copyFileSync(f, path.join(outputs, name));
      verdicts.push({ file: name, expect: x.expect, failed: gradeText(fs.readFileSync(f, 'utf8'), x.expect, d) });
    }
  }
  write('regrade.json', verdicts);
}
console.log(`Wrote the parity goldens to ${out}`);
