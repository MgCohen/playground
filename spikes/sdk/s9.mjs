import { query } from '@anthropic-ai/claude-agent-sdk'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync, readFileSync, existsSync, readdirSync } from 'node:fs'

const store = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/store/sessions'
const cwd = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/spikes'
const count = Number(process.argv[2] ?? 6)

async function one(n) {
  const id = randomUUID()
  mkdirSync(`${store}/${id}`, { recursive: true })
  writeFileSync(`${store}/${id}/enabled`, '')
  const started = Date.now()
  let result
  for await (const m of query({ prompt: `Spike S9 worker ${n}. Reply only: worker ${n} ok`, options: { cwd, settingSources: ['user'], sessionId: id } }))
    if (m.type === 'result') result = m
  const dir = `${store}/${id}`
  const lease = existsSync(`${dir}/lease.json`) ? JSON.parse(readFileSync(`${dir}/lease.json`, 'utf8')).holder : 'MISSING'
  const status = existsSync(`${dir}/status.json`) ? JSON.parse(readFileSync(`${dir}/status.json`, 'utf8')).state : 'MISSING'
  const rows = existsSync(`${dir}/mirror`) ? readdirSync(`${dir}/mirror`).length : 0
  return { n, id: id.slice(0, 8), seconds: Math.round((Date.now() - started) / 1000), result: result?.subtype, text: result?.result, lease, status, rows }
}

const t = Date.now()
const all = await Promise.all(Array.from({ length: count }, (_, i) => one(i + 1)))
console.table(all)
console.log('total seconds', Math.round((Date.now() - t) / 1000))
