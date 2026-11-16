import { createServer, type IncomingMessage, type Server } from 'node:http'
import type { Supervisor } from './supervisor.ts'

async function body(request: IncomingMessage): Promise<any> {
  let text = ''
  for await (const chunk of request) text += chunk
  return text === '' ? {} : JSON.parse(text)
}

export function sessionView(supervisor: Supervisor, id: string, now: number) {
  const store = supervisor.store
  const meta = store.meta(id)
  return {
    id,
    title: meta.title,
    parent: meta.parent,
    holder: store.lease(id).holder,
    live: store.isLive(id, now),
    running: supervisor.running.has(id),
    status: store.readJson<any>(store.path(id, 'status.json'))?.state,
    report: store.report(id),
    blocked: store.readJson<any>(store.path(id, 'blocked.json'))?.reason ?? null,
    openDecisions: store.openDecisions(id).length,
  }
}

export function startHttp(supervisor: Supervisor, port: number): Server {
  const store = supervisor.store
  const server = createServer(async (request, response) => {
    const reply = (code: number, value: unknown) => {
      response.writeHead(code, { 'content-type': 'application/json' })
      response.end(JSON.stringify(value, null, 2))
    }
    try {
      const url = new URL(request.url ?? '/', 'http://localhost')
      const parts = url.pathname.split('/').filter(Boolean)
      const now = Date.now()
      if (request.method === 'GET' && url.pathname === '/api/sessions') return reply(200, store.sessions().map(id => sessionView(supervisor, id, now)))
      if (request.method === 'GET' && url.pathname === '/api/decisions') return reply(200, store.sessions().flatMap(id => store.openDecisions(id)))
      if (request.method === 'POST' && url.pathname === '/api/sessions') {
        const input = await body(request)
        if (!input.brief || !input.cwd) return reply(400, { error: 'brief and cwd are necessary' })
        const id = store.create({ title: input.title, cwd: input.cwd, parent: input.parent, createdBy: 'supervisor' }, input.brief)
        store.event('brief', 'user', `session:${id}`, { title: input.title, cwd: input.cwd, text: String(input.brief).slice(0, 2000) }, {}, id)
        supervisor.tick()
        return reply(201, { id })
      }
      if (request.method === 'POST' && parts[0] === 'api' && parts[1] === 'sessions' && parts[3] === 'send' && parts[2]) {
        const input = await body(request)
        const name = store.send(parts[2], String(input.text ?? ''))
        store.event('message', 'user', `session:${parts[2]}`, { text: String(input.text ?? '') }, { inbox: name }, parts[2])
        supervisor.tick()
        return reply(202, { inbox: name })
      }
      if (request.method === 'GET' && url.pathname === '/api/events') return reply(200, store.events())
      if (request.method === 'GET' && url.pathname === '/api/ledger') {
        const text = store.readText(`${store.root}/ledger.jsonl`) ?? ''
        return reply(200, text.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line)))
      }
      if (request.method === 'POST' && parts[0] === 'api' && parts[1] === 'decisions' && parts[2] && parts[3] && parts[4] === 'review') {
        const input = await body(request)
        if (!['approve', 'change', 'refuse'].includes(input.verdict)) return reply(400, { error: 'verdict is approve, change or refuse' })
        const name = supervisor.review(parts[2], parts[3], input.verdict, String(input.text ?? ''))
        supervisor.tick()
        return reply(202, { inbox: name })
      }
      if (request.method === 'POST' && parts[0] === 'api' && parts[1] === 'decisions' && parts[2] && parts[3]) {
        const input = await body(request)
        store.answer(parts[2], parts[3], { answer: String(input.answer ?? ''), by: 'user' })
        store.event('answer', 'user', `session:${parts[2]}`, { text: String(input.answer ?? '') }, { decision: parts[3] }, parts[2])
        supervisor.tick()
        return reply(202, { answered: parts[3] })
      }
      return reply(404, { error: 'not found' })
    } catch (error) {
      return reply(500, { error: String(error) })
    }
  })
  server.listen(port, '127.0.0.1')
  return server
}
