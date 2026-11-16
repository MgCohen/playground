// The check that the agent runs in its sandbox: structure + rules, no expected answer.
// The sandbox has a small verify.mjs that calls main(domain, sandboxDir).
import fs from 'node:fs';
import path from 'node:path';
import { loadDomain } from '../lib/domain.mjs';
import { gradeStructure } from './graders/structure.mjs';

export async function check(domain, dir) {
  let d;
  try { d = await loadDomain(domain, { patternsDir: path.join(dir, 'patterns') }); }
  catch (e) { return { ok: false, failures: [`cannot read the pattern files: ${e.message}`] }; }
  const outFile = path.join(dir, d.config.output);
  if (!fs.existsSync(outFile)) return { ok: false, failures: [`no ${d.config.output}`] };
  let trace = null;
  try { trace = JSON.parse(fs.readFileSync(path.join(dir, 'trace.json'), 'utf8')); } catch { /* no trace yet */ }
  const parsed = d.adapter.parse(fs.readFileSync(outFile, 'utf8'));
  if (trace && trace.proposed != null && typeof trace.proposed !== 'string') return { ok: false, failures: ['trace.json: "proposed" must be a path such as "patterns/<id>.md", or null'] };
  const proposedId = typeof trace?.proposed === 'string' ? path.basename(trace.proposed, '.md') : parsed.roots[0]?.p;
  const known = d.patterns.get(proposedId);
  const s = gradeStructure(parsed, d.patterns, d.adapter, { proposedId: known && known.status !== 'proposed' ? undefined : proposedId });
  const r = d.adapter.lint(parsed);
  return { ok: s.ok && r.ok, failures: [...s.failures, ...r.failures] };
}

export async function main(domain, dir) {
  const res = await check(domain, dir);
  if (res.ok) console.log('PASS: structure and rules checks pass.');
  else { console.log(`FAIL: ${res.failures.length} problem(s):`); res.failures.forEach(f => console.log(' - ' + f)); }
  process.exitCode = res.ok ? 0 : 1;
}
