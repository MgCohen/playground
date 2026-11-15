// Load a domain pack: its config, its adapter and its patterns.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import YAML from 'yaml';
import { domainDir, loadPatterns, validate } from './patterns.mjs';

const REQUIRED = ['parse', 'lint', 'assert', 'checkLeaf', 'readParam'];

export async function loadDomain(domain, { patternsDir } = {}) {
  const dir = domainDir(domain);
  if (!fs.existsSync(dir)) throw new Error(`no domain "${domain}" in ${dir}`);
  const config = YAML.parse(fs.readFileSync(path.join(dir, 'domain.yaml'), 'utf8'));
  const adapter = await import(pathToFileURL(path.join(dir, 'adapter.mjs')).href);
  for (const fn of REQUIRED) if (typeof adapter[fn] !== 'function') throw new Error(`adapter of "${domain}" has no ${fn}()`);
  const patterns = loadPatterns(patternsDir || path.join(dir, 'patterns'));
  return { name: domain, dir, config, adapter, patterns };
}

export function assertValid(patterns) {
  const { errors, warnings } = validate(patterns);
  warnings.forEach(w => console.warn('warn:', w));
  if (errors.length) {
    errors.forEach(e => console.error('error:', e));
    throw new Error(`${errors.length} pattern error(s)`);
  }
}
