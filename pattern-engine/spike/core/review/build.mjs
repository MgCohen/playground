// Build a human review page from one or more eval result files.
// The page shows, for each run: the request, the output, the expected answer,
// what the agent reported, and the engine verdict. A person approves or rejects each run.
// The domain renders the output: domains/<name>/review.mjs (renderOutput, renderExpected, renderTrace).
// Usage: node core/review/build.mjs results/<a>.json [results/<b>.json ...] [--out results/review/<name>.html] [--title "..."]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { ENGINE_ROOT, domainDir } from '../lib/patterns.mjs';
import { treeText } from '../lib/tree.mjs';

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

/** Fallback handlers for a domain with no review.mjs: text blocks. */
const fallback = {
  renderOutput: (runDir) => {
    const f = fs.readdirSync(runDir).find((x) => x.startsWith('output.'));
    return { html: f ? `<pre>${esc(fs.readFileSync(path.join(runDir, f), 'utf8'))}</pre>` : '<p class="none">No output file.</p>' };
  },
  renderExpected: (expect) => ({ html: `<pre>${esc(expect.outcome === 'no-fit' ? 'no-fit, nearest: ' + [].concat(expect.nearest).join(' or ') : treeText(expect.tree))}</pre>` }),
  renderTrace: (trace) => ({ html: `<pre>${esc(JSON.stringify(trace, null, 2))}</pre>` }),
};

async function handlersFor(domain) {
  const f = path.join(domainDir(domain), 'review.mjs');
  if (!fs.existsSync(f)) return fallback;
  return { ...fallback, ...(await import(pathToFileURL(f).href)) };
}

export async function buildReview(resultFiles, { title } = {}) {
  const items = [];
  const runsMeta = [];
  for (const file of resultFiles) {
    const r = JSON.parse(fs.readFileSync(file, 'utf8'));
    const h = await handlersFor(r.domain);
    runsMeta.push({ runId: r.runId, harness: (r.harness || 'monolith') + (r.gate ? ' + gate' : ''), model: r.model, k: r.k, mode: r.mode, runs: r.runs.length });
    for (const x of r.runs) {
      const runDir = path.join(ENGINE_ROOT, 'results', 'runs', r.runId, `${x.case}-${x.run}`);
      const graders = Object.entries(x.res).filter(([, g]) => g).map(([name, g]) => ({ name, ok: g.ok, why: g.ok ? '' : (g.failures[0] || '') }));
      items.push({
        key: `${r.runId}__${x.case}-${x.run}`.replace(/[^A-Za-z0-9_-]/g, '_'),
        runId: r.runId, harness: (r.harness || 'monolith') + (r.gate ? ' + gate' : ''), model: r.model,
        case: x.case, run: x.run, tags: x.tags, request: x.request,
        output: fs.existsSync(runDir) ? h.renderOutput(runDir, x).html : '<p class="none">The run folder is missing.</p>',
        expected: h.renderExpected(x.expect).html,
        expectedVisual: (h.renderExpectedVisual || h.renderExpected)(x.expect).html,
        trace: h.renderTrace(x.trace).html,
        engine: { pass: !!x.pass, graders, traceMatches: !!x.consistent },
        agent: { secs: Math.round((x.agent?.ms || 0) / 1000), turns: x.agent?.turns ?? null, subagents: x.agent?.subagents ?? 0 },
      });
    }
  }
  // Interleave by case so the arms of one case sit next to each other.
  items.sort((a, b) => a.case.localeCompare(b.case) || a.run - b.run || a.harness.localeCompare(b.harness));
  const tpl = fs.readFileSync(path.join(ENGINE_ROOT, 'core', 'review', 'template.html'), 'utf8');
  const data = { title: title || 'Eval review', built: new Date().toISOString(), runs: runsMeta, items };
  const json = JSON.stringify(data).replace(/</g, '\\u003c');
  return tpl.replace('/*__DATA__*/null', json).replace('{{TITLE}}', esc(title || 'Eval Review'));
}

if (process.argv[1]?.endsWith('build.mjs')) {
  const args = process.argv.slice(2);
  const files = [];
  let out = null, title = null;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--out') out = args[++i];
    else if (args[i] === '--title') title = args[++i];
    else files.push(args[i]);
  }
  if (!files.length) { console.error('usage: node core/review/build.mjs results/<run>.json [...] [--out file] [--title text]'); process.exit(2); }
  const html = await buildReview(files, { title });
  out ||= path.join(ENGINE_ROOT, 'results', 'review', `review-${path.basename(files[0], '.json')}.html`);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, html);
  console.log(`Wrote ${out} (${Math.round(html.length / 1024)} KB)`);
}
