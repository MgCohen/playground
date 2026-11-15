// The one place that prepares a sandbox and calls the agent.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import YAML from 'yaml';
import { ENGINE_ROOT, loadPatterns, toIndexMarkdown } from '../lib/patterns.mjs';

export const HARNESSES = path.join(ENGINE_ROOT, 'harnesses');

export function loadHarness(name) {
  const dir = path.join(HARNESSES, name);
  if (!fs.existsSync(path.join(dir, 'harness.yaml'))) throw new Error(`no harness "${name}" in ${HARNESSES}`);
  return { dir, ...YAML.parse(fs.readFileSync(path.join(dir, 'harness.yaml'), 'utf8')) };
}

/** Remove the blocks marked <!-- ablate:name --> ... <!-- /ablate --> for each active ablation. */
function ablateText(text, ablate) {
  return text.replace(/<!-- ablate:([\w-]+) -->\r?\n([\s\S]*?)<!-- \/ablate -->\r?\n?/g, (_, name, body) => (ablate.includes(name) ? '' : body));
}

function copyTree(src, dst, ablate) {
  fs.mkdirSync(dst, { recursive: true });
  for (const e of fs.readdirSync(src, { withFileTypes: true })) {
    if (e.name === 'harness.yaml') continue;
    const s = path.join(src, e.name), d = path.join(dst, e.name);
    if (e.isDirectory()) copyTree(s, d, ablate);
    else if (e.name.endsWith('.md')) fs.writeFileSync(d, ablateText(fs.readFileSync(s, 'utf8'), ablate));
    else fs.copyFileSync(s, d);
  }
}

/** Copy what the agent may read into a new folder. Ablations change the copies only. */
export function prepareSandbox(domain, dir, { ablate = [], harness = 'modular' } = {}) {
  const src = path.join(ENGINE_ROOT, 'domains', domain);
  fs.mkdirSync(path.join(dir, 'patterns'), { recursive: true });
  const patterns = loadPatterns(path.join(src, 'patterns'), { includeProposed: false });
  for (const p of patterns.values()) {
    let text = fs.readFileSync(path.join(src, 'patterns', p.file), 'utf8');
    if (ablate.includes('no-dont-use')) {
      text = text.replace(/^do_not_use_when:\r?\n(?:[ \t]+.*\r?\n)+/m, '');
      delete p.do_not_use_when;
    }
    fs.writeFileSync(path.join(dir, 'patterns', p.file), text);
  }
  fs.writeFileSync(path.join(dir, 'index.md'), toIndexMarkdown(patterns, domain));
  fs.copyFileSync(path.join(src, 'instructions.md'), path.join(dir, 'instructions.md'));
  const h = loadHarness(harness);
  copyTree(h.dir, dir, ablate);
  if (ablate.includes('no-nofit')) fs.rmSync(path.join(dir, '.claude', 'skills', 'pattern-nofit'), { recursive: true, force: true });
  const url = (f) => JSON.stringify(pathToFileURL(path.join(ENGINE_ROOT, 'core', 'eval', f)).href);
  fs.writeFileSync(path.join(dir, 'verify.mjs'), `import(${url('verify.mjs')}).then(m => m.main(${JSON.stringify(domain)}, process.cwd()));\n`);
  fs.writeFileSync(path.join(dir, 'pe.mjs'), `import(${url('pe-cli.mjs')}).then(m => m.main(${JSON.stringify(domain)}, process.cwd(), process.argv.slice(2)));\n`);
  return h;
}

/** Run the agent headless in the sandbox. Project settings only: no user settings, plugins or MCP servers. */
export function runAgent(dir, request, { model, output, harness, extra, timeoutMs = 10 * 60 * 1000 } = {}) {
  let prompt = harness.prompt.replaceAll('{{request}}', request).replaceAll('{{output}}', output).trim();
  if (extra) prompt += '\n\n' + extra;
  const args = [
    '-p', prompt,
    '--output-format', 'json',
    '--setting-sources', 'project',
    '--strict-mcp-config',
    '--no-session-persistence',
    '--permission-mode', 'acceptEdits',
    '--allowedTools', ...harness.tools,
  ];
  if (model) args.push('--model', model);
  return new Promise((resolve) => {
    const t0 = Date.now();
    const child = spawn('claude', args, { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
    let out = '', err = '';
    child.stdout.on('data', d => (out += d));
    child.stderr.on('data', d => (err += d));
    const timer = setTimeout(() => { child.kill(); err += '\n[timeout]'; }, timeoutMs);
    child.on('close', (code) => {
      clearTimeout(timer);
      let meta = {};
      try { meta = JSON.parse(out.slice(out.indexOf('{'))); } catch { meta = { raw: out.slice(-2000) }; }
      const logFile = path.join(dir, 'agent.log.json');
      const prev = fs.existsSync(logFile) ? JSON.parse(fs.readFileSync(logFile, 'utf8')) : [];
      fs.writeFileSync(logFile, JSON.stringify([...[].concat(prev), { code, meta, stderr: err.slice(-4000) }], null, 2));
      resolve({
        code, ms: Date.now() - t0, turns: meta.num_turns, cost: meta.total_cost_usd,
        subagents: meta.subagent_stats?.spawned ?? 0,
        error: code ? (err || meta.result || 'exit ' + code).slice(-500) : null,
      });
    });
  });
}
