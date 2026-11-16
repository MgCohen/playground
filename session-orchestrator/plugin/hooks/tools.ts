import { HOME, MAX_WAIT_SECONDS, S, log, now, readJson, writeJson, type Decision } from './state.ts'
import { emit } from './events.ts'
import { answerText, markDelivered, waitForFile } from './inbox.ts'

export const ASK_TOOL = 'mcp__orch__ask_user'
export const REPORT_TOOL = 'mcp__orch__report'
export const UPDATE_TOOL = 'mcp__orch__update'
export const SPAWN_TOOL = 'mcp__orch__spawn_child'

async function templateSchema($: any, kind: string): Promise<any> {
  const schema = await readJson($, `${HOME}/templates/${kind}/schema.json`)
  if (!schema?.properties) throw new Error(`orch: no schema for template ${kind}`)
  return { type: 'object', properties: schema.properties, required: schema.required ?? [] }
}

async function riskLevels($: any): Promise<{ names: string[]; text: string }> {
  const policy = S.store ? await readJson($, `${S.store}/policy.json`) : undefined
  const levels: any[] = Array.isArray(policy?.levels) && policy.levels.length > 0 ? policy.levels : [{ name: 'low' }, { name: 'medium' }, { name: 'high' }]
  const text = levels.map(level => `${level.name}: ${level.description ?? ''}`).join(' ')
  return { names: levels.map(level => String(level.name)), text }
}

export async function registerTools($: any): Promise<void> {
  const risk = await riskLevels($)
  const decision = await templateSchema($, 'decision')
  decision.properties.risk = { ...decision.properties.risk, enum: risk.names, description: `Pick the level that fits. ${risk.text}` }
  await $.tool.register({
    name: 'ask_user',
    description:
      'Put one decision in the user queue (the decision template). The skill orch:orch-ask tells how to fill it. If no answer comes in time, the orchestrator can apply your default option, by the risk level. The user answers in free text. With waitSeconds 0 it returns PENDING at once: end your turn, and the answer arrives later as a new message that starts with "Answer to decision". With waitSeconds more than 0 it blocks up to that time (580 maximum) and returns ANSWER: <text>, or PENDING when no answer came.',
    inputSchema: decision,
    isDeferred: false,
  })
  await $.tool.register({
    name: 'report',
    description: 'Tell your parent or the user that the work is done or blocked (the report template). The reader must act. The skill orch:orch-report tells when to report and when to update.',
    inputSchema: await templateSchema($, 'report'),
    isDeferred: false,
  })
  await $.tool.register({
    name: 'update',
    description: 'Show your progress (the update template). Nobody must act on it: the UI shows it in the session tree.',
    inputSchema: await templateSchema($, 'update'),
    isDeferred: false,
  })
  const brief = await templateSchema($, 'brief')
  delete brief.properties.parent
  await $.tool.register({
    name: 'spawn_child',
    description:
      'Start a child session under the orchestrator (the brief template). The child knows only its brief. The skill orch:orch-brief tells how to fill it. When the child reports done or blocked, you get a new message that starts with "Report from child".',
    inputSchema: brief,
    isDeferred: false,
  })
}

async function parentParty($: any): Promise<string> {
  const meta = await readJson($, `${S.dir}/meta.json`)
  return meta?.parent ? `session:${meta.parent}` : 'user'
}

export async function report($: any, e: any): Promise<any> {
  if (!S.dir) return { deny: 'orch: this session is not enabled' }
  const fields = { state: e.state, summary: e.summary, done: e.done, next: e.next, blockers: e.blockers, links: e.links, details: e.details }
  const event = await emit($, 'report', `session:${S.id}`, await parentParty($), fields)
  const value = { ...fields, at: await now($), event }
  await writeJson($, `${S.dir}/reports/${await $.clock.now()}.json`, value)
  await writeJson($, `${S.dir}/report.json`, value)
  return { result: 'Recorded.' }
}

export async function update($: any, e: any): Promise<any> {
  if (!S.dir) return { deny: 'orch: this session is not enabled' }
  const fields = { step: e.step, progress: e.progress, summary: e.summary }
  const event = await emit($, 'update', `session:${S.id}`, await parentParty($), fields)
  await writeJson($, `${S.dir}/update.json`, { ...fields, at: await now($), event })
  return { result: 'Recorded.' }
}

export async function spawnChild($: any, e: any): Promise<any> {
  if (!S.dir || !S.id) return { deny: 'orch: this session is not enabled' }
  const argv = ['orch', 'new', '--title', String(e.title), '--goal', String(e.goal), '--parent', S.id, '--created-by', 'spawn_child', '--cwd', String(e.cwd ?? S.cwd)]
  if (e.scope) argv.push('--scope', String(e.scope))
  if (e.doneWhen) argv.push('--done-when', String(e.doneWhen))
  if (Array.isArray(e.skills) && e.skills.length > 0) argv.push('--skills', e.skills.join(','))
  if (e.agent) argv.push('--agent', String(e.agent))
  if (e.worktree === true) argv.push('--worktree')
  const run = await $.process.run(argv, { timeoutMs: 60000 })
  const out = String(run.stdout ?? '').trim()
  if (run.exitCode !== 0) {
    await log($, `spawn_child failed: ${String(run.stderr ?? '').slice(0, 400)}`)
    return { result: `orch: spawn_child failed: ${String(run.stderr ?? out).trim().slice(0, 400)}. Fix the fields, or do the work yourself.` }
  }
  const child = out.split(/\r?\n/).pop() ?? out
  await log($, `spawn_child ${child} ${e.title}`)
  return { result: `Started child "${e.title}" (${child}). It reports back as a new message that starts with "Report from child". If you have nothing else to do now, end your turn.` }
}

export async function askUser($: any, e: any): Promise<any> {
  if (!S.dir) return { deny: 'orch: this session is not enabled' }
  const wait = Math.max(0, Math.min(MAX_WAIT_SECONDS, Number(e.waitSeconds ?? 0)))
  const decision: Decision = {
    id: String(e.tool_use_id),
    session: S.id ?? '',
    kind: 'ask_user',
    question: e.question,
    context: e.context,
    snippet: e.snippet,
    options: e.options,
    recommendation: e.recommendation,
    risk: e.risk ?? 'medium',
    defaultOption: e.defaultOption,
    mode: wait > 0 ? 'wait' : 'pending',
    waitSeconds: wait,
    asked: await now($),
  }
  const fields = { question: e.question, context: e.context, snippet: e.snippet, options: e.options, recommendation: e.recommendation, risk: decision.risk, defaultOption: e.defaultOption, mode: decision.mode }
  await emit($, 'decision', `session:${S.id}`, 'user', fields, { decision: decision.id })
  await writeJson($, `${S.dir}/decisions/${decision.id}.json`, decision)
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
}
