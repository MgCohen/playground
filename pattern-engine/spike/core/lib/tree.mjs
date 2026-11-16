// Pattern trees: { p, v?, params?, parts?: [tree] }. Leaf parts are not in a tree.

/** Compare an expected tree with an actual tree. Returns a list of differences (empty = match). */
export function diffTrees(expected, actual, at = '') {
  const here = at ? `${at} > ${expected.p}` : expected.p;
  if (!actual) return [`${here}: missing`];
  const diffs = [];
  if (expected.p !== actual.p) return [`${here}: expected "${expected.p}", got "${actual.p}"`];
  if (expected.v && expected.v !== actual.v) diffs.push(`${here}: expected variant "${expected.v}", got "${actual.v ?? '-'}"`);
  for (const [k, v] of Object.entries(expected.params || {})) {
    const got = actual.params?.[k];
    if (got === undefined || String(got).toLowerCase() !== String(v).toLowerCase()) diffs.push(`${here}: param ${k} expected "${v}", got "${got ?? '-'}"`);
  }
  if (Array.isArray(expected.parts)) {
    const a = actual.parts || [];
    if (a.length !== expected.parts.length) {
      diffs.push(`${here}: expected ${expected.parts.length} parts [${expected.parts.map(x => x.p).join(', ')}], got ${a.length} [${a.map(x => x.p).join(', ')}]`);
    } else {
      expected.parts.forEach((e, i) => diffs.push(...diffTrees(e, a[i], `${here}[${i}]`)));
    }
  }
  return diffs;
}

/** Short text form, for reports: street(row)[house, house(chimney){door_fill:red}] */
export function treeText(t) {
  if (!t) return '-';
  let s = t.p + (t.v ? `(${t.v})` : '');
  const ps = Object.entries(t.params || {});
  if (ps.length) s += '{' + ps.map(([k, v]) => `${k}:${v}`).join(',') + '}';
  if (t.parts?.length) s += '[' + t.parts.map(treeText).join(', ') + ']';
  return s;
}
