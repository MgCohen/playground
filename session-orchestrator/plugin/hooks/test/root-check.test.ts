// Tests of the recurring check of a root orchestrator (P1-F12): the question lines of `orch decisions` and the prompt.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { checkPrompt, questionLines } from '../check.ts'

const LIST = [
  'toolu_A [medium] K: Architecture review: Do you approve?',
  '  options: Approve, Refuse',
  'toolu_B [high] Orch plugin: How do I update orch?',
].join('\n')

test('only question lines count, not option lines', () => {
  assert.deepEqual(questionLines(LIST).map(q => q.id), ['toolu_A', 'toolu_B'])
  assert.deepEqual(questionLines('No open questions.'), [])
})

test('the prompt names each question and the template to use', () => {
  const text = checkPrompt(['toolu_B [high] Orch plugin: How do I update orch?'])
  assert.match(text, /1 open question/)
  assert.match(text, /toolu_B/)
  assert.match(text, /orch:orch-reply/)
})
