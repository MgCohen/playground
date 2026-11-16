// Reference renderer: draws an expected pattern tree as SVG, with fixed geometry.
// The drawing follows the shapes rules (data-p groups, allowed shapes, asserts), so it
// is a valid fixture by construction. reference.test in review.mjs checks that.

const W = 400, H = 300, GROUND = 262;
const COLORS = { body: '#f3ead6', roof: '#7a7470', door: 'brown', window: '#9fd3f0', chimney: '#a0522d', ground: '#7cb36b', trunk: '#8b5a2b', crown: 'green' };

// Height of a drawn object as a multiple of its width.
const FACTOR = { house: 1.04, tower: 2.75, shed: 0.85, tree: 1.08 };

const r = (n) => Math.round(n * 10) / 10;
const rect = (x, y, w, h, fill) => `<rect x="${r(x)}" y="${r(y)}" width="${r(w)}" height="${r(h)}" fill="${fill}"/>`;
const tri = (pts, fill) => `<polygon points="${pts.map(([a, b]) => `${r(a)},${r(b)}`).join(' ')}" fill="${fill}"/>`;
const leaf = (p, shape) => `<g data-p="${p}">${shape}</g>`;

function house(t, x, base, w) {
  const bh = 0.62 * w, top = base - bh, rh = 0.42 * w, door = t.params?.door_fill || COLORS.door;
  const parts = [];
  if (t.v === 'chimney') parts.push(leaf('chimney', rect(x + 0.68 * w, top - 0.85 * rh, 0.12 * w, 0.5 * rh, COLORS.chimney)));
  parts.push(leaf('body', rect(x, top, w, bh, COLORS.body)));
  parts.push(leaf('roof', tri([[x - 0.06 * w, top], [x + 0.5 * w, top - rh], [x + 1.06 * w, top]], COLORS.roof)));
  parts.push(leaf('door', rect(x + 0.4 * w, base - 0.5 * bh, 0.2 * w, 0.5 * bh, door)));
  parts.push(leaf('window', rect(x + 0.1 * w, top + 0.2 * bh, 0.18 * w, 0.18 * w, COLORS.window)));
  parts.push(leaf('window', rect(x + 0.72 * w, top + 0.2 * bh, 0.18 * w, 0.18 * w, COLORS.window)));
  return parts;
}
function tower(t, x, base, w) {
  const bh = 2.2 * w, top = base - bh, rh = 0.55 * w, door = t.params?.door_fill || COLORS.door;
  return [
    leaf('body', rect(x, top, w, bh, COLORS.body)),
    leaf('roof', tri([[x - 0.05 * w, top], [x + 0.5 * w, top - rh], [x + 1.05 * w, top]], COLORS.roof)),
    leaf('door', rect(x + 0.35 * w, base - 0.45 * w, 0.3 * w, 0.45 * w, door)),
    leaf('window', rect(x + 0.33 * w, top + 0.25 * w, 0.34 * w, 0.34 * w, COLORS.window)),
    leaf('window', rect(x + 0.33 * w, top + 0.95 * w, 0.34 * w, 0.34 * w, COLORS.window)),
  ];
}
function shed(t, x, base, w) {
  const bh = 0.55 * w, top = base - bh, rh = 0.3 * w, door = t.params?.door_fill || COLORS.door;
  return [
    leaf('body', rect(x, top, w, bh, COLORS.body)),
    leaf('roof', tri([[x - 0.04 * w, top], [x + 0.5 * w, top - rh], [x + 1.04 * w, top]], COLORS.roof)),
    leaf('door', rect(x + 0.25 * w, base - 0.75 * bh, 0.5 * w, 0.75 * bh, door)),
  ];
}
function tree(t, x, base, w) {
  const tw = 0.16 * w, th = 0.55 * w, cx = x + 0.5 * w, cr = 0.38 * w, cy = base - th - 0.15 * w;
  return [
    leaf('trunk', rect(cx - tw / 2, base - th, tw, th, COLORS.trunk)),
    leaf('crown', `<circle cx="${r(cx)}" cy="${r(cy)}" r="${r(cr)}" fill="${COLORS.crown}"/>`),
  ];
}
const DRAW = { house, tower, shed, tree };

function object(t, x, base, w) {
  const draw = DRAW[t.p];
  if (!draw) return `<g data-p="${t.p}"></g>`;
  return `<g data-p="${t.p}" data-v="${t.v || 'plain'}">${draw(t, x, base, w).join('')}</g>`;
}

/** Draw a tree of patterns. Returns an SVG string, or null if a pattern has no drawing. */
export function referenceSvg(t) {
  let body;
  if (t.p === 'street') {
    const kids = t.parts || [];
    const slot = (W - 10) / Math.max(kids.length, 1);
    const objs = kids.map((k, i) => {
      const w = Math.min(slot * 0.7, (GROUND - 14) / (FACTOR[k.p] || 1), 150);
      return object(k, 5 + i * slot + (slot - w) / 2, GROUND, w);
    });
    body = `<g data-p="street" data-v="${t.v || 'row'}">${leaf('ground', rect(0, GROUND, W, H - GROUND, COLORS.ground))}${objs.join('')}</g>`;
  } else if (DRAW[t.p]) {
    const w = Math.min(220, 255 / (FACTOR[t.p] || 1));
    body = object(t, (W - w) / 2, 285, w);
  } else return null;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}">${body}</svg>`;
}
