// The solver: code drives the loop, the model answers one small question per call.
//
//   queue of nodes ─► catalog query (code) ─► decide (model) ─► solved | partial | none | unresolved
//        ▲                                                         │
//        └──── code makes child nodes from the pattern parts ◄─────┘
//   then compose bottom-up: each node is drawn (model) and checked (code), with retries.
//   The graph state is the trace.
//
// The domain pack gives the layout and assembly hooks (domains/<name>/solve.mjs).
// The harness gives one agent file for each judgment (harnesses/solver/.claude/agents/*.md): model, tools,
// output schema and system prompt. The engine builds each call from that file (judge.mjs).
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadDomain } from '../lib/domain.mjs';
import { partsFor, parseCount } from '../lib/patterns.mjs';
import { gradeStructure } from '../eval/graders/structure.mjs';
import { scaffold } from '../eval/pe-cli.mjs';
import { claudeJudge } from './judge.mjs';

const read = (f) => (fs.existsSync(f) ? fs.readFileSync(f, 'utf8') : '');

/** The candidate rows that the model sees for one node. Code chooses them; the model never reads files. */
function candidates(ids, patterns) {
  return ids.map((id) => patterns.get(id)).filter(Boolean).map((p) => ({
    id: p.id,
    kind: p.kind,
    use_when: p.use_when,
    do_not_use_when: p.do_not_use_when || [],
    variants: (p.variants || []).map((v) => ({ id: v.id, when: v.when })),
    params: Object.fromEntries(Object.entries(p.params || {}).map(([k, d]) => [k, { values: d.values, default: d.default }])),
    child_patterns: (p.parts || []).filter((pt) => patterns.get(pt.ref)?.kind !== 'leaf').map((pt) => pt.ref),
  }));
}

export async function solve(domainName, sandbox, request, opts = {}) {
  const t0 = Date.now();
  const settings = { max_depth: 4, compose_retries: 3, concurrency: 4, ...(opts.settings || {}) };
  const judge = opts.judge || claudeJudge({ dir: sandbox, model: opts.model });
  const allowNoFit = !(opts.ablate || []).includes('no-nofit');
  const d = await loadDomain(domainName, { patternsDir: path.join(sandbox, 'patterns') });
  const pack = await import(pathToFileURL(path.join(d.dir, 'solve.mjs')).href);
  const rules = read(path.join(sandbox, 'instructions.md'));
  const stats = { calls: 0, cost: 0, log: [] };
  const ask = async (agent, prompt) => {
    const r = await judge.call({ agent, prompt });
    stats.calls++; stats.cost += r.cost || 0;
    stats.log.push({ agent, ms: r.ms, cost: r.cost, error: r.error, data: r.data });
    return r;
  };

  // ---------- 1. Decide: a work queue of nodes ----------
  const topIds = [...d.patterns.values()].filter((p) => p.kind !== 'leaf' && p.status !== 'proposed').sort((a, b) => a.level - b.level).map((p) => p.id);
  const nodes = [];
  const mk = (text, allowed, depth, parent) => { const n = { id: nodes.length, text, allowed, depth, parent, status: 'open', children: [] }; nodes.push(n); return n; };
  const root = mk(request, topIds, 0, null);
  let queue = [root];
  while (queue.length) {
    const level = queue; queue = [];
    await Promise.all(level.map(async (n) => {
      if (n.depth > settings.max_depth) { n.status = 'unresolved'; n.reason = 'too deep'; return; }
      const cands = candidates(n.allowed, d.patterns);
      const r = await ask('decider', `Request: ${n.text}\n\nCandidates:\n${JSON.stringify(cands, null, 1)}`);
      const a = r.data;
      if (!a) { n.status = 'unresolved'; n.reason = `no answer: ${r.error}`; return; }
      n.reason = a.reason;
      if (a.status === 'none' && !allowNoFit) { n.status = 'unresolved'; n.reason = 'no-fit is switched off (ablation)'; return; }
      if (a.status === 'none') {
        n.status = 'none';
        n.nearest = n.allowed.includes(a.nearest) ? a.nearest : n.allowed.includes(a.pattern) ? a.pattern : n.allowed[0];
        n.deltas = a.deltas || [];
        n.newId = (a.new_id || 'new-pattern').toLowerCase().replace(/[^a-z0-9-]/g, '-').replace(/^-+|-+$/g, '') || 'new-pattern';
        return;
      }
      const p = d.patterns.get(a.pattern);
      if (!p || !n.allowed.includes(p.id)) { n.status = 'unresolved'; n.reason = `pattern "${a.pattern}" is not allowed here`; return; }
      n.status = a.status; // solved | partial
      n.pattern = p.id;
      const vids = (p.variants || []).map((v) => v.id);
      n.variant = vids.includes(a.variant) ? a.variant : vids[0];
      n.params = {};
      for (const [k, v] of Object.entries(a.params || {})) {
        const def = p.params?.[k];
        if (def && (!def.values || def.values.includes(String(v).toLowerCase()))) n.params[k] = String(v).toLowerCase();
      }
      // Children: code checks them against the pattern parts.
      const childIds = (p.parts || []).filter((pt) => d.patterns.get(pt.ref)?.kind !== 'leaf').map((pt) => pt.ref);
      if (childIds.length) {
        for (const text of a.children || []) {
          const c = mk(text, childIds, n.depth + 1, n.id);
          n.children.push(c.id);
          queue.push(c);
        }
      }
      if (a.status === 'partial' && a.leftover) {
        const c = mk(a.leftover, topIds, n.depth + 1, n.id);
        c.leftover = true;
        n.children.push(c.id);
        queue.push(c);
      }
    }));
  }

  // ---------- 2. Compose bottom-up, check each node ----------
  let attempts = 0;
  const patternText = (id) => read(path.join(sandbox, 'patterns', `${id}.md`));
  async function composeLeafObject(n, box, proposedText) {
    const name = n.status === 'none' ? n.newId : n.pattern;
    let failures = [];
    let fragment = '';
    for (let i = 0; i < settings.compose_retries; i++) {
      attempts++;
      const r = await ask('composer', [
          `# Subject rules\n${rules}`,
          `Request: ${n.text}`,
          n.status === 'none'
            ? `No pattern fits. Draw a new pattern with the id "${name}". Start from the nearest pattern "${n.nearest}" and apply these changes: ${n.deltas.join('; ') || 'none'}. Use data-p="${name}" data-v="plain". Use only known leaf parts.\n\nNearest pattern file:\n${patternText(n.nearest)}\n\nNew pattern file:\n${proposedText}`
            : `Pattern: ${n.pattern}, variant: ${n.variant}, params: ${JSON.stringify(n.params)}.\n\nPattern file:\n${patternText(n.pattern)}`,
          pack.boxText(box),
          failures.length ? `The last attempt failed these checks. Fix them:\n- ${failures.join('\n- ')}\n\nThe last attempt:\n${fragment}` : '',
        ].filter(Boolean).join('\n\n'));
      fragment = (r.data?.svg || '').trim();
      const parsed = d.adapter.parse(pack.wrap(fragment));
      const s = gradeStructure(parsed, d.patterns, d.adapter, { proposedId: n.status === 'none' ? name : undefined });
      const l = d.adapter.lint(parsed);
      failures = [...s.failures, ...l.failures, ...pack.checkBox(parsed.roots[0], box)];
      if (parsed.roots[0] && parsed.roots[0].p !== name) failures.push(`the top group must be data-p="${name}"`);
      if (!failures.length) return { fragment, ok: true };
    }
    return { fragment, ok: false, failures };
  }

  async function compose(n, box) {
    if (n.status === 'unresolved') return { fragment: '', ok: false, failures: [n.reason] };
    if (pack.assembled.has(n.pattern) && n.children.length) {
      const kids = n.children.map((i) => nodes[i]);
      const boxes = pack.layout({ pattern: n.pattern, children: kids });
      const done = await Promise.all(kids.map((k, i) => compose(k, boxes[i])));
      return { fragment: pack.assemble({ pattern: n.pattern, variant: n.variant }, done.map((x) => x.fragment)), ok: done.every((x) => x.ok), failures: done.flatMap((x) => x.failures || []) };
    }
    let proposedText = '';
    if (n.status === 'none') {
      try { scaffold(domainName, sandbox, n.newId, n.nearest); } catch { /* already there */ }
      proposedText = patternText(n.newId);
    }
    return composeLeafObject(n, box, proposedText);
  }

  const top = await compose(root, pack.layout({ pattern: root.pattern, children: [] })[0]);
  fs.writeFileSync(path.join(sandbox, d.config.output), pack.wrap(top.fragment));

  // ---------- 3. Trace from the graph state ----------
  const tree = (n) => {
    const t = { p: n.status === 'none' ? n.newId : n.pattern };
    if (n.variant) t.v = n.variant; else if (n.status === 'none') t.v = 'plain';
    if (n.params && Object.keys(n.params).length) t.params = n.params;
    const kids = n.children.map((i) => nodes[i]).filter((k) => k.status !== 'unresolved');
    if (kids.length) t.parts = kids.map(tree);
    return t;
  };
  const trace = {
    outcome: root.status === 'none' ? 'no-fit' : 'match',
    tree: root.status === 'unresolved' ? null : tree(root),
    nearest: root.nearest || null,
    deltas: root.deltas || [],
    proposed: root.status === 'none' ? `patterns/${root.newId}.md` : null,
    questions: nodes.map((n) => `${n.text} → ${n.status}${n.pattern ? ' ' + n.pattern : ''}: ${n.reason || ''}`),
    attempts,
    graph: nodes.map(({ id, text, status, pattern, variant, params, children, depth, nearest, newId }) => ({ id, text, status, pattern, variant, params, children, depth, nearest, newId })),
  };
  fs.writeFileSync(path.join(sandbox, 'trace.json'), JSON.stringify(trace, null, 2) + '\n');
  fs.writeFileSync(path.join(sandbox, 'agent.log.json'), JSON.stringify({ calls: stats.calls, cost: stats.cost, log: stats.log }, null, 2));
  return {
    code: 0, ms: Date.now() - t0, turns: stats.calls, cost: stats.cost, subagents: 0,
    error: top.fragment ? null : 'no output',
  };
}

