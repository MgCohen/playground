// The model boundary of the solver: one small call, no tools, a short system prompt, a JSON schema.
// claudeJudge uses the Claude CLI and the user's login. scriptedJudge returns fixed answers for tests.
import { spawn } from 'node:child_process';

export function claudeJudge({ model = 'haiku', timeoutMs = 120000, cwd } = {}) {
  const once = ({ system, prompt, schema, model: m }) => new Promise((resolve) => {
    const args = [
      '-p', prompt,
      '--output-format', 'json',
      '--json-schema', JSON.stringify(schema),
      '--tools', '',
      '--system-prompt', system,
      '--setting-sources', 'project',
      '--strict-mcp-config',
      '--no-session-persistence',
      '--model', m || model,
    ];
    const t0 = Date.now();
    const child = spawn('claude', args, { cwd, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
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
    async call(req) {
      const a = await once(req);
      if (a.data) return a;
      const b = await once(req); // one retry for a call that gave no structured answer
      return { ...b, ms: a.ms + b.ms, cost: a.cost + b.cost };
    },
  };
}

/** For tests: answers come from a function (role, input) -> data. */
export function scriptedJudge(fn) {
  return { async call(req) { return { data: fn(req.role, req), ms: 0, cost: 0, error: null }; } };
}
