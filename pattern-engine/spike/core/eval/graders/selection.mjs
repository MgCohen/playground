// Selection grader. Compares what the agent says it selected (trace.json) with the case.
import { diffTrees } from '../../lib/tree.mjs';

export function gradeSelection(trace, expect) {
  if (!trace) return { ok: false, failures: ['no trace.json'] };
  const failures = [];
  if (expect.outcome === 'no-fit') {
    if (trace.outcome !== 'no-fit') failures.push(`expected outcome "no-fit", got "${trace.outcome}"`);
    const nearest = [].concat(expect.nearest || []);
    if (nearest.length && !nearest.includes(trace.nearest)) failures.push(`expected nearest ${nearest.join(' or ')}, got "${trace.nearest ?? '-'}"`);
    if (!Array.isArray(trace.deltas) || !trace.deltas.length) failures.push('no deltas listed');
  } else {
    if (trace.outcome !== 'match') failures.push(`expected outcome "match", got "${trace.outcome}"`);
    failures.push(...diffTrees(expect.tree, trace.tree));
  }
  return { ok: failures.length === 0, failures };
}

/** Composition grader: the tree that was really built, from the output file. */
export function gradeBuilt(built, expect) {
  if (expect.outcome === 'no-fit') return { ok: true, failures: [], skipped: true };
  const failures = diffTrees(expect.tree, built);
  return { ok: failures.length === 0, failures };
}
