// Validate the patterns of a domain and write build/graph.json and build/index.md.
// Usage: node core/graph/build.mjs <domain>
import fs from 'node:fs';
import path from 'node:path';
import { domainDir, loadPatterns, toGraph, toIndexMarkdown } from '../lib/patterns.mjs';
import { assertValid } from '../lib/domain.mjs';

export function build(domain) {
  const dir = domainDir(domain);
  const patterns = loadPatterns(path.join(dir, 'patterns'));
  assertValid(patterns);
  const out = path.join(dir, 'build');
  fs.mkdirSync(out, { recursive: true });
  const graph = toGraph(patterns);
  fs.writeFileSync(path.join(out, 'graph.json'), JSON.stringify(graph, null, 2) + '\n');
  fs.writeFileSync(path.join(out, 'index.md'), toIndexMarkdown(patterns, domain));
  return { patterns, graph };
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('build.mjs')) {
  const domain = process.argv[2] || 'shapes';
  const { patterns, graph } = build(domain);
  const levels = [...new Set([...patterns.values()].map(p => p.level))].sort();
  console.log(`${domain}: ${patterns.size} patterns, ${graph.edges.length} edges, levels ${levels.join(', ')}. Wrote build/graph.json and build/index.md.`);
}
