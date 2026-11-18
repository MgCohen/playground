// The tests of the store backend of the plugin hooks. Run: node --test plugin/hooks/test/
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { S } from '../state.ts'
import { StoreConflict, createEnvelope, log, readSession, writeSession } from '../backend.ts'
import { post } from '../stream.ts'

const HOOKS = join(dirname(fileURLToPath(import.meta.url)), '..')

// Store IO in a hook: the file API of the engine ($.fs) or a node file module.
const STORE_IO = /\$\.fs\b|\bfs\s*:|from\s+['"](node:)?fs(\/promises)?['"]|require\(\s*['"](node:)?fs/

function fake(files: Record<string, string> = {}, ms = Date.parse('2026-11-18T10:00:00.000Z')): any {
  return {
    files,
    fs: { exists: async (p: string) => p in files, read: async (p: string) => files[p], write: async (p: string, t: unknown) => void (files[p] = String(t)) },
    clock: { now: async () => ms },
    env: { get: async () => undefined },
  }
}

function reset(): void {
  Object.assign(S, { id: 's1', dir: 'store/sessions/s1', store: 'store', holder: 'live', lastMs: 0 })
}

test('only backend.ts has store IO in the hook modules', () => {
  const offenders = readdirSync(HOOKS)
    .filter(name => name.endsWith('.ts') && name !== 'backend.ts')
    .filter(name => STORE_IO.test(readFileSync(join(HOOKS, name), 'utf8')))
  assert.deepEqual(offenders, [])
  assert.ok(STORE_IO.test(readFileSync(join(HOOKS, 'backend.ts'), 'utf8')), 'the check finds the IO of backend.ts')
})

test('a second create of an envelope id fails and keeps the first envelope', async () => {
  reset()
  const $ = fake()
  const envelope = { id: 'e1', body: 'bodies/2026-11-18/e1.json', kind: 'update' }
  await createEnvelope($, '2026-11-18', 'e1', { text: 'first' }, envelope)
  await assert.rejects(createEnvelope($, '2026-11-18', 'e1', { text: 'second' }, envelope), StoreConflict)
  assert.equal(JSON.parse($.files['store/bodies/2026-11-18/e1.json']).text, 'first')
})

test('a save of an old version fails, a save of the current version passes', async () => {
  reset()
  const $ = fake({ 'store/sessions/s1/meta.json': JSON.stringify({ version: 3, title: 'a' }) })
  await assert.rejects(writeSession($, 'meta.json', { version: 3, title: 'b' }, S.dir, 2), StoreConflict)
  assert.equal((await readSession($, 'meta.json')).title, 'a')
  await writeSession($, 'meta.json', { version: 4, title: 'c' }, S.dir, 3)
  assert.equal((await readSession($, 'meta.json')).title, 'c')
  await writeSession($, 'meta.json', { title: 'd' })
  assert.equal((await readSession($, 'meta.json')).title, 'd', 'with no expected version the save replaces the file')
})

test('a session file that is not JSON reads as its text, and a missing file as undefined', async () => {
  reset()
  const $ = fake({ 'store/sessions/s1/turn.json': 'not json' })
  assert.deepEqual(await readSession($, 'turn.json'), { text: 'not json' })
  assert.equal(await readSession($, 'lease.json'), undefined)
})

test('post writes the body and the envelope at the same paths as before', async () => {
  reset()
  const $ = fake()
  const id = await post($, 'session.output', 'session:s1', 'user', { text: 'hello' })
  const envelope = JSON.parse($.files[`store/events/2026-11-18/${id}.json`])
  assert.equal(envelope.body, `bodies/2026-11-18/${id}.json`)
  assert.deepEqual(JSON.parse($.files[`store/${envelope.body}`]), { text: 'hello' })
  assert.equal(envelope.subject, 'hello')
})

test('the log appends a line and keeps the last part of a long log', async () => {
  reset()
  const path = 'store/sessions/s1/log-live.txt'
  const $ = fake({ [path]: 'x'.repeat(200001) })
  await log($, 'one')
  assert.equal($.files[path].length, 100000 + '2026-11-18T10:00:00.000Z one\n'.length)
  assert.ok($.files[path].endsWith('2026-11-18T10:00:00.000Z one\n'))
})
