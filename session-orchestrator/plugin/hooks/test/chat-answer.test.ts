// Tests of the chat answer path with a fake engine. Run: node --test plugin/hooks/test/
// orch chat-answer (C#) closes the question only when exactly one is open, and acks it (no echo):
// StreamTests.A_chat_line_answers_only_when_one_decision_is_open_and_the_answer_has_an_ack.
import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { S } from '../state.ts'
import { answerFromChat, chatAnswer } from '../tools.ts'

type Fake = { $: any; orchCalls: string[][] }

function fakeEngine(): Fake {
  const orchCalls: string[][] = []
  const files: Record<string, string> = {}
  const $ = {
    fs: { exists: async (p: string) => p in files, read: async (p: string) => files[p], write: async (p: string, t: unknown) => void (files[p] = String(t)) },
    clock: { now: async () => Date.parse('2026-11-17T12:00:00Z') },
    env: { get: async () => undefined },
    process: {
      run: async (argv: string[]) => {
        orchCalls.push(argv)
        return { exitCode: 0, stdout: 'Answered d1 from the chat.' }
      },
    },
  }
  return { $, orchCalls }
}

function chat(text: string): any {
  return { door: 'prompt', origin: { kind: 'composer' }, message: { role: 'user', content: [{ type: 'text', text }] } }
}

let f: Fake

beforeEach(() => {
  Object.assign(S, { id: 's1', dir: 'store/sessions/s1', store: 'store', holder: 'live' })
  f = fakeEngine()
})

test('a chat message without answer: leaves the question open', async () => {
  await answerFromChat(f.$, chat('Why is the plugin sending my message back to this chat?'))
  assert.deepEqual(f.orchCalls, [])
})

test('a chat message with answer: goes to orch chat-answer without the prefix', async () => {
  await answerFromChat(f.$, chat('Answer: use the blue option'))
  assert.deepEqual(f.orchCalls, [['orch', 'chat-answer', 's1', 'use the blue option']])
})

test('a system-reminder block is not text of the user', async () => {
  await answerFromChat(f.$, chat('<system-reminder>\nThis conversation is now continuing in the app.\nanswer: no\n</system-reminder>\n\nCan you add those details?'))
  assert.deepEqual(f.orchCalls, [])
  await answerFromChat(f.$, chat('<system-reminder>\nApp guidance.\n</system-reminder>\nanswer: yes'))
  assert.deepEqual(f.orchCalls, [['orch', 'chat-answer', 's1', 'yes']])
})

test('a message that is not from the composer is not an answer', async () => {
  await answerFromChat(f.$, { door: 'prompt', origin: { kind: 'plugin' }, message: { role: 'user', content: [{ type: 'text', text: 'answer: yes' }] } })
  assert.deepEqual(f.orchCalls, [])
})

test('chatAnswer needs the prefix at the start and a text after it', () => {
  assert.equal(chatAnswer('answer: yes'), 'yes')
  assert.equal(chatAnswer('  ANSWER:  2: yes  '), '2: yes')
  assert.equal(chatAnswer('My answer: yes'), undefined)
  assert.equal(chatAnswer('answer:'), undefined)
  assert.equal(chatAnswer('Can you explain?'), undefined)
})
