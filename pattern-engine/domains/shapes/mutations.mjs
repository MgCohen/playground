// Mutation operators for the "shapes" domain. Each one breaks a known-good output in one way.
// The core applies each operator to each reference drawing. The graders must fail the result,
// and at least one grader in `expect` must be among the failures (the right reason).
// apply({ svg, tree, adapter }) returns the broken SVG, or null if the operator does not apply.

const OBJECT = /<g data-p="(house|tower|shed|tree)" data-v="[^"]*">(?:<g data-p="[a-z]+"><[^>]*\/><\/g>)+<\/g>/g;
const leafRe = (name) => new RegExp(`<g data-p="${name}"><[^>]*\\/><\\/g>`);
const num = (s, attr) => Number((s.match(new RegExp(`\\s${attr}="([-\\d.]+)"`)) || [])[1]);
const setAttr = (s, attr, v) => s.replace(new RegExp(`(\\s${attr}=")[-\\d.]+(")`), `$1${Math.round(v * 10) / 10}$2`);

/** Move every coordinate of an SVG fragment by dx. */
function shiftX(fragment, dx) {
  return fragment
    .replace(/(\s(?:x|cx)=")([-\d.]+)(")/g, (_, a, n, b) => a + (Number(n) + dx) + b)
    .replace(/points="([^"]*)"/g, (_, pts) => `points="${pts.trim().split(/\s+/).map((p) => { const [x, y] = p.split(','); return `${Number(x) + dx},${y}`; }).join(' ')}"`);
}
const objects = (svg) => [...svg.matchAll(OBJECT)].map((m) => ({ text: m[0], index: m.index }));
const differ = (a, b) => JSON.stringify(a) !== JSON.stringify(b);
const allParams = (t, out = []) => { if (t.params) out.push(t.params); (t.parts || []).forEach((p) => allParams(p, out)); return out; };

export const operators = [
  { id: 'remove-roof', expect: ['structure'], describe: 'Remove the roof of the first building',
    apply: ({ svg }) => (leafRe('roof').test(svg) ? svg.replace(leafRe('roof'), '') : null) },
  { id: 'remove-ground', expect: ['structure'], describe: 'Remove the ground of a street',
    apply: ({ svg }) => (leafRe('ground').test(svg) ? svg.replace(leafRe('ground'), '') : null) },
  { id: 'remove-chimney', expect: ['structure'], describe: 'Remove the chimney of a chimney house',
    apply: ({ svg }) => (leafRe('chimney').test(svg) ? svg.replace(leafRe('chimney'), '') : null) },
  { id: 'door-up', expect: ['structure'], describe: 'Move the first door up, off the bottom of the body',
    apply: ({ svg }) => { const m = svg.match(leafRe('door')); return m ? svg.replace(m[0], setAttr(m[0], 'y', num(m[0], 'y') - 25)) : null; } },
  { id: 'window-over-door', expect: ['structure'], describe: 'Put the first window on top of the door',
    apply: ({ svg }) => {
      const obj = objects(svg).find((o) => leafRe('window').test(o.text) && leafRe('door').test(o.text));
      if (!obj) return null;
      const d = obj.text.match(leafRe('door'))[0], w = obj.text.match(leafRe('window'))[0];
      const moved = setAttr(setAttr(w, 'x', num(d, 'x')), 'y', num(d, 'y'));
      return svg.replace(obj.text, obj.text.replace(w, moved));
    } },
  { id: 'swap-children', expect: ['built'], describe: 'Swap the first two objects of a street (wrong order)',
    apply: ({ svg, tree }) => {
      if (tree.p !== 'street' || (tree.parts || []).length < 2 || !differ(tree.parts[0], tree.parts[1])) return null;
      const [a, b] = objects(svg);
      return svg.slice(0, a.index) + b.text + svg.slice(a.index + a.text.length, b.index) + a.text + svg.slice(b.index + b.text.length);
    } },
  { id: 'change-param', expect: ['built'], describe: 'Paint the doors that have the asked color in another color',
    apply: ({ svg, tree }) => {
      const p = allParams(tree).find((x) => x.door_fill);
      if (!p) return null;
      const re = new RegExp(`(<g data-p="door"><rect [^>]*fill=")${p.door_fill}(")`, 'g');
      return re.test(svg) ? svg.replace(re, '$1black$2') : null;
    } },
  { id: 'wrong-variant', expect: ['structure', 'built'], describe: 'Mark a chimney house as plain',
    apply: ({ svg }) => (svg.includes('data-v="chimney"') ? svg.replace('data-v="chimney"', 'data-v="plain"') : null) },
  { id: 'wrong-type', expect: ['built'], describe: 'Mark the first house as a tower',
    apply: ({ svg }) => (svg.includes('data-p="house"') ? svg.replace('data-p="house"', 'data-p="tower"') : null) },
  { id: 'overlap', expect: ['structure'], describe: 'Move the second object of a street onto the first',
    apply: ({ svg, tree, adapter }) => {
      if (tree.p !== 'street' || (tree.parts || []).length < 2) return null;
      const [a, b] = objects(svg);
      const box = (t) => adapter.parse(`<svg viewBox="0 0 400 300">${t}</svg>`).roots[0].box;
      return svg.replace(b.text, shiftX(b.text, box(a.text).left - box(b.text).left));
    } },
  { id: 'outside-canvas', expect: ['rules'], describe: 'Move the first object out of the canvas',
    apply: ({ svg }) => { const [a] = objects(svg); return a ? svg.replace(a.text, shiftX(a.text, 500)) : null; } },
  { id: 'add-path', expect: ['rules'], describe: 'Add a path element, which the rules forbid',
    apply: ({ svg }) => { const [a] = objects(svg); return a ? svg.replace(a.text, a.text.replace(/^(<g [^>]*>)/, '$1<path d="M0 0L5 5" fill="black"/>')) : null; } },
  { id: 'no-fill', expect: ['rules'], describe: 'Remove the fill of the first body',
    apply: ({ svg }) => { const m = svg.match(leafRe('body')); return m ? svg.replace(m[0], m[0].replace(/\sfill="[^"]*"/, '')) : null; } },
];
