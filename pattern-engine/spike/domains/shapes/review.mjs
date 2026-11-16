// Review handlers for the "shapes" domain. The core review builder calls these
// to show one run to a person. Another domain gives other HTML (a code block, a page preview).
import fs from 'node:fs';
import path from 'node:path';
import { referenceSvg } from './reference.mjs';

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

/** Remove anything that can run code. The SVG comes from an agent. */
function cleanSvg(svg) {
  return svg
    .replace(/<\?xml[\s\S]*?\?>/g, '')
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<foreignObject[\s\S]*?<\/foreignObject>/gi, '')
    .replace(/\son\w+\s*=\s*("[^"]*"|'[^']*')/gi, '')
    .replace(/(href|xlink:href)\s*=\s*("|')\s*javascript:[^"']*\2/gi, '');
}

/** The output of one run, as HTML. */
export function renderOutput(runDir) {
  const f = path.join(runDir, 'output.svg');
  if (!fs.existsSync(f)) return { html: '<p class="none">No output file.</p>' };
  return { html: `<div class="svg-frame">${cleanSvg(fs.readFileSync(f, 'utf8'))}</div>` };
}

function tree(t) {
  if (!t) return '';
  const params = Object.entries(t.params || {}).map(([k, v]) => `<span class="param">${esc(k.replace(/_/g, ' '))}: ${esc(v)}</span>`).join('');
  const kids = (t.parts || []).map(tree).join('');
  return `<li><span class="pat">${esc(t.p)}</span>${t.v ? `<span class="var">${esc(t.v)}</span>` : ''}${params}${kids ? `<ol>${kids}</ol>` : ''}</li>`;
}

/** The expected answer as a picture: the reference renderer draws the expected tree. */
export function renderExpectedVisual(expect) {
  if (expect.outcome === 'no-fit') {
    const near = [].concat(expect.nearest || []);
    const svg = referenceSvg({ p: near[0], v: 'plain' });
    const pic = svg ? `<div class="svg-frame faded">${svg}</div>` : '';
    return { html: `${pic}<p class="caption">No pattern fits. The agent must start from the nearest pattern (${near.map(esc).join(' or ')}) and change it.</p>` };
  }
  const svg = referenceSvg(expect.tree);
  return { html: svg ? `<div class="svg-frame">${svg}</div>` : '<p class="none">No reference drawing for this pattern.</p>' };
}

/** The expected answer of the case, as a tree (for the detail view). */
export function renderExpected(expect) {
  if (expect.outcome === 'no-fit') {
    const near = [].concat(expect.nearest || []).map(esc).join(' or ');
    return { html: `<p><b>No pattern fits.</b> The agent must not force one. It must start from the nearest pattern (${near}), list the changes and write a proposed pattern.</p>` };
  }
  return { html: `<ol class="tree">${tree(expect.tree)}</ol><p class="hint">Left to right, in this order. Only the listed details are checked.</p>` };
}

/** What the agent reported (trace.json), as HTML. */
export function renderTrace(trace) {
  if (!trace) return { html: '<p class="none">No trace.json.</p>' };
  const parts = [`<p>Outcome: <b>${esc(trace.outcome)}</b>${trace.nearest ? ` · nearest: <b>${esc(typeof trace.nearest === 'string' ? trace.nearest : JSON.stringify(trace.nearest))}</b>` : ''}</p>`];
  if (trace.tree) parts.push(`<ol class="tree">${tree(trace.tree)}</ol>`);
  const qs = (trace.questions || []).filter(Boolean);
  if (qs.length) parts.push(`<ul class="qs">${qs.map((q) => `<li>${esc(typeof q === 'string' ? q : JSON.stringify(q))}</li>`).join('')}</ul>`);
  return { html: parts.join('') };
}
