// Eval settings: eval.config.yaml in the engine root, merged with domains/<name>/eval.config.yaml.
import fs from 'node:fs';
import path from 'node:path';
import YAML from 'yaml';
import { ENGINE_ROOT, domainDir } from './patterns.mjs';

function merge(a, b) {
  if (b === undefined) return a;
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object' || Array.isArray(a) || Array.isArray(b)) return b;
  const out = { ...a };
  for (const [k, v] of Object.entries(b)) out[k] = merge(a[k], v);
  return out;
}
const read = (f) => (fs.existsSync(f) ? YAML.parse(fs.readFileSync(f, 'utf8')) || {} : {});

export function loadConfig(domain) {
  const base = read(path.join(ENGINE_ROOT, 'eval.config.yaml'));
  return domain ? merge(base, read(path.join(domainDir(domain), 'eval.config.yaml'))) : base;
}

/** The targets for a mode: targets[mode], else targets.default. null means no targets. */
export function targetsFor(config, mode) {
  const t = config.targets || {};
  return Object.prototype.hasOwnProperty.call(t, mode) ? t[mode] : t.default ?? null;
}
