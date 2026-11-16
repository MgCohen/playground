import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { Store } from './store.ts'
import { Supervisor } from './supervisor.ts'
import { sdkDocsChecker, sdkRunner } from './sdk.ts'
import { loadPolicy } from './policy.ts'
import { startHttp } from './http.ts'

const root = resolve(process.env.ORCH_STORE ?? fileURLToPath(new URL('../../store', import.meta.url)))
const port = Number(process.env.ORCH_PORT ?? 7777)
const store = new Store(root)
const policyPath = `${root}/policy.json`
const policy = loadPolicy(policyPath, fileURLToPath(new URL('../../config/policy.default.json', import.meta.url)))
store.writeJson(policyPath, policy)
const supervisor = new Supervisor(store, sdkRunner(root.replaceAll('\\', '/')), { maxRuns: 4, maxFailures: 3, maxChecks: 2, policy, checkDocs: sdkDocsChecker() })

startHttp(supervisor, port)
setInterval(() => supervisor.tick(), 3000)
store.log(`supervisor started: store ${root}, port ${port}, levels ${policy.levels.map(level => level.name).join(', ')}`)
console.log(`orch supervisor: store ${root}, http://127.0.0.1:${port}/api/sessions`)
