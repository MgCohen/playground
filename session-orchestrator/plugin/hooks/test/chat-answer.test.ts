// Tests of the chat answer path (F2) with a fake engine. Run: node --test plugin/hooks/test/
import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { S } from '../state.ts'
import { tick } from '../inbox.ts'
import { answerFromChat, chatAnswer } from '../tools.ts'

type Fake = { $: any; dir: string; submitted: string[]; orchCalls: string[][] }

// The fake orch answer writes the answer file, as the real CLI does.
function fakeEngine(dir: string): Fake {
  const submitted: string[] = []
  const orchCalls: string[][] = []
  let clock = Date.parse('2026-11-17T12:00:00Z')
  const $ = {
    fs: {
      exists: async (path: string) => existsSync(path),
      read: async (path: string) => readFileSync(path, 'utf8'),
      write: async (path: string, text: string) => {
        mkdirSync(dirname(path), { recursive: true })
        writeFileSync(path, text)
      },
      list: async (path: string) => readdirSync(path).map(name => ({ name })),
    },
    clock: { now: async () => (clock += 1000) },
    env: { get: async () => undefined },
    prompt: {
      submit: async ({ text }: { text: string }) => {
        submitted.push(text)
        return {}
      },
    },
    process: {
      run: async (argv: string[]) => {
        orchCalls.push(argv)
        if (argv[0] === 'orch' && argv[1] === 'answer') {
          writeFileSync(join(dir, 'answers', `${argv[2]}.json`), JSON.stringify({ value: argv[3], by: 'user' }))
          return { exitCode: 0, stdout: `Answered ${argv[2]}.` }
        }
        return { exitCode: 1, stderr: 'unknown command' }
      },
    },
  }
  return { $, dir, submitted, orchCalls }
}

function openDecision(dir: string, id: string, asked: string): void {
  writeFileSync(join(dir, 'decisions', `${id}.json`), JSON.stringify({ id, session: 's1', kind: 'ask_user', question: `Question ${id}?`, risk: 'high', mode: 'pending', waitSeconds: 0, asked }))
}

function chat(text: string): any {
  return { door: 'prompt', origin: { kind: 'composer' }, message: { role: 'user', content: [{ type: 'text', text }] } }
}

let f: Fake

beforeEach(() => {
  const dir = mkdtempSync(join(tmpdir(), 'orch-chat-'))
  for (const folder of ['decisions', 'answers', 'delivered', 'inbox']) mkdirSync(join(dir, folder))
  Object.assign(S, { id: 's1', dir, store: undefined, holder: 'live', cursor: '', busy: false, idle: true, dropped: '' })
  f = fakeEngine(dir)
  openDecision(dir, 'd1', '2026-11-17T10:00:00Z')
  openDecision(dir, 'd2', '2026-11-17T11:00:00Z')
})

test('a chat message without answer: leaves the decision open', async () => {
  await answerFromChat(f.$, chat('Why is the plugin sending my message back to this chat?'))
  assert.deepEqual(f.orchCalls, [])
  assert.equal(existsSync(join(f.dir, 'answers', 'd2.json')), false)
  assert.equal(existsSync(join(f.dir, 'delivered', 'd2.json')), false)
})

test('a chat message with answer: closes the newest open decision without the prefix', async () => {
  await answerFromChat(f.$, chat('Answer: use the blue option'))
  assert.deepEqual(f.orchCalls, [['orch', 'answer', 'd2', 'use the blue option']])
  assert.equal(existsSync(join(f.dir, 'answers', 'd1.json')), false)
})

test('a system-reminder block is not text of the user', async () => {
  await answerFromChat(f.$, chat('<system-reminder>\nThis conversation is now continuing in the app.\nanswer: no\n</system-reminder>\n\nCan you add those details?'))
  assert.deepEqual(f.orchCalls, [])
  await answerFromChat(f.$, chat('<system-reminder>\nApp guidance.\n</system-reminder>\nanswer: yes'))
  assert.deepEqual(f.orchCalls, [['orch', 'answer', 'd2', 'yes']])
})

test('a chat answer does not come back to the session as a message (no echo)', async () => {
  await answerFromChat(f.$, chat('answer: yes'))
  const delivered = JSON.parse(readFileSync(join(f.dir, 'delivered', 'd2.json'), 'utf8'))
  assert.equal(delivered.by, 'chat')
  await tick(f.$)
  assert.deepEqual(f.submitted, [])
})

test('an answer from elsewhere still arrives as a message', async () => {
  writeFileSync(join(f.dir, 'answers', 'd1.json'), JSON.stringify({ value: 'from the root', by: 'session:root' }))
  await tick(f.$)
  assert.equal(f.submitted.length, 1)
  assert.match(f.submitted[0], /^Answer to decision d1 .*from the root/)
})

test('chatAnswer needs the prefix at the start and a text after it', () => {
  assert.equal(chatAnswer('answer: yes'), 'yes')
  assert.equal(chatAnswer('  ANSWER:  2: yes  '), '2: yes')
  assert.equal(chatAnswer('My answer: yes'), undefined)
  assert.equal(chatAnswer('answer:'), undefined)
  assert.equal(chatAnswer('Can you explain?'), undefined)
})
