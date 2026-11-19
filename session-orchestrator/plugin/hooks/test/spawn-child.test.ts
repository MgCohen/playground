// Tests of the start switch of spawn_child (R-F8). orch new --app-start decides where the child starts: a parent in the
// desktop app gets the app call, any other parent gets the id of a child that the supervisor starts.
// The C# side: AppStartTests.
import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { S } from '../state.ts'
import { appStart, spawnChild } from '../tools.ts'

type Fake = { $: any; orchCalls: string[][] }

function fakeEngine(stdout: string): Fake {
  const orchCalls: string[][] = []
  const $ = {
    fs: { exists: async () => false, read: async () => '', write: async () => undefined, append: async () => undefined },
    clock: { now: async () => Date.parse('2026-11-18T12:00:00Z') },
    env: { get: async () => undefined },
    process: {
      run: async (argv: string[]) => {
        orchCalls.push(argv)
        return { exitCode: 0, stdout }
      },
    },
  }
  return { $, orchCalls }
}

beforeEach(() => {
  Object.assign(S, { id: 'p1', dir: 'store/sessions/p1', store: 'store', holder: 'live', cwd: 'C:/repo' })
})

test('the output of an app start gives the start file and the app call', () => {
  const start = appStart('app-start C:/tmp/orch-starts/start-1.json\nStart the child in the desktop app now: call x.\nDo not change the prompt.')
  assert.deepEqual(start, { file: 'C:/tmp/orch-starts/start-1.json', instruction: 'Start the child in the desktop app now: call x.\nDo not change the prompt.' })
})

test('the output of a supervisor start is no app start', () => {
  assert.equal(appStart('c0ffee00-0000-0000-0000-000000000000'), undefined)
})

test('spawn_child asks for the app start by default and gives the app call to the parent', async () => {
  const f = fakeEngine('app-start C:/tmp/s.json\nStart the child in the desktop app now.')
  const result = await spawnChild(f.$, { title: 'T', goal: 'G' })
  assert.ok(f.orchCalls[0].includes('--app-start'))
  assert.deepEqual(result, { result: 'Start the child in the desktop app now.' })
})

test('spawn_child with inApp false leaves the start to the supervisor', async () => {
  const f = fakeEngine('c0ffee00-0000-0000-0000-000000000000')
  const result = await spawnChild(f.$, { title: 'T', goal: 'G', inApp: false })
  assert.ok(!f.orchCalls[0].includes('--app-start'))
  assert.match(result.result, /^Started child "T" \(c0ffee00-/)
})
