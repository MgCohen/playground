// The plugin hooks in a cloud worker (H5, C0-2): with ORCH_URL the plugin stays off and writes nothing. Run: node --test plugin/hooks/test/
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mayActivate, storeApiMode } from '../register.ts'

function fake(env: Record<string, string>, files: Record<string, string> = {}): any {
  return {
    files,
    fs: { exists: async (p: string) => p in files, read: async (p: string) => files[p], write: async (p: string, t: unknown) => void (files[p] = String(t)) },
    env: { get: async (name: string) => env[name] },
  }
}

test('with ORCH_URL the plugin does not activate and writes nothing', async () => {
  const $ = fake({ ORCH_URL: 'https://orch.example.com', ORCH_STORE: 'store' }, { 'store/version.json': '{"version":3}' })
  assert.equal(await storeApiMode($), true)
  assert.equal(await mayActivate($), false)
  assert.deepEqual(Object.keys($.files), ['store/version.json'])
})

test('with no ORCH_URL the plugin activates on a store of its version', async () => {
  assert.equal(await mayActivate(fake({ ORCH_STORE: 'store' }, { 'store/version.json': '{"version":3}' })), true)
  assert.equal(await mayActivate(fake({ ORCH_STORE: 'store' }, { 'store/version.json': '{"version":9}' })), false)
})

test('with a notion: ORCH_STORE the plugin does not activate and writes nothing (N1)', async () => {
  const $ = fake({ ORCH_STORE: 'notion:42391e2457868137aed0d0f5804daece' })
  assert.equal(await storeApiMode($), true)
  assert.equal(await mayActivate($), false)
  assert.deepEqual(Object.keys($.files), [])
})
