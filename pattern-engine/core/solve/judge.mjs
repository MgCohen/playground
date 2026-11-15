// The model boundary of the solver. Each judgment is one agent file: .claude/agents/<name>.md.
// The engine reads the file and builds the call from it, so the file is the one source of truth:
//   model          → the agent's model (a run setting can override it)
//   tools          → the allowed tools; `StructuredOutput` lets the CLI enforce the schema
//   output_schema  → our own field (Claude Code ignores it): a JSON schema file, passed as --json-schema
//   body           → the system prompt (`--agent` replaces the default one)
// claudeJudge runs `claude -p --agent <name>` with the user's login. scriptedJudge is for tests.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import YAML from 'yaml';

/** Read one agent file: its front matter, its body and its schema. */
export function readAgent(dir, name) {
  const file = path.join(dir, '.claude', 'agents', `${name}.md`);
  if (!fs.existsSync(file)) throw new Error(`no agent file ${path.relative(dir, file)}`);
  const m = fs.readFileSync(file, 'utf8').match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)$/);
  if (!m) throw new Error(`${name}.md: no front matter`);
  const fm = YAML.parse(m[1]) || {};
  if (!fm.output_schema) throw new Error(`${name}.md: no output_schema`);
  const tools = String(fm.tools || '').split(',').map((t) => t.trim());
  if (!tools.includes('StructuredOutput')) throw new Error(`${name}.md: tools must include StructuredOutput, or the schema is not enforced`);
  const schema = JSON.parse(fs.readFileSync(path.join(dir, fm.output_schema), 'utf8'));
  return { name, file, frontMatter: fm, body: m[2], schema };
}

export function claudeJudge({ dir, model, timeoutMs = 120000 } = {}) {
  const cache = new Map();
  const agent = (name) => { if (!cache.has(name)) cache.set(name, readAgent(dir, name)); return cache.get(name); };
  const once = (a, prompt) => new Promise((resolve) => {
    const args = [
      '-p', prompt,
      '--agent', a.name,
      '--json-schema', JSON.stringify(a.schema),
      '--output-format', 'json',
      '--setting-sources', 'project',
      '--strict-mcp-config',
      '--no-session-persistence',
    ];
    if (model) args.push('--model', model); // a run setting overrides the model of the file
    const t0 = Date.now();
    const child = spawn('claude', args, { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
    let out = '', err = '';
    child.stdout.on('data', (d) => (out += d));
    child.stderr.on('data', (d) => (err += d));
    const timer = setTimeout(() => { child.kill(); err += '[timeout]'; }, timeoutMs);
    child.on('close', (code) => {
      clearTimeout(timer);
      let meta = {};
      try { meta = JSON.parse(out.slice(out.indexOf('{'))); } catch { /* not JSON */ }
      const data = meta.structured_output ?? null;
      resolve({ data, ms: Date.now() - t0, cost: meta.total_cost_usd || 0, error: data ? null : (err || meta.result || `exit ${code}`).slice(-300) });
    });
  });
  return {
    agent,
    async call({ agent: name, prompt }) {
      const a = agent(name);
      const first = await once(a, prompt);
      if (first.data) return first;
      const second = await once(a, prompt); // one retry for a call that gave no structured answer
      return { ...second, ms: first.ms + second.ms, cost: first.cost + second.cost };
    },
  };
}

/** For tests: answers come from a function (agentName, request) -> data. */
export function scriptedJudge(fn) {
  return { async call(req) { return { data: fn(req.agent, req), ms: 0, cost: 0, error: null }; } };
}
