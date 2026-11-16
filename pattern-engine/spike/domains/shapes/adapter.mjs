// Adapter for the "shapes" domain: SVG drawings made of rect, polygon (triangle), circle and line.
// The core calls parse, lint, assert, checkLeaf and readParam. Nothing else in the core knows SVG.

export const CANVAS = { w: 400, h: 300 };
const TOL = 4; // px tolerance for == in asserts
const DRAWN = new Set(['rect', 'polygon', 'circle', 'line', 'ellipse', 'path', 'text', 'image', 'polyline']);
const ALLOWED = new Set(['rect', 'polygon', 'circle', 'line']);

// ---------- parse ----------

function attrs(s) {
  const out = {};
  for (const m of s.matchAll(/([a-zA-Z_:][\w:.-]*)\s*=\s*("([^"]*)"|'([^']*)')/g)) out[m[1]] = m[3] ?? m[4];
  return out;
}
const num = (v) => (v === undefined ? NaN : parseFloat(v));

function elBox(type, a) {
  if (type === 'rect') return { left: num(a.x || 0), top: num(a.y || 0), right: num(a.x || 0) + num(a.width), bottom: num(a.y || 0) + num(a.height) };
  if (type === 'circle') return { left: num(a.cx) - num(a.r), right: num(a.cx) + num(a.r), top: num(a.cy) - num(a.r), bottom: num(a.cy) + num(a.r) };
  if (type === 'ellipse') return { left: num(a.cx) - num(a.rx), right: num(a.cx) + num(a.rx), top: num(a.cy) - num(a.ry), bottom: num(a.cy) + num(a.ry) };
  if (type === 'line') return { left: Math.min(num(a.x1), num(a.x2)), right: Math.max(num(a.x1), num(a.x2)), top: Math.min(num(a.y1), num(a.y2)), bottom: Math.max(num(a.y1), num(a.y2)) };
  if (type === 'polygon' || type === 'polyline') {
    const n = (a.points || '').trim().split(/[\s,]+/).map(Number);
    const xs = n.filter((_, i) => i % 2 === 0), ys = n.filter((_, i) => i % 2 === 1);
    if (!xs.length) return null;
    return { left: Math.min(...xs), right: Math.max(...xs), top: Math.min(...ys), bottom: Math.max(...ys) };
  }
  return null;
}
function union(boxes) {
  const b = boxes.filter(x => x && [x.left, x.right, x.top, x.bottom].every(Number.isFinite));
  if (!b.length) return null;
  return { left: Math.min(...b.map(x => x.left)), right: Math.max(...b.map(x => x.right)), top: Math.min(...b.map(x => x.top)), bottom: Math.max(...b.map(x => x.bottom)) };
}

/** SVG text -> { roots, errors, viewBox, loose, unsupported, transforms } */
export function parse(text) {
  const errors = [];
  const root = { p: '$root', children: [], els: [] };
  const stack = [root];
  let viewBox = null;
  const loose = [], unsupported = [], transforms = [];
  const body = String(text).replace(/<!--[\s\S]*?-->/g, '').replace(/<\?[\s\S]*?\?>/g, '');
  for (const m of body.matchAll(/<(\/?)([a-zA-Z][\w:-]*)([^>]*?)(\/?)>/g)) {
    const [, close, tag, rest, selfClose] = m;
    const a = attrs(rest);
    if (a.transform) transforms.push(tag);
    if (tag === 'svg' && !close) { viewBox = a.viewBox || a.viewbox || null; continue; }
    if (tag === 'g') {
      if (close) {
        const n = stack.pop();
        if (!n || n === root) { errors.push('unbalanced </g>'); stack.push(root); continue; }
        if (n.transparent) { const parent = stack[stack.length - 1]; parent.children.push(...n.children); parent.els.push(...n.els); }
        continue;
      }
      const node = a['data-p'] ? { p: a['data-p'], v: a['data-v'], children: [], els: [], attrs: a } : { transparent: true, children: [], els: [] };
      if (!node.transparent) stack[stack.length - 1].children.push(node);
      if (!selfClose) stack.push(node);
      continue;
    }
    if (DRAWN.has(tag) && !close) {
      const el = { type: tag, attrs: a, box: elBox(tag, a) };
      if (!ALLOWED.has(tag)) unsupported.push(tag);
      const cur = stack[stack.length - 1];
      if (cur === root) loose.push(tag);
      cur.els.push(el);
    }
  }
  if (stack.length > 1) errors.push('unclosed <g>');
  const box = (n) => { n.children.forEach(box); n.box = union([...n.els.map(e => e.box), ...n.children.map(c => c.box)]); return n.box; };
  root.children.forEach(box);
  return { roots: root.children, errors, viewBox, loose, unsupported, transforms };
}

// ---------- lint ----------

export function lint(parsed) {
  const out = [];
  const vb = (parsed.viewBox || '').trim().split(/[\s,]+/).map(Number);
  if (vb.join(' ') !== `0 0 ${CANVAS.w} ${CANVAS.h}`) out.push(`viewBox must be "0 0 ${CANVAS.w} ${CANVAS.h}", got "${parsed.viewBox ?? '-'}"`);
  if (parsed.unsupported.length) out.push(`not allowed: ${[...new Set(parsed.unsupported)].join(', ')}`);
  if (parsed.transforms.length) out.push('transform is not allowed');
  if (parsed.loose.length) out.push(`${parsed.loose.length} shape(s) outside a pattern group`);
  const walk = (n) => {
    const b = n.box;
    if (b && (b.left < -1 || b.top < -1 || b.right > CANVAS.w + 1 || b.bottom > CANVAS.h + 1)) out.push(`${n.p} is outside the canvas`);
    for (const e of n.els) if (!e.attrs.fill && e.type !== 'line') out.push(`${n.p}: ${e.type} has no fill`);
    n.children.forEach(walk);
  };
  parsed.roots.forEach(walk);
  return { ok: out.length === 0, failures: out };
}

// ---------- leaf form ----------

export function checkLeaf(node, def) {
  const m = [];
  if (node.els.length !== 1) return [`a leaf needs exactly one shape, got ${node.els.length}`];
  const e = node.els[0];
  const want = { rect: 'rect', circle: 'circle', line: 'line', triangle: 'polygon' }[def.primitive];
  if (e.type !== want) m.push(`expected ${def.primitive}, got ${e.type}`);
  if (def.primitive === 'triangle') {
    const n = (e.attrs.points || '').trim().split(/[\s,]+/).filter(Boolean);
    if (n.length !== 6) m.push(`a triangle needs 3 points, got ${n.length / 2}`);
  }
  if (!e.box) m.push('shape has no size');
  return m;
}

// ---------- asserts ----------
// Forms:  A.prop OP B.prop [* k]   |  all A.prop OP B.prop  |  A inside B  |  all A inside B
//         no-overlap A B ...      |  count(A,B) OP n
// A, B: a child part name or "self". prop: top bottom left right width height cx cy.

const PROPS = {
  top: b => b.top, bottom: b => b.bottom, left: b => b.left, right: b => b.right,
  width: b => b.right - b.left, height: b => b.bottom - b.top,
  cx: b => (b.left + b.right) / 2, cy: b => (b.top + b.bottom) / 2,
};
const cmp = (a, op, b) => ({ '==': Math.abs(a - b) <= TOL, '>=': a >= b - TOL, '<=': a <= b + TOL, '>': a > b, '<': a < b })[op];

function pick(node, name) { return name === 'self' ? [node] : node.children.filter(c => c.p === name); }

function operand(node, s) {
  const m = s.trim().match(/^([\w-]+)\.(\w+)(?:\s*\*\s*([\d.]+))?$/);
  if (!m) { const n = Number(s); if (Number.isFinite(n)) return { value: n }; throw new Error(`bad operand "${s}"`); }
  const [, name, prop, k] = m;
  const t = pick(node, name)[0];
  if (!t) return { missing: name };
  if (!PROPS[prop]) throw new Error(`bad property "${prop}"`);
  return { value: PROPS[prop](t.box) * (k ? Number(k) : 1) };
}

function inside(a, b) { return a.left >= b.left - 2 && a.right <= b.right + 2 && a.top >= b.top - 2 && a.bottom <= b.bottom + 2; }
function overlap(a, b) { const w = Math.min(a.right, b.right) - Math.max(a.left, b.left); const h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top); return w > 2 && h > 2; }

export function assert(node, list) {
  return list.map((raw) => {
    const s = String(raw).trim();
    try {
      let m;
      if ((m = s.match(/^no-overlap\s+(.+)$/))) {
        const items = m[1].split(/\s+/).flatMap(n => pick(node, n));
        for (let i = 0; i < items.length; i++) for (let j = i + 1; j < items.length; j++) {
          if (overlap(items[i].box, items[j].box)) return { assert: s, ok: false, detail: `${items[i].p} and ${items[j].p} overlap` };
        }
        return { assert: s, ok: true };
      }
      if ((m = s.match(/^count\(([^)]*)\)\s*(==|>=|<=|>|<)\s*(\d+)$/))) {
        const n = m[1].split(',').flatMap(x => pick(node, x.trim())).length;
        const ok = { '==': n === +m[3], '>=': n >= +m[3], '<=': n <= +m[3], '>': n > +m[3], '<': n < +m[3] }[m[2]];
        return { assert: s, ok, detail: ok ? '' : `count is ${n}` };
      }
      if ((m = s.match(/^(all\s+)?([\w-]+)\s+inside\s+([\w-]+)$/))) {
        const as = pick(node, m[2]), b = pick(node, m[3])[0];
        if (!as.length || !b) return { assert: s, ok: true, skipped: true };
        const bad = as.filter(a => !inside(a.box, b.box));
        return { assert: s, ok: bad.length === 0, detail: bad.length ? `${bad.length} not inside` : '' };
      }
      if ((m = s.match(/^(all\s+)?(.+?)\s*(==|>=|<=|>|<)\s*(.+)$/))) {
        const [, all, left, op, right] = m;
        if (all) {
          const lm = left.trim().match(/^([\w-]+)\.(\w+)$/);
          const targets = pick(node, lm[1]);
          const r = operand(node, right);
          if (r.missing || !targets.length) return { assert: s, ok: true, skipped: true };
          const bad = targets.filter(t => !cmp(PROPS[lm[2]](t.box), op, r.value));
          return { assert: s, ok: bad.length === 0, detail: bad.length ? `${bad.length} of ${targets.length} fail` : '' };
        }
        const l = operand(node, left), r = operand(node, right);
        if (l.missing || r.missing) return { assert: s, ok: true, skipped: true };
        const ok = cmp(l.value, op, r.value);
        return { assert: s, ok, detail: ok ? '' : `${Math.round(l.value)} vs ${Math.round(r.value)}` };
      }
      return { assert: s, ok: false, detail: 'cannot read this assert' };
    } catch (e) {
      return { assert: s, ok: false, detail: e.message };
    }
  });
}

// ---------- params ----------

const NAMED = {
  red: [220, 40, 40], blue: [40, 80, 220], green: [40, 160, 60], yellow: [240, 210, 40], orange: [240, 140, 30],
  brown: [130, 80, 40], black: [0, 0, 0], white: [255, 255, 255], gray: [128, 128, 128], grey: [128, 128, 128],
};
function toRgb(c) {
  if (!c) return null;
  c = c.trim().toLowerCase();
  if (NAMED[c]) return NAMED[c];
  let m = c.match(/^#([0-9a-f]{3})$/);
  if (m) return [...m[1]].map(h => parseInt(h + h, 16));
  m = c.match(/^#([0-9a-f]{6})$/);
  if (m) return [0, 2, 4].map(i => parseInt(m[1].slice(i, i + 2), 16));
  m = c.match(/^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$/);
  if (m) return m.slice(1, 4).map(Number);
  return null;
}
/** Nearest color name, so "#c62828" reads as "red". */
export function colorName(c) {
  const rgb = toRgb(c);
  if (!rgb) return c;
  let best = null, bd = Infinity;
  for (const [n, v] of Object.entries(NAMED)) { const d = v.reduce((s, x, i) => s + (x - rgb[i]) ** 2, 0); if (d < bd) { bd = d; best = n; } }
  return best === 'grey' ? 'gray' : best;
}

export function readParam(node, key, def) {
  const part = node.children.find(c => c.p === def.part);
  const el = part?.els[0];
  if (!el) return undefined;
  const v = el.attrs[def.attr || 'fill'];
  return v === undefined ? undefined : colorName(v);
}
