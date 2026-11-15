import { query } from '@anthropic-ai/claude-agent-sdk'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'node:fs'

const store = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/store/sessions'
const cwd = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/spikes'
const id = randomUUID()
mkdirSync(`${store}/${id}`, { recursive: true })
writeFileSync(`${store}/${id}/enabled`, '')

async function turn(label, prompt, extra) {
  const types = {}
  let result
  const asked = []
  for await (const m of query({
    prompt,
    options: {
      cwd,
      settingSources: ['user'],
      permissionMode: 'default',
      canUseTool: async (tool, input) => {
        asked.push(tool)
        return { behavior: 'allow', updatedInput: input }
      },
      ...extra,
    },
  })) {
    const k = m.subtype ? `${m.type}/${m.subtype}` : m.type
    types[k] = (types[k] ?? 0) + 1
    if (m.type === 'result') result = m
    if (m.type === 'system' && m.subtype === 'init') console.log(label, 'init session', m.session_id, 'plugins', JSON.stringify(m.plugins ?? []), 'apiKeySource', m.apiKeySource)
  }
  console.log(label, 'events', JSON.stringify(types))
  console.log(label, 'canUseTool asked for', JSON.stringify(asked))
  console.log(label, 'result', result?.subtype, JSON.stringify(result?.result))
}

await turn('T1', 'Spike S6. Keep a list of words. Add APPLE (SDK turn 1). Reply only with the list.', { sessionId: id })
await turn('T2', 'Run the Bash command `echo hi` once. Then add BANANA (SDK turn 2, resume). Reply only with the list.', { resume: id })
console.log('id', id)
for (const f of ['lease.json', 'log.txt', 'status.json']) {
  const p = `${store}/${id}/${f}`
  console.log(f, existsSync(p) ? readFileSync(p, 'utf8').slice(0, 300) : 'MISSING')
}
