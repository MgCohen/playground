import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { Store } from '../src/store.ts'
import { Supervisor, type TurnRequest } from '../src/supervisor.ts'

function setup(ok = true) {
  const store = new Store(mkdtempSync(join(tmpdir(), 'orch-')))
  const turns: TurnRequest[] = []
  const supervisor = new Supervisor(
    store,
    async request => {
      turns.push(request)
      return ok ? { ok: true, text: 'done', seconds: 0 } : { ok: false, error: 'boom', seconds: 0 }
    },
    { maxRuns: 2, maxFailures: 2 },
  )
  return { store, turns, supervisor }
}

function makeLive(store: Store, id: string) {
  store.writeJson(store.path(id, 'lease.json'), { holder: 'live', since: new Date().toISOString() })
  store.write(store.path(id, 'heartbeat.txt'), new Date().toISOString())
}

test('a new session starts with its brief as a new SDK session', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ title: 'root', cwd: 'C:/x', createdBy: 'supervisor' }, 'Do the work.')
  supervisor.tick()
  await supervisor.idle()
  assert.deepEqual(turns, [{ id, cwd: 'C:/x', prompt: 'Do the work.', isNew: true }])
  assert.equal(store.meta(id).started, true)
  assert.equal(store.pending(id).text, '')
})

test('the next turn of a started session is a resume, with all inbox files in order', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ title: 'root', cwd: 'C:/x', createdBy: 'supervisor' }, 'one')
  supervisor.tick()
  await supervisor.idle()
  store.write(store.path(id, 'inbox', '10.txt'), 'three')
  store.write(store.path(id, 'inbox', '2.txt'), 'two')
  supervisor.tick()
  await supervisor.idle()
  assert.equal(turns[1]?.isNew, false)
  assert.equal(turns[1]?.prompt, 'two\n\n---\n\nthree')
})

test('a live session gets no supervisor turn', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, 'hello')
  makeLive(store, id)
  supervisor.tick()
  await supervisor.idle()
  assert.equal(turns.length, 0)
})

test('a live lease with a stale heartbeat is released and the session runs', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, 'hello')
  store.writeJson(store.path(id, 'lease.json'), { holder: 'live', since: new Date(Date.now() - 60000).toISOString() })
  store.write(store.path(id, 'heartbeat.txt'), new Date(Date.now() - 60000).toISOString())
  supervisor.tick()
  await supervisor.idle()
  assert.equal(store.lease(id).holder, 'none')
  assert.equal(turns.length, 1)
})

test('an answer to a pending decision is sent once and marked delivered', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, 'start')
  supervisor.tick()
  await supervisor.idle()
  store.writeJson(store.path(id, 'decisions', 'd1.json'), { id: 'd1', session: id, kind: 'ask_user', question: 'Pick', risk: 'low', mode: 'pending', waitSeconds: 0, asked: '' })
  assert.equal(store.openDecisions(id).length, 1)
  store.answer(id, 'd1', { answer: 'LIME' })
  supervisor.tick()
  await supervisor.idle()
  supervisor.tick()
  await supervisor.idle()
  assert.equal(turns.length, 2)
  assert.equal(turns[1]?.prompt, 'Answer to decision d1 (Pick): LIME')
  assert.equal(store.isDelivered(id, 'd1'), true)
})

test('a done report of a child goes to the parent inbox once', async () => {
  const { store, turns, supervisor } = setup()
  const parent = store.create({ title: 'parent', cwd: 'C:/x', createdBy: 'supervisor' }, 'p')
  const child = store.create({ title: 'child', cwd: 'C:/x', parent, createdBy: 'spawn_child' }, 'c')
  supervisor.tick()
  await supervisor.idle()
  store.writeJson(store.path(child, 'report.json'), { state: 'done', summary: 'All good.', at: 't1' })
  supervisor.tick()
  await supervisor.idle()
  supervisor.tick()
  await supervisor.idle()
  const toParent = turns.filter(t => t.id === parent).map(t => t.prompt)
  assert.deepEqual(toParent, ['p', 'Report from child "child" (' + child + '): done. All good.'])
})

test('a failed turn keeps the inbox and stops after the failure limit', async () => {
  const { store, turns, supervisor } = setup(false)
  const id = store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, 'hello')
  for (let i = 0; i < 4; i++) {
    supervisor.tick()
    await supervisor.idle()
  }
  assert.equal(turns.length, 2)
  assert.equal(store.pending(id).text, 'hello')
  assert.equal(turns.every(t => t.isNew), true)
  assert.match(store.readJson<any>(store.path(id, 'blocked.json')).reason, /failed 2 times/)
})

test('no more than maxRuns turns run at one time', async () => {
  const { store, turns, supervisor } = setup()
  for (let i = 0; i < 3; i++) store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, `s${i}`)
  supervisor.tick()
  assert.equal(supervisor.running.size, 2)
  await supervisor.idle()
  supervisor.tick()
  await supervisor.idle()
  assert.equal(turns.length, 3)
})

test('a new live lease with an old heartbeat counts as live', async () => {
  const { store, turns, supervisor } = setup()
  const id = store.create({ cwd: 'C:/x', createdBy: 'supervisor' }, 'hello')
  store.writeJson(store.path(id, 'lease.json'), { holder: 'live', since: new Date().toISOString() })
  store.write(store.path(id, 'heartbeat.txt'), new Date(Date.now() - 60000).toISOString())
  supervisor.tick()
  await supervisor.idle()
  assert.equal(store.lease(id).holder, 'live')
  assert.equal(turns.length, 0)
})
