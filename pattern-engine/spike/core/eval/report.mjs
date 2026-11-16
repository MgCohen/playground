// Turn a result file into metrics and a text table.
// Usage: node core/eval/report.mjs results/<run>.json [results/<baseline>.json]
import fs from 'node:fs';

const pct = (a, b) => (b ? `${Math.round((100 * a) / b)}%` : '—');

export function metrics(result) {
  const runs = result.runs;
  const by = (f) => runs.filter(f);
  const okOf = (key) => (r) => r.res[key]?.ok;
  const match = by(r => r.expect.outcome === 'match');
  const nofit = by(r => r.expect.outcome === 'no-fit');
  const near = by(r => r.tags.includes('near-miss'));
  const cases = [...new Set(runs.map(r => r.case))];
  const allPass = cases.filter(id => runs.filter(r => r.case === id).every(r => r.pass));
  return {
    runs: runs.length,
    'selection accuracy': [by(okOf('selection')).length, runs.length],
    'built tree matches': [match.filter(okOf('built')).length, match.length],
    'structure pass': [by(okOf('structure')).length, runs.length],
    'rules pass': [by(okOf('rules')).length, runs.length],
    'pass@1 (all checks)': [by(r => r.pass).length, runs.length],
    [`pass^${result.k} (all runs of a case)`]: [allPass.length, cases.length],
    'near-miss accuracy': [near.filter(okOf('selection')).length, near.length],
    'no-fit precision': [nofit.filter(r => r.trace?.outcome === 'no-fit').length, nofit.length],
    'no-fit full pass': [nofit.filter(r => r.pass).length, nofit.length],
    'agent errors': [by(r => r.agent?.error).length, runs.length],
    'trace matches output': [by(r => r.consistent).length, runs.length],
    'gate retries': [by(r => r.agent?.gateRetries).length, runs.length],
  };
}

// Stable ids for the targets in eval.config.yaml.
const TARGET_IDS = {
  'selection': (k) => k === 'selection accuracy',
  'pass@1': (k) => k.startsWith('pass@1'),
  'pass^k': (k) => k.startsWith('pass^'),
  'near-miss': (k) => k === 'near-miss accuracy',
  'no-fit': (k) => k === 'no-fit full pass',
  'trace': (k) => k === 'trace matches output',
  'agent-errors': (k) => k === 'agent errors',
};

/** Compare a result with targets. Returns { ok, lines, text }. */
export function checkTargets(result, targets) {
  if (!targets) return { ok: true, lines: [], text: `Targets: none for mode "${result.mode}".` };
  const m = metrics(result);
  const lines = [];
  let ok = true;
  for (const [id, want] of Object.entries(targets)) {
    const test = TARGET_IDS[id];
    if (!test) { lines.push(`  ?    ${id}: unknown target id (use ${Object.keys(TARGET_IDS).join(', ')})`); continue; }
    const key = Object.keys(m).find(test);
    const [n, d] = m[key] || [0, 0];
    if (!d) { lines.push(`  -    ${id}: no runs to measure`); continue; }
    const rate = n / d;
    const max = typeof want === 'object' && want !== null ? want.max : undefined;
    const good = max !== undefined ? rate <= max : rate >= want;
    if (!good) ok = false;
    lines.push(`  ${good ? 'ok  ' : 'LOW '} ${id}: ${(100 * rate).toFixed(0)}% (${n}/${d}), target ${max !== undefined ? '<= ' + 100 * max : '>= ' + 100 * want}%`);
  }
  return { ok, lines, text: `Targets (mode ${result.mode}): ${ok ? 'all met' : 'BELOW TARGET'}\n` + lines.join('\n') };
}

export function report(result, baseline) {
  const m = metrics(result);
  const b = baseline ? metrics(baseline) : null;
  const cost = result.runs.reduce((s, r) => s + (r.agent?.cost || 0), 0);
  const secs = result.runs.reduce((s, r) => s + (r.agent?.ms || 0), 0) / 1000 / (result.runs.length || 1);
  const lines = [
    `Report ${result.runId}${result.ablate?.length ? ' (ablate ' + result.ablate.join(',') + ')' : ''}: ${result.cases} cases, ${m.runs} runs`,
    `harness ${result.harness ?? 'monolith'}${result.gate ? ' + gate' : ''}, mode ${result.mode ?? '-'}, model ${result.model}, k=${result.k}`,
    `API-equivalent cost $${cost.toFixed(2)} ($${(cost / (result.runs.length || 1)).toFixed(3)} per run), ${Math.round(secs)} s per run, ` +
      `${(result.runs.reduce((s, r) => s + (r.agent?.turns || 0), 0) / (result.runs.length || 1)).toFixed(1)} turns per run, ` +
      `${(result.runs.reduce((s, r) => s + (r.agent?.subagents || 0), 0) / (result.runs.length || 1)).toFixed(1)} subagents per run`,
    '',
  ];
  if (result.mode && result.mode !== 'full') lines.push(`Note: a ${result.mode} run is a sample. Use --mode full to validate.`, '');
  lines.push('metric'.padEnd(34) + 'score'.padEnd(14) + (b ? 'baseline' : ''));
  for (const [k, v] of Object.entries(m)) {
    if (!Array.isArray(v)) continue;
    const s = `${pct(v[0], v[1])} (${v[0]}/${v[1]})`;
    const bs = b && Array.isArray(b[k]) ? `${pct(b[k][0], b[k][1])}` : '';
    lines.push(k.padEnd(34) + s.padEnd(14) + bs);
  }
  // Per top pattern
  const tops = {};
  for (const r of result.runs) {
    const top = r.expect.outcome === 'no-fit' ? 'no-fit' : r.expect.tree.p;
    (tops[top] ||= []).push(r.pass);
  }
  lines.push('', 'by top pattern'.padEnd(34) + 'pass@1');
  for (const [k, v] of Object.entries(tops)) lines.push(`  ${k}`.padEnd(34) + `${pct(v.filter(Boolean).length, v.length)} (${v.filter(Boolean).length}/${v.length})`);
  // Most common failures
  const fails = {};
  for (const r of result.runs) for (const [k, g] of Object.entries(r.res)) if (g && !g.ok) for (const f of g.failures.slice(0, 1)) {
    const key = `${k}: ${f.replace(/\d+/g, '#').replace(/\[#\]/g, '')}`;
    fails[key] = (fails[key] || 0) + 1;
  }
  const top = Object.entries(fails).sort((a, b) => b[1] - a[1]).slice(0, 8);
  if (top.length) { lines.push('', 'most common failures'); for (const [k, n] of top) lines.push(`  ${String(n).padStart(3)}× ${k}`); }
  return lines.join('\n');
}

if (process.argv[1]?.endsWith('report.mjs')) {
  const r = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const b = process.argv[3] ? JSON.parse(fs.readFileSync(process.argv[3], 'utf8')) : null;
  console.log(report(r, b));
}
