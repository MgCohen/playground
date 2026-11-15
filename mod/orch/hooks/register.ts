import type { Register } from 'claude-code'

const DEFAULT_STORE = 'C:/Users/matheus.cohen/Documents/Projects/claude-orchestrator/store'
const ASK_TOOL = 'mcp__orch__ask_user'
const REPORT_TOOL = 'mcp__orch__report'
const SPAWN_TOOL = 'mcp__orch__spawn_child'
const MIRROR_DOORS = ['prompt', 'response', 'tool-result']
const TICK_MS = 3000
const REDIRECT_WAIT_SECONDS = 300
const MAX_WAIT_SECONDS = 580
const ROW_CHARS = 4000

type Decision = {
  id: string
  session: string
  kind: 'ask_user' | 'AskUserQuestion'
  question?: string
  options?: string[]
  questions?: unknown
  risk: string
  defaultOption?: string
  mode: 'wait' | 'pending'
  waitSeconds: number
  asked: string
}

const S: { id?: string; dir?: string; store?: string; cwd?: string; holder: string; cursor: string; busy: boolean; idle: boolean; dropped: string } = {
  dropped: '',
  holder: 'live',
  cursor: '',
  busy: false,
  idle: true,
}

async function now($: any): Promise<string> {
  return new Date(await $.clock.now()).toISOString()
}

async function readText($: any, path: string): Promise<string | undefined> {
  return (await $.fs.exists(path)) ? String(await $.fs.read(path)) : undefined
}

async function readJson($: any, path: string): Promise<any> {
  const text = await readText($, path)
  if (text === undefined) return undefined
  try {
    return JSON.parse(text)
  } catch {
    return { text }
  }
}

async function writeJson($: any, path: string, value: unknown): Promise<void> {
  await $.fs.write(path, JSON.stringify(value, null, 2))
}

async function log($: any, line: string): Promise<void> {
  if (!S.dir) return
  const path = `${S.dir}/log-${S.holder}.txt`
  const old = (await readText($, path)) ?? ''
  const kept = old.length > 200000 ? old.slice(-100000) : old
  await $.fs.write(path, `${kept}${await now($)} ${line}\n`)
}

function byName(a: string, b: string): number {
  return a.localeCompare(b, 'en', { numeric: true })
}

async function names($: any, folder: string): Promise<string[]> {
  if (!(await $.fs.exists(folder))) return []
  const entries = await $.fs.list(folder)
  return entries.map((x: any) => String(x.name)).sort(byName)
}

async function waitForFile($: any, path: string, seconds: number): Promise<string | undefined> {
  const script = `$f='${path}'; $end=(Get-Date).AddSeconds(${seconds}); while (-not (Test-Path $f) -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 1000 }; if (Test-Path $f) { Get-Content -Raw $f }`
  const run = await $.process.run(['powershell', '-NoProfile', '-Command', script], { timeoutMs: (seconds + 15) * 1000 })
  if (run.exitCode !== 0) await log($, `wait exit=${run.exitCode} err=${String(run.stderr ?? '').slice(0, 200)}`)
  const text = String(run.stdout ?? '').trim()
  return text.length > 0 ? text : undefined
}

function answerText(answer: any): string {
  if (answer === undefined || answer === null) return ''
  if (typeof answer.answer === 'string') return answer.answer
  if (typeof answer.text === 'string') return answer.text
  return JSON.stringify(answer.answers ?? answer)
}

async function markDelivered($: any, id: string, by: string): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/delivered/${id}.json`, { by, at: await now($) })
}

async function writeLease($: any, holder: string, reason: unknown): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/lease.json`, { holder, since: await now($), reason })
}

async function submit($: any, text: string): Promise<boolean> {
  const result = await $.prompt.submit({ text })
  if (result && typeof result.drop === 'string') {
    if (S.dropped !== result.drop) {
      await log($, `submit dropped, will try again: ${result.drop}`)
      await writeJson($, `${S.dir}/blocked.json`, { reason: result.drop, since: await now($) })
    }
    S.dropped = result.drop
    return false
  }
  if (S.dropped !== '') await writeJson($, `${S.dir}/blocked.json`, { reason: null, since: await now($) })
  S.dropped = ''
  return true
}

async function drainInbox($: any): Promise<void> {
  if (!S.dir) return
  for (const name of await names($, `${S.dir}/inbox`)) {
    if (S.cursor !== '' && byName(name, S.cursor) <= 0) continue
    const raw = (await readText($, `${S.dir}/inbox/${name}`)) ?? ''
    const text = name.endsWith('.json') ? answerText(JSON.parse(raw)) : raw
    if (!(await submit($, text))) return
    S.cursor = name
    await $.fs.write(`${S.dir}/cursor.txt`, name)
    await log($, `inbox ${name} submitted`)
  }
}

async function deliverAnswers($: any): Promise<void> {
  if (!S.dir) return
  for (const name of await names($, `${S.dir}/decisions`)) {
    const id = name.replace(/\.json$/, '')
    if (await $.fs.exists(`${S.dir}/delivered/${id}.json`)) continue
    const decision: Decision | undefined = await readJson($, `${S.dir}/decisions/${name}`)
    if (!decision || decision.mode !== 'pending') continue
    const answer = await readJson($, `${S.dir}/answers/${id}.json`)
    if (answer === undefined) continue
    const text = `Answer to decision ${id} (${decision.question ?? 'question'}): ${answerText(answer)}`
    if (!(await submit($, text))) return
    await markDelivered($, id, 'inbox')
    await log($, `decision ${id} answer submitted`)
  }
}

async function beat($: any): Promise<void> {
  if (!S.dir) return
  await $.fs.write(`${S.dir}/heartbeat.txt`, await now($))
  const lease = await readJson($, `${S.dir}/lease.json`)
  if (lease?.holder === S.holder) return
  if (lease?.holder === 'none') {
    await writeLease($, S.holder, { event: 'lease taken back', was: lease })
    await log($, 'lease taken back')
    return
  }
  await log($, `lease conflict: the store says ${lease?.holder}, this process is ${S.holder}`)
}

async function tick($: any): Promise<void> {
  if (!S.dir || S.busy || !S.idle || S.holder !== 'live') return
  S.busy = true
  try {
    await drainInbox($)
    await deliverAnswers($)
  } catch (error) {
    await log($, `tick failed: ${String(error)}`)
  } finally {
    S.busy = false
  }
}

async function writeStatus($: any, state: string, turnId: unknown): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/status.json`, { state, turnId, at: await now($) })
}

async function registerTools($: any): Promise<void> {
  await $.tool.register({
    name: 'ask_user',
    description:
      'Put one decision in the user queue. Give the risk and your default option. With waitSeconds 0 it returns PENDING at once: end your turn, and the answer arrives later as a new message that starts with "Answer to decision". With waitSeconds more than 0 it blocks up to that time (maximum 580) and returns ANSWER: <text>, or PENDING when no answer came.',
    inputSchema: {
      type: 'object',
      properties: {
        question: { type: 'string' },
        options: { type: 'array', items: { type: 'string' } },
        risk: { type: 'string', enum: ['low', 'medium', 'high'] },
        defaultOption: { type: 'string', description: 'The option you pick if the user gives no answer.' },
        waitSeconds: { type: 'number', description: '0 (default): return PENDING at once. More than 0: block up to this many seconds.' },
      },
      required: ['question', 'risk'],
    },
    isDeferred: false,
  })
  await $.tool.register({
    name: 'report',
    description: 'Record your progress for the orchestrator. Use it when you start a step, when you are blocked, and when you are done.',
    inputSchema: {
      type: 'object',
      properties: {
        state: { type: 'string', enum: ['working', 'blocked', 'done'] },
        summary: { type: 'string', description: 'One or two sentences.' },
        details: { type: 'string' },
      },
      required: ['state', 'summary'],
    },
    isDeferred: false,
  })
  await $.tool.register({
    name: 'spawn_child',
    description:
      'Start a child session under the orchestrator. Give it a short title and a complete brief: the child knows only the brief. The child starts at once and works by itself. When the child reports done or blocked, you get a new message that starts with "Report from child".',
    inputSchema: {
      type: 'object',
      properties: {
        title: { type: 'string' },
        brief: { type: 'string' },
        cwd: { type: 'string', description: 'The folder of the child. Default: the folder of this session.' },
      },
      required: ['title', 'brief'],
    },
    isDeferred: false,
  })
}

async function newDecision($: any, e: any, kind: Decision['kind'], wait: number): Promise<Decision> {
  const decision: Decision = {
    id: String(e.tool_use_id),
    session: S.id ?? '',
    kind,
    question: e.question,
    options: e.options,
    questions: e.questions,
    risk: e.risk ?? 'medium',
    defaultOption: e.defaultOption,
    mode: wait > 0 ? 'wait' : 'pending',
    waitSeconds: wait,
    asked: await now($),
  }
  await writeJson($, `${S.dir}/decisions/${decision.id}.json`, decision)
  return decision
}

async function storeRoot($: any): Promise<string> {
  return (await $.env.get('ORCH_STORE')) ?? DEFAULT_STORE
}

async function sessionDir($: any): Promise<string> {
  return `${await storeRoot($)}/sessions/${await $.session.id()}`
}

function newId(): string {
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = '89ab'.charAt(Math.floor(Math.random() * 4))
  const h = hex.join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

async function activate($: any, dir: string, reason: unknown): Promise<void> {
  S.id = await $.session.id()
  S.dir = dir
  S.store = await storeRoot($)
  S.cwd = await $.session.cwd()
  S.holder = (await $.env.get('ORCH_HOLDER')) ?? 'live'
  S.cursor = ((await readText($, `${dir}/cursor.txt`)) ?? '').trim()
  const status = await readJson($, `${dir}/status.json`)
  S.idle = status?.state !== 'running'
  await $.fs.write(`${dir}/heartbeat.txt`, await now($))
  await writeLease($, S.holder, reason)
  await log($, `activate holder=${S.holder} cursor=${S.cursor} ${JSON.stringify(reason)}`)
  await registerTools($)
  $.clock.every(TICK_MS, () => beat($))
  $.clock.every(TICK_MS, () => tick($))
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    const dir = await sessionDir($)
    if (await $.fs.exists(`${dir}/enabled`)) await activate($, dir, { event: 'session.start', start: e })
    await $.command.register({ name: 'orch-enable', description: 'Put this session under the orchestrator: inbox, mirror, lease and the ask_user and report tools.' })
    return result
  })

  on('command.run', { command: 'orch-enable' }, async $ => {
    const dir = await sessionDir($)
    if (S.dir === dir) return { text: `orch: this session is already enabled (${dir}).` }
    await $.fs.write(`${dir}/enabled`, '')
    await activate($, dir, { event: 'orch-enable' })
    return { text: `orch: enabled. Store folder: ${dir}` }
  })

  on('turn.start', async ($, e, next) => {
    if (S.dir && e.agentId === undefined) {
      S.idle = false
      await writeStatus($, 'running', e.turnId)
    }
    return next(e)
  })

  on('turn.complete', async ($, e, next) => {
    const result = await next(e)
    const turn = e as any
    if (S.dir && turn.agentId === undefined) {
      S.idle = true
      await writeStatus($, 'idle', turn.turnId)
    }
    return result
  })

  on('session.append', async ($, e, next) => {
    if (S.dir && e.agentId === undefined && MIRROR_DOORS.includes(e.door)) {
      const row = { door: e.door, origin: e.origin, uuid: e.uuid, message: JSON.stringify(e.message).slice(0, ROW_CHARS) }
      await writeJson($, `${S.dir}/mirror/${await $.clock.now()}-${e.uuid}.json`, row)
    }
    return next(e)
  })

  on('session.end', async ($, e, next) => {
    if (S.dir) {
      const lease = await readJson($, `${S.dir}/lease.json`)
      if (lease?.holder === S.holder) await writeLease($, 'none', { event: 'session.end', end: e })
      await log($, `session.end ${JSON.stringify(e)}`)
    }
    return next(e)
  })

  on('tool.call', { tool: REPORT_TOOL }, async ($, e: any) => {
    if (!S.dir) return { deny: 'orch: this session is not enabled' }
    const report = { state: e.state, summary: e.summary, details: e.details, at: await now($) }
    await writeJson($, `${S.dir}/reports/${await $.clock.now()}.json`, report)
    await writeJson($, `${S.dir}/report.json`, report)
    return { result: 'Recorded.' }
  }).catch((_$: any, _e: any) => ({ result: 'orch: the report was not recorded. Continue your work.' }))

  on('tool.call', { tool: SPAWN_TOOL }, async ($, e: any) => {
    if (!S.dir || !S.store) return { deny: 'orch: this session is not enabled' }
    const child = newId()
    const dir = `${S.store}/sessions/${child}`
    const meta = { id: child, parent: S.id, title: e.title, cwd: e.cwd ?? S.cwd, createdBy: 'spawn_child', createdAt: await now($) }
    await writeJson($, `${dir}/meta.json`, meta)
    await $.fs.write(`${dir}/inbox/1.txt`, String(e.brief))
    await $.fs.write(`${dir}/enabled`, '')
    await log($, `spawn_child ${child} ${e.title}`)
    return { result: `Started child "${e.title}" (${child}). It reports back as a new message that starts with "Report from child". If you have nothing else to do now, end your turn.` }
  }).catch((_$: any, _e: any) => ({ result: 'orch: spawn_child failed. Do the work yourself or report blocked.' }))

  on('tool.call', { tool: ASK_TOOL }, async ($, e: any) => {
    if (!S.dir) return { deny: 'orch: this session is not enabled' }
    const wait = Math.max(0, Math.min(MAX_WAIT_SECONDS, Number(e.waitSeconds ?? 0)))
    const decision = await newDecision($, e, 'ask_user', wait)
    const later = `Decision ${decision.id} stays in the user queue. End your turn now. The answer arrives as a new message that starts with "Answer to decision ${decision.id}".`
    if (wait === 0) {
      await log($, `ask_user ${decision.id} pending`)
      return { result: `PENDING: ${later}` }
    }
    await log($, `ask_user ${decision.id} waiting ${wait}s`)
    const answer = await waitForFile($, `${S.dir}/answers/${decision.id}.json`, wait)
    if (answer === undefined) {
      await writeJson($, `${S.dir}/decisions/${decision.id}.json`, { ...decision, mode: 'pending' })
      await log($, `ask_user ${decision.id} timed out, now pending`)
      return { result: `PENDING: no answer in ${wait}s. ${later}` }
    }
    await markDelivered($, decision.id, 'tool-result')
    await log($, `ask_user ${decision.id} answered`)
    return { result: `ANSWER: ${answerText(JSON.parse(answer))}` }
  }).catch((_$: any, _e: any) => ({ result: 'orch: ask_user failed. Ask the question in your reply text and end your turn.' }))

  on('tool.call', { tool: 'AskUserQuestion' }, async ($, e: any, next) => {
    if (!S.dir) return next(e)
    const decision = await newDecision($, e, 'AskUserQuestion', REDIRECT_WAIT_SECONDS)
    await log($, `AskUserQuestion ${decision.id} redirected`)
    let answer: string | undefined
    try {
      answer = await waitForFile($, `${S.dir}/answers/${decision.id}.json`, REDIRECT_WAIT_SECONDS)
    } catch (error) {
      await log($, `AskUserQuestion ${decision.id} wait failed: ${String(error)}`)
    }
    if (answer === undefined) {
      await markDelivered($, decision.id, 'app-card')
      await log($, `AskUserQuestion ${decision.id} no answer, app card`)
      return next(e)
    }
    await markDelivered($, decision.id, 'tool-result')
    await log($, `AskUserQuestion ${decision.id} answered from the store`)
    return { result: { questions: e.questions, answers: JSON.parse(answer).answers } }
  }).catch((_$: any, e: any, next: any) => next(e))
}
