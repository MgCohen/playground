// "pe": the deterministic commands for the agent. The sandbox has a small pe.mjs that calls main().
// The runner calls the same functions (verify, trace), so agent and system use one code path.
//   node pe.mjs index                      the pattern index as JSON
//   node pe.mjs parts <id> [variant]       one pattern: parts for the variant, params, asserts, rules
//   node pe.mjs verify [--json]            structure + rules checks of the output
//   node pe.mjs trace [--write]            the pattern tree of the output; --write puts it into trace.json
//   node pe.mjs scaffold <id> --from <id>  a proposed pattern file from the template
import fs from 'node:fs';
import path from 'node:path';
import { loadDomain } from '../lib/domain.mjs';
import { partsFor, parsePatternText } from '../lib/patterns.mjs';
import { builtTree } from './graders/structure.mjs';
import { check } from './verify.mjs';

const out = (o) => console.log(JSON.stringify(o, null, 2));

export async function index(domain, dir) {
  const d = await loadDomain(domain, { patternsDir: path.join(dir, 'patterns') });
  return [...d.patterns.values()].filter(p => p.status !== 'proposed').map(p => ({
    id: p.id, kind: p.kind, level: p.level,
    use_when: p.use_when, do_not_use_when: p.do_not_use_when,
    variants: p.variants?.map(v => ({ id: v.id, when: v.when })),
  }));
}

export async function parts(domain, dir, id, variant) {
  const d = await loadDomain(domain, { patternsDir: path.join(dir, 'patterns') });
  const p = d.patterns.get(id);
  if (!p) throw new Error(`no pattern "${id}"`);
  const v = variant || p.variants?.[0]?.id;
  return {
    id: p.id, kind: p.kind, level: p.level, variant: v,
    parts: partsFor(p, v).map(pt => {
      const ref = d.patterns.get(pt.ref);
      return { ref: pt.ref, count: String(pt.count ?? 1), kind: ref?.kind, primitive: ref?.primitive };
    }),
    params: p.params || {}, asserts: p.asserts || [], rules: p.sections?.Rules || '',
  };
}

export async function trace(domain, dir) {
  const d = await loadDomain(domain, { patternsDir: path.join(dir, 'patterns') });
  const file = path.join(dir, d.config.output);
  if (!fs.existsSync(file)) throw new Error(`no ${d.config.output}`);
  const parsed = d.adapter.parse(fs.readFileSync(file, 'utf8'));
  if (!parsed.roots[0]) throw new Error('no pattern group in the output');
  return builtTree(parsed.roots[0], d.patterns, d.adapter);
}

export async function scaffold(domain, dir, id, from) {
  const d = await loadDomain(domain, { patternsDir: path.join(dir, 'patterns') });
  const near = d.patterns.get(from);
  if (!near || near.kind === 'leaf') throw new Error(`--from must name a pattern that is not a leaf`);
  const tplFile = path.join(dir, 'templates', 'pattern.proposed.md');
  const tpl = fs.existsSync(tplFile) ? fs.readFileSync(tplFile, 'utf8') : null;
  if (!tpl) throw new Error('no templates/pattern.proposed.md');
  const partsYaml = (near.parts || []).map(pt => `  - { ref: ${pt.ref}, count: "${pt.count ?? 1}" }`).join('\n');
  const text = tpl.replaceAll('{{id}}', id).replaceAll('{{from}}', from).replaceAll('{{kind}}', near.kind)
    .replaceAll('{{level}}', String(near.level)).replaceAll('{{parts}}', partsYaml);
  parsePatternText(text, `${id}.md`); // fails early if the template is broken
  const target = path.join(dir, 'patterns', `${id}.md`);
  if (fs.existsSync(target)) throw new Error(`patterns/${id}.md exists`);
  fs.writeFileSync(target, text);
  return { file: `patterns/${id}.md`, from, note: 'Edit use_when, parts, asserts and the sections. Keep status: proposed.' };
}

export async function main(domain, dir, argv) {
  const [cmd, ...rest] = argv;
  try {
    if (cmd === 'index') return out(await index(domain, dir));
    if (cmd === 'parts') return out(await parts(domain, dir, rest[0], rest[1]));
    if (cmd === 'verify') {
      const r = await check(domain, dir);
      if (rest.includes('--json')) out(r);
      else if (r.ok) console.log('PASS: structure and rules checks pass.');
      else { console.log(`FAIL: ${r.failures.length} problem(s):`); r.failures.forEach(f => console.log(' - ' + f)); }
      process.exitCode = r.ok ? 0 : 1;
      return;
    }
    if (cmd === 'trace') {
      const tree = await trace(domain, dir);
      if (rest.includes('--write')) {
        const f = path.join(dir, 'trace.json');
        let t = {};
        try { t = JSON.parse(fs.readFileSync(f, 'utf8')); } catch { /* new */ }
        if (!t.outcome) {
          const tpl = path.join(dir, 'templates', 'trace.json');
          if (fs.existsSync(tpl)) t = { ...JSON.parse(fs.readFileSync(tpl, 'utf8')), ...t };
        }
        t.tree = tree;
        fs.writeFileSync(f, JSON.stringify(t, null, 2) + '\n');
        console.log('Wrote the tree into trace.json. Set outcome, nearest, deltas, proposed, questions and attempts.');
      }
      return out(tree);
    }
    if (cmd === 'scaffold') {
      const i = rest.indexOf('--from');
      return out(await scaffold(domain, dir, rest[0], i >= 0 ? rest[i + 1] : undefined));
    }
    console.log('usage: node pe.mjs index | parts <id> [variant] | verify [--json] | trace [--write] | scaffold <id> --from <id>');
    process.exitCode = 2;
  } catch (e) {
    console.log(`ERROR: ${e.message}`);
    process.exitCode = 1;
  }
}
