import type { Register } from 'claude-code'

const ROOT = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/store/sessions'
const TOOL = 'mcp__orch-spike__ask_user'

const S: { dir?: string; cursor: number; draining: boolean } = { cursor: 0, draining: false }

async function now($: any): Promise<string> {
  return new Date(await $.clock.now()).toISOString()
}

async function log($: any, line: string): Promise<void> {
  if (!S.dir) return
  const path = `${S.dir}/log.txt`
  const old = (await $.fs.exists(path)) ? await $.fs.read(path) : ''
  await $.fs.write(path, `${old}${await now($)} ${line}
`)
}

async function drainInbox($: any): Promise<void> {
  if (!S.dir || S.draining) return
  S.draining = true
  try {
    if (!(await $.fs.exists(`${S.dir}/inbox`))) return
    const entries = await $.fs.list(`${S.dir}/inbox`)
    const seqs = entries
      .map((x: any) => Number(String(x.name).replace(/\.txt$/, '')))
      .filter((n: number) => Number.isFinite(n) && n > S.cursor)
      .sort((a: number, b: number) => a - b)
    for (const seq of seqs) {
      const text = await $.fs.read(`${S.dir}/inbox/${seq}.txt`)
      S.cursor = seq
      await $.fs.write(`${S.dir}/cursor.txt`, String(S.cursor))
      await log($, `inbox ${seq} submitted`)
      void $.prompt.submit({ text })
    }
  } finally {
    S.draining = false
  }
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    const id = await $.session.id()
    const candidate = `${ROOT}/${id}`
    if (!(await $.fs.exists(`${candidate}/enabled`))) return result
    S.dir = candidate
    S.cursor = (await $.fs.exists(`${S.dir}/cursor.txt`)) ? Number(await $.fs.read(`${S.dir}/cursor.txt`)) || 0 : 0
    const surfaces = await $.session.surfaces().catch(() => [])
    await $.fs.write(`${S.dir}/lease.json`, JSON.stringify({ holder: 'live', since: await now($), start: e, surfaces }, null, 2))
    await log($, `session.start ${JSON.stringify(e)}`)
    await $.tool.register({
      name: 'ask_user',
      description: 'Put one decision question in the user queue. Returns PENDING at once; end your turn; the answer arrives later as a new message.',
      inputSchema: {
        type: 'object',
        properties: {
          question: { type: 'string' },
          options: { type: 'array', items: { type: 'string' } },
        },
        required: ['question'],
      },
      isDeferred: false,
    })
    $.clock.every(3000, async () => {
      if (!S.dir) return
      await $.fs.write(`${S.dir}/heartbeat.txt`, await now($))
      await drainInbox($)
    })
    return result
  })

  on('turn.start', async ($, e, next) => {
    if (S.dir && e.agentId === undefined) await $.fs.write(`${S.dir}/status.json`, JSON.stringify({ state: 'running', turnId: e.turnId, at: await now($) }))
    return next(e)
  })

  on('turn.complete', async ($, e, next) => {
    const result = await next(e)
    if (S.dir && (e as any).agentId === undefined) await $.fs.write(`${S.dir}/status.json`, JSON.stringify({ state: 'idle', turnId: (e as any).turnId, at: await now($) }))
    return result
  })

  on('session.append', async ($, e, next) => {
    if (S.dir && e.agentId === undefined) {
      const row = { door: e.door, origin: e.origin, message: JSON.stringify(e.message).slice(0, 1500) }
      await $.fs.write(`${S.dir}/mirror/${await $.clock.now()}-${e.uuid}.json`, JSON.stringify(row))
    }
    return next(e)
  })

  on('session.receive', async ($, e, next) => {
    await log($, `session.receive ${JSON.stringify(e).slice(0, 400)}`)
    return next(e)
  })

  on('session.end', async ($, e, next) => {
    if (S.dir) await $.fs.write(`${S.dir}/lease.json`, JSON.stringify({ holder: 'none', since: await now($), end: e }, null, 2))
    return next(e)
  })

  on('tool.call', { tool: TOOL }, async ($, e: any) => {
    if (!S.dir) return { deny: 'orch-spike: this session is not enabled' }
    const id = e.tool_use_id
    await $.fs.write(`${S.dir}/decisions/${id}.json`, JSON.stringify({ question: e.question, options: e.options ?? [], asked: await now($) }, null, 2))
    await log($, `ask_user ${id} queued`)
    return { result: `PENDING: decision ${id} is in the user queue. End your turn now. The answer arrives as a new message that starts with "Answer to decision ${id}".` }
  })
}
