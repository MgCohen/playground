// Run the eval: cases × k agent runs, each in a clean sandbox, then grade and report.
// Usage: node core/eval/run.mjs <domain> [--harness monolith|modular] [--gate] [--mode smoke|dev|full] [--sample n] [--k n] [--model haiku|sonnet|opus]
//        [--only gen|hand|<tag>|<case id>,...] [--limit n] [--concurrency 4] [--ablate a,b] [--label text] [--dry]
// Case set: [--gen 15] [--seed 7] [--max-children 4]. Another seed gives a held-out set (tag "heldout").
// A mode sets sample, k and model. A flag that you give changes only that value.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import YAML from 'yaml';
import { ENGINE_ROOT, domainDir } from '../lib/patterns.mjs';
import { loadDomain, assertValid } from '../lib/domain.mjs';
import { generate } from '../graph/generate-cases.mjs';
import { prepareSandbox, runAgent } from './agent.mjs';
import { check } from './verify.mjs';
import { diffTrees } from '../lib/tree.mjs';
import { gradeStructure, builtTree } from './graders/structure.mjs';
import { gradeSelection, gradeBuilt } from './graders/selection.mjs';
import { report, checkTargets } from './report.mjs';
import { loadConfig, targetsFor } from '../lib/config.mjs';

// Modes (smoke, dev, full) and targets come from eval.config.yaml.

function args(argv) {
  const o = { mode: 'dev', concurrency: 4, ablate: [], only: [], harness: null, gate: false };
  const set = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i], v = argv[i + 1];
    if (a === '--mode') { o.mode = v; i++; }
    else if (a === '--sample') { set.sample = +v; i++; }
    else if (a === '--dry') { o.dry = true; }
    else if (a === '--harness') { o.harness = v; i++; }
    else if (a === '--gate') { o.gate = true; }
    else if (a === '--gen') { o.gen = +v; i++; }
    else if (a === '--seed') { o.seed = +v; i++; }
    else if (a === '--max-children') { o.maxChildren = +v; i++; }
    else if (a === '--k') { set.k = +v; i++; }
    else if (a === '--limit') { o.limit = +v; i++; }
    else if (a === '--concurrency') { o.concurrency = +v; i++; }
    else if (a === '--ablate') { o.ablate = v.split(','); i++; }
    else if (a === '--only') { o.only = v.split(','); i++; }
    else if (a === '--model') { set.model = v; i++; }
    else if (a === '--label') { o.label = v; i++; }
    else if (!a.startsWith('--')) o.domain = a;
  }
  o.config = loadConfig(o.domain || 'shapes');
  const MODES = o.config.modes || {};
  o.harness ||= o.config.harness || 'modular';
  if (!MODES[o.mode]) throw new Error(`--mode must be one of ${Object.keys(MODES).join(', ')}`);
  return { ...o, ...MODES[o.mode], ...set };
}

/** Bucket of a case, for the sample: the rarest kind first. */
const BUCKETS = ['no-fit', 'near-miss', 'deep', 'group', 'object'];
const bucketOf = (c) => BUCKETS.find(b => c.tags.includes(b)) || 'other';

/** Take n cases, spread over the buckets (round robin), in a fixed order. */
export function sample(cases, n) {
  if (!n || n >= cases.length) return cases;
  const groups = new Map();
  for (const c of cases) { const b = bucketOf(c); if (!groups.has(b)) groups.set(b, []); groups.get(b).push(c); }
  const order = [...BUCKETS, 'other'].filter(b => groups.has(b));
  const out = [];
  while (out.length < n) for (const b of order) { const c = groups.get(b).shift(); if (c && out.length < n) out.push(c); }
  return out;
}

export function loadCases(domain, { gen = 15, seed = 7, maxChildren = 4 } = {}) {
  const dir = domainDir(domain);
  const cases = generate(domain, gen, seed, maxChildren);
  const hand = path.join(dir, 'cases');
  for (const f of fs.existsSync(hand) ? fs.readdirSync(hand).filter(f => /\.ya?ml$/.test(f)) : []) {
    for (const c of YAML.parse(fs.readFileSync(path.join(hand, f), 'utf8'))) cases.push({ source: 'hand', ...c, tags: ['hand', ...(c.tags || [])] });
  }
  return cases;
}

async function gradeRun(domainName, sandbox, expect) {
  const d = await loadDomain(domainName, { patternsDir: path.join(sandbox, 'patterns') }).catch(e => ({ error: e.message }));
  const res = { selection: null, built: null, structure: null, rules: null, proposed: null };
  let trace = null;
  try { trace = JSON.parse(fs.readFileSync(path.join(sandbox, 'trace.json'), 'utf8')); } catch { /* missing */ }
  res.selection = gradeSelection(trace, expect);
  if (d.error) {
    res.structure = res.rules = res.built = { ok: false, failures: [d.error] };
    return { trace, res };
  }
  const outFile = path.join(sandbox, d.config.output);
  if (!fs.existsSync(outFile)) {
    res.structure = res.rules = res.built = { ok: false, failures: ['no output file'] };
    return { trace, res };
  }
  const parsed = d.adapter.parse(fs.readFileSync(outFile, 'utf8'));
  const proposedId = typeof trace?.proposed === 'string' ? path.basename(trace.proposed, '.md') : undefined;
  res.structure = gradeStructure(parsed, d.patterns, d.adapter, { proposedId });
  res.rules = d.adapter.lint(parsed);
  const built = parsed.roots[0] ? builtTree(parsed.roots[0], d.patterns, d.adapter) : null;
  res.built = built ? gradeBuilt(built, expect) : { ok: false, failures: ['no top pattern'] };
  // Not a pass condition: does the trace say what the output has? (patterns and variants only)
  const strip = (t) => t && ({ p: t.p, v: t.v, parts: (t.parts || []).map(strip) });
  const consistent = !!(trace?.tree && built) && diffTrees(strip(built), strip(trace.tree)).length === 0;
  if (expect.outcome === 'no-fit') {
    const p = proposedId && d.patterns.get(proposedId);
    const failures = [];
    if (!p) failures.push('no proposed pattern file');
    else {
      if (p.status !== 'proposed') failures.push(`status is "${p.status}", not "proposed"`);
      if (!(p.related || []).includes(trace.nearest)) failures.push('related does not name the nearest pattern');
    }
    res.proposed = { ok: failures.length === 0, failures };
  }
  return { trace, res, consistent };
}

async function pool(items, n, fn) {
  let i = 0;
  const workers = Array.from({ length: n }, async () => { while (i < items.length) { const j = i++; await fn(items[j], j); } });
  await Promise.all(workers);
}

if (process.argv[1]?.endsWith('run.mjs')) {
  const o = args(process.argv.slice(2));
  const domain = o.domain || 'shapes';
  const dom = await loadDomain(domain);
  assertValid(dom.patterns);
  let cases = loadCases(domain, { gen: o.gen ?? 15, seed: o.seed ?? 7, maxChildren: o.maxChildren ?? 4 });
  if (o.only.length) cases = cases.filter(c => o.only.some(s => c.id === s || c.tags.includes(s) || (s === 'gen' && c.source === 'generator')));
  cases = sample(cases, o.sample);
  if (o.limit) cases = cases.slice(0, o.limit);
  if (o.dry) {
    console.log(`Mode ${o.mode}: harness ${o.harness}${o.gate ? ' + gate' : ''}, model ${o.model}, k=${o.k}, ${cases.length} cases, ${cases.length * o.k} agent runs`);
    for (const c of cases) console.log(`  ${c.id.padEnd(16)} ${bucketOf(c).padEnd(10)} ${c.request}`);
    process.exit(0);
  }

  const runId = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19) + `-${o.harness}${o.gate ? '-gate' : ''}` + (o.ablate.length ? '-' + o.ablate.join('+') : '');
  const tmp = path.join(fs.realpathSync.native(os.tmpdir()), 'pattern-engine-runs', runId);
  const keep = path.join(ENGINE_ROOT, 'results', 'runs', runId);
  const jobs = cases.flatMap(c => Array.from({ length: o.k }, (_, i) => ({ c, i })));
  console.log(`Run ${runId} [${o.mode}]: harness ${o.harness}${o.gate ? ' + gate' : ''}, model ${o.model}, ${cases.length} cases × k=${o.k} = ${jobs.length} agent runs, concurrency ${o.concurrency}${o.ablate.length ? ', ablate ' + o.ablate.join(',') : ''}`);

  const runs = [];
  let done = 0;
  await pool(jobs, o.concurrency, async ({ c, i }) => {
    const sandbox = path.join(tmp, `${c.id}-${i + 1}`);
    const harness = prepareSandbox(domain, sandbox, { ablate: o.ablate, harness: o.harness });
    const opts = { model: o.model, output: dom.config.output, harness };
    const agent = await runAgent(sandbox, c.request, opts);
    // Gate (outside the agent): the system runs the same check. On a failure, one more agent run with the failures.
    let gateRetries = 0;
    if (o.gate && !agent.error) {
      const g = await check(domain, sandbox);
      if (!g.ok) {
        gateRetries = 1;
        const again = await runAgent(sandbox, c.request, { ...opts, extra: 'The system check failed after your last run. Fix these problems in the output, then update trace.json:\n- ' + g.failures.join('\n- ') });
        agent.ms += again.ms; agent.cost = (agent.cost || 0) + (again.cost || 0); agent.turns = (agent.turns || 0) + (again.turns || 0);
        agent.subagents += again.subagents; agent.error = again.error;
      }
    }
    agent.gateRetries = gateRetries;
    // A bad agent output gives a failed run, never a crashed eval.
    const { trace, res, consistent } = await gradeRun(domain, sandbox, c.expect).catch(e => ({
      trace: null, consistent: false, res: { grader: { ok: false, failures: [`grader error: ${e.message}`] } },
    }));
    const checks = Object.values(res).filter(Boolean);
    const pass = checks.every(r => r.ok) && !agent.error;
    // Keep the outputs next to the results.
    const dest = path.join(keep, `${c.id}-${i + 1}`);
    fs.mkdirSync(dest, { recursive: true });
    for (const f of fs.readdirSync(sandbox)) if (/^(output\.|trace\.json|agent\.log\.json)/.test(f)) fs.copyFileSync(path.join(sandbox, f), path.join(dest, f));
    if (fs.existsSync(path.join(sandbox, 'plan.json'))) fs.copyFileSync(path.join(sandbox, 'plan.json'), path.join(dest, 'plan.json'));
    if (fs.existsSync(path.join(sandbox, 'steps'))) fs.cpSync(path.join(sandbox, 'steps'), path.join(dest, 'steps'), { recursive: true });
    if (typeof trace?.proposed === 'string' && fs.existsSync(path.join(sandbox, trace.proposed))) fs.copyFileSync(path.join(sandbox, trace.proposed), path.join(dest, path.basename(trace.proposed)));
    runs.push({ case: c.id, run: i + 1, tags: c.tags, request: c.request, expect: c.expect, trace, res, pass, agent, consistent });
    done++;
    const fails = Object.entries(res).filter(([, r]) => r && !r.ok).map(([k, r]) => `${k}: ${r.failures[0]}`);
    console.log(`[${done}/${jobs.length}] ${pass ? 'PASS' : 'FAIL'} ${c.id}#${i + 1} ${Math.round(agent.ms / 1000)}s${agent.error ? ' agent error: ' + agent.error.slice(0, 120) : ''}${fails.length ? '\n      ' + fails.join('\n      ') : ''}`);
  });

  const result = { runId, domain, harness: o.harness, gate: o.gate, mode: o.mode, label: o.label || null, k: o.k, ablate: o.ablate, model: o.model, cases: cases.length, runs };
  fs.mkdirSync(path.join(ENGINE_ROOT, 'results'), { recursive: true });
  const file = path.join(ENGINE_ROOT, 'results', `${runId}.json`);
  fs.writeFileSync(file, JSON.stringify(result, null, 2) + '\n');
  console.log('\n' + report(result));
  console.log(`\nResults: ${path.relative(ENGINE_ROOT, file)}\nOutputs: ${path.relative(ENGINE_ROOT, keep)}`);
  const t = checkTargets(result, targetsFor(o.config, o.mode));
  console.log('\n' + t.text);
  process.exitCode = t.ok ? 0 : 1;
}
