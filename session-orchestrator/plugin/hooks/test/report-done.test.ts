// Tests of the done report check (C3-F4): the report tool refuses a done report while a question is open.
// It parses the output of `orch decisions`: the new text "No open questions." and the old text "No open decisions."
import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { S } from '../state.ts'
import { EARLY_DONE, NO_OPEN_QUESTIONS, report } from '../tools.ts'

function fakeEngine(stdout: string): any {
  return {
    fs: { exists: async () => false, read: async () => '', write: async () => undefined },
    clock: { now: async () => Date.parse('2026-11-18T12:00:00Z') },
    env: { get: async () => undefined },
    process: { run: async () => ({ exitCode: 0, stdout }) },
  }
}

beforeEach(() => {
  Object.assign(S, { id: 's1', dir: 'store/sessions/s1', store: 'store', holder: 'live' })
})

test('the new and the old "no open" texts both mean no open question', () => {
  assert.ok(NO_OPEN_QUESTIONS.test('No open questions.'))
  assert.ok(NO_OPEN_QUESTIONS.test('No open decisions.'))
  assert.ok(!NO_OPEN_QUESTIONS.test('toolu_1  s1  high  Which color?'))
})

test('a done report with an open question is refused', async () => {
  const result = await report(fakeEngine('toolu_1  s1  high  Which color?'), { state: 'done', summary: 'x' })
  assert.deepEqual(result, { result: EARLY_DONE })
})
