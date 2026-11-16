// Solver hooks for the "shapes" domain: layout slots, assembly of groups, and the frame for a check.
// The core solver calls these. Code does the layout, so the model draws one object in one box.

export const CANVAS = { w: 400, h: 300 };
const GROUND = 262;

/** Patterns that code assembles from their composed children. */
export const assembled = new Set(['street']);

/** The box for each child of a group, or for a single top object. */
export function layout(node) {
  if (node.pattern === 'street') {
    const n = Math.max(node.children.length, 1);
    const slot = (CANVAS.w - 10) / n;
    return node.children.map((_, i) => ({ x: Math.round(5 + i * slot + slot * 0.08), w: Math.round(slot * 0.84), base: GROUND, maxH: GROUND - 12 }));
  }
  return [{ x: 70, w: 260, base: 285, maxH: 275 }];
}

/** The text that tells the model where to draw. */
export function boxText(b) {
  return `Draw inside x from ${b.x} to ${b.x + b.w}. The bottom of the object is on y = ${b.base}. The top is not above y = ${b.base - b.maxH}. You can use less width than the box.`;
}

/** Code builds the group from the fragments of its children. */
export function assemble(node, fragments) {
  if (node.pattern === 'street') {
    return `<g data-p="street" data-v="${node.variant || 'row'}"><g data-p="ground"><rect x="0" y="${GROUND}" width="${CANVAS.w}" height="${CANVAS.h - GROUND}" fill="green"/></g>${fragments.join('')}</g>`;
  }
  throw new Error(`no assembly for ${node.pattern}`);
}

/** A full output from one fragment. */
export function wrap(fragment) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${CANVAS.w} ${CANVAS.h}">${fragment}</svg>`;
}

/** Problems if the composed object is not in its box. */
export function checkBox(parsedRoot, b) {
  const x = parsedRoot?.box;
  if (!x) return ['the fragment has no shapes'];
  const out = [];
  if (x.left < b.x - 3 || x.right > b.x + b.w + 3) out.push(`the object goes outside x ${b.x}..${b.x + b.w} (it is ${Math.round(x.left)}..${Math.round(x.right)})`);
  if (Math.abs(x.bottom - b.base) > 4) out.push(`the bottom must be on y = ${b.base} (it is ${Math.round(x.bottom)})`);
  if (x.top < b.base - b.maxH - 3) out.push(`the top is above y = ${b.base - b.maxH}`);
  return out;
}

