import { resolve } from 'node:path'
import { Store } from './store.ts'
import { Supervisor } from './supervisor.ts'
import { sdkRunner } from './sdk.ts'
import { startHttp } from './http.ts'

const root = resolve(process.env.ORCH_STORE ?? new URL('../../store', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))
const port = Number(process.env.ORCH_PORT ?? 7777)
const store = new Store(root)
const supervisor = new Supervisor(store, sdkRunner(root.replaceAll('\\', '/')), { maxRuns: 4, maxFailures: 3 })

startHttp(supervisor, port)
setInterval(() => supervisor.tick(), 3000)
store.log(`supervisor started: store ${root}, port ${port}`)
console.log(`orch supervisor: store ${root}, http://127.0.0.1:${port}/api/sessions`)
