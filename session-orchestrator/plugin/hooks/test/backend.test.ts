// The tests of the store backend of the plugin hooks. Run: node --test plugin/hooks/test/
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { S } from '../state.ts'
import { StoreConflict, createEnvelope, log, readSession, readSessionVersioned, readTemplate, versionOf, writeSession } from '../backend.ts'
import { post } from '../stream.ts'

// Known versions of three texts. VersionRuleTests (C#) checks the same values.
const VERSIONS: Record<string, bigint> = { '': -3750763034362895579n, '{"title":"a"}': 7837972144070839554n, 'é': 775207407765167617n }

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
  Object.assign(S, { home: 'home', id: 's1', dir: 'store/sessions/s1', store: 'store', holder: 'live', lastMs: 0 })
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
  const $ = fake({ 'store/sessions/s1/meta.json': JSON.stringify({ title: 'a' }) })
  const read = await readSessionVersioned($, 'meta.json')
  assert.equal(read.version, versionOf(JSON.stringify({ title: 'a' })))
  await writeSession($, 'meta.json', { title: 'b' }, S.dir, read.version)
  await assert.rejects(writeSession($, 'meta.json', { title: 'c' }, S.dir, read.version), StoreConflict)
  assert.equal((await readSession($, 'meta.json')).title, 'b')
  await writeSession($, 'meta.json', { title: 'd' })
  assert.equal((await readSession($, 'meta.json')).title, 'd', 'with no expected version the save replaces the file')
})

// The same values are in VersionRuleTests (C#): both sides compute one version for one text.
test('the version is the FNV-1a hash of the text, as in the C# store', () => {
  assert.equal(versionOf(undefined), 0n)
  assert.equal(versionOf(''), VERSIONS[''])
  assert.equal(versionOf('{"title":"a"}'), VERSIONS['{"title":"a"}'])
  assert.equal(versionOf('é'), VERSIONS['é'])
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

test('a template reads from the store templates root first, else from the core templates', async () => {
  reset()
  const $ = fake({
    'home/templates/plan/meta.json': JSON.stringify({ from: 'core' }),
    'home/templates/note/meta.json': JSON.stringify({ from: 'core' }),
    'store/templates/plan/schema.json': '{}',
    'store/templates/plan/meta.json': JSON.stringify({ from: 'store' }),
  })
  assert.equal((await readTemplate($, 'plan', 'meta')).from, 'store', 'a store folder with a core name replaces the core template')
  assert.equal((await readTemplate($, 'note', 'meta')).from, 'core')
})
