// Grade the saved outputs of a result file again with the current patterns and graders. No agent runs.
// Usage: node core/eval/regrade.mjs results/<run>.json
import fs from 'node:fs';
import path from 'node:path';
import { ENGINE_ROOT } from '../lib/patterns.mjs';
import { loadDomain } from '../lib/domain.mjs';
import { gradeText } from './mutate.mjs';

const r = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const d = await loadDomain(r.domain);
let before = 0, now = 0;
const changed = [];
for (const x of r.runs) {
  if (x.expect.outcome !== 'match') continue;
  const f = path.join(ENGINE_ROOT, 'results', 'runs', r.runId, `${x.case}-${x.run}`, d.config.output);
  if (!fs.existsSync(f)) continue;
  const failed = gradeText(fs.readFileSync(f, 'utf8'), x.expect, d);
  const okNow = !Object.keys(failed).length;
  const okBefore = ['structure', 'rules', 'built'].every((k) => x.res[k]?.ok);
  before += okBefore; now += okNow;
  if (okBefore !== okNow) changed.push(`${x.case}#${x.run}: ${okBefore ? 'pass' : 'fail'} -> ${okNow ? 'pass' : 'fail'}${okNow ? '' : ' (' + Object.entries(failed)[0].join(': ') + ')'}`);
}
console.log(`${r.runId}: output checks pass ${before} -> ${now}`);
changed.forEach((c) => console.log('  ' + c));
