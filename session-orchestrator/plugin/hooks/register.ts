import type { Register } from 'claude-code'
import { ROW_CHARS, S, TICK_MS, homeRoot, log, now, readJson, sessionDir, storeRoot, writeJson } from './state.ts'
import { emitRow } from './events.ts'
import { beat, findProcess, releaseLease, writeLease } from './lease.ts'
import { deliver, tick, watchLoop } from './deliver.ts'
import { NUDGE } from './stream.ts'
import {
  ANSWER_TOOL,
  ASK_TOOL,
  PLAN_TOOL,
  QUERY_TOOL,
  QUESTIONS_TOOL,
  REPORT_TOOL,
  SEND_TOOL,
  SPAWN_TOOL,
  UPDATE_TOOL,
  answerChild,
  answerFromChat,
  askUser,
  listQuestions,
  plan,
  query,
  registerTools,
  report,
  send,
  spawnChild,
  update,
} from './tools.ts'

const REMOTE_CONTROL_TOOL = 'mcp__ccd_session_mgmt__set_remote_control'

function isSelf(id: unknown): boolean {
  return id === undefined || id === 'self' || id === S.id || id === `local_${S.id}`
}

async function recordRemoteControl($: any, enabled: boolean): Promise<void> {
  const meta = await readJson($, `${S.dir}/meta.json`)
  if (meta) await writeJson($, `${S.dir}/meta.json`, { ...meta, remoteControl: enabled })
  await log($, `remote control ${enabled ? 'on' : 'off'}`)
}

const MIRROR_DOORS = ['prompt', 'response', 'tool-result']
const ASK_IN_TEXT =
  'The user does not use this tool, because it allows only a click on fixed options. Write each question in your reply text: the question, the context, the options and your recommendation. Then end your turn. The user answers in free text.'
const MERGE_IN_ORCH =
  'In this session, do not merge with gh pr merge. Run orch merge <pr>. It refuses the merge while the PR carries a default decision that the user did not approve.'
const GH_PR_MERGE = /\bgh\s+pr\s+merge\b/
const ASK_IN_ORCH =
  'The user does not use this tool, because it allows only a click on fixed options. In this session, call mcp__orch__ask_user one time for each question. The user answers in free text.'

async function writeStatus($: any, state: string, turnId: unknown): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/status.json`, { state, turnId, at: await now($) })
}

async function mirror($: any, e: any): Promise<void> {
  const row = { door: e.door, origin: e.origin, uuid: e.uuid, message: JSON.stringify(e.message).slice(0, ROW_CHARS) }
  await writeJson($, `${S.dir}/mirror/${await $.clock.now()}-${e.uuid}.json`, row)
  await emitRow($, e)
}

export const STORE_VERSION = 2

// A store of another version has another contract: the plugin stays inactive and writes nothing.
async function versionOk($: any): Promise<boolean> {
  const version = await readJson($, `${await storeRoot($)}/version.json`)
  return version === undefined || version.version === STORE_VERSION
}

async function activate($: any, dir: string, reason: unknown): Promise<void> {
  if (!(await versionOk($))) return
  S.id = await $.session.id()
  S.dir = dir
  S.home = await homeRoot($)
  S.store = await storeRoot($)
  S.cwd = await $.session.cwd()
  S.holder = (await $.env.get('ORCH_HOLDER')) ?? 'live'
  S.wanted = true
  const status = await readJson($, `${dir}/status.json`)
  S.idle = status?.state !== 'running'
  await $.fs.write(`${dir}/heartbeat.txt`, await now($))
  await findProcess($)
  await writeLease($, S.holder, reason)
  const host = await $.env.get('CLAUDE_CODE_HOST_SESSION_ID')
  const meta = await readJson($, `${dir}/meta.json`)
  if (host && meta && meta.appId !== host) await writeJson($, `${dir}/meta.json`, { ...meta, appId: host })
  await log($, `activate plugin holder=${S.holder} ${JSON.stringify(reason)}`)
  await registerTools($)
  if (S.holder === 'live') watchLoop($).catch((error: unknown) => log($, `watch failed: ${String(error)}`))
  if (S.timers) return
  S.timers = true
  $.clock.every(TICK_MS, () => beat($))
  $.clock.every(TICK_MS, () => tick($))
}

async function activateIfEnabled($: any, event: string): Promise<void> {
  const dir = await sessionDir($)
  if (await $.fs.exists(`${dir}/enabled`)) await activate($, dir, { event })
}

async function writeMeta($: any, dir: string, title: string): Promise<void> {
  const path = `${dir}/meta.json`
  const old = await readJson($, path)
  if (old && title === '') return
  const cwd = await $.session.cwd()
  const name = String(cwd).split(/[\\/]/).filter(Boolean).pop() ?? 'chat'
  await writeJson($, path, {
    ...(old ?? { id: await $.session.id(), cwd, createdBy: 'orch-enable', createdAt: await now($), started: true }),
    title: title !== '' ? title : old?.title ?? `App chat in ${name}`,
  })
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    const dir = await sessionDir($)
    if (await $.fs.exists(`${dir}/enabled`)) await activate($, dir, { event: 'session.start', start: e })
    await $.command.register({ name: 'orch-enable', description: 'Put this session under the orchestrator: stream delivery, mirror, lease and the orch tools. Optional: a title, for example /orch-enable Foobar root.' })
    return result
  })

  on('command.run', { command: 'orch-enable' }, async ($, e: any) => {
    const dir = await sessionDir($)
    const title = String(e.args ?? '').trim()
    await writeMeta($, dir, title)
    if (S.dir === dir) return { text: `This session is already enabled (${dir}).${title !== '' ? ` New title: ${title}.` : ''}` }
    await $.fs.write(`${dir}/enabled`, '')
    await activate($, dir, { event: 'orch-enable' })
    return { text: `Enabled. Store folder: ${dir}` }
  })

  on('turn.start', async ($, e, next) => {
    if (!S.dir && e.agentId === undefined) await activateIfEnabled($, 'turn.start')
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
      deliver($).catch((error: unknown) => log($, `deliver failed: ${String(error)}`))
    }
    return result
  })

  on('session.append', async ($, e, next) => {
    if (!S.dir && e.agentId === undefined && e.door === 'tool-result') await activateIfEnabled($, 'orch enable')
    if (S.dir && e.agentId === undefined && MIRROR_DOORS.includes(e.door)) await mirror($, e)
    if (S.dir && e.agentId === undefined && e.door === 'prompt') answerFromChat($, e).catch((error: unknown) => log($, `chat answer failed: ${String(error)}`))
    return next(e)
  })

  // A peer nudge carries no content: take it, so the model does not see it, and look at the stream.
  on('session.receive', async ($, e: any, next) => {
    if (typeof e.text !== 'string' || !e.text.startsWith(NUDGE)) return next(e)
    if (S.dir) {
      S.wanted = true
      if (!S.watching) watchLoop($).catch((error: unknown) => log($, `watch failed: ${String(error)}`))
      deliver($).catch((error: unknown) => log($, `deliver failed: ${String(error)}`))
    }
    return { consumed: 'orch nudge: the plugin looks at the stream' }
  })

  // F4: the session turned its Remote Control on or off. meta.json records it, so the supervisor sends no parent notice.
  on('tool.call', { tool: REMOTE_CONTROL_TOOL }, async ($: any, e: any, next: any) => {
    const result = await next(e)
    if (S.dir && isSelf(e.session_id) && !(result && (result.deny || result.isError))) recordRemoteControl($, e.enabled !== false).catch((error: unknown) => log($, `remote control record failed: ${String(error)}`))
    return result
  })

  on('session.end', async ($, e, next) => {
    await releaseLease($, e)
    return next(e)
  })

  on('tool.call', { tool: REPORT_TOOL }, async ($, e: any) => report($, e)).catch((_$: any, _e: any) => ({ result: 'orch: the report was not recorded. Continue your work.' }))

  on('tool.call', { tool: UPDATE_TOOL }, async ($, e: any) => update($, e)).catch((_$: any, _e: any) => ({ result: 'orch: the update was not recorded. Continue your work.' }))

  on('tool.call', { tool: SPAWN_TOOL }, async ($, e: any) => spawnChild($, e)).catch((_$: any, _e: any) => ({ result: 'orch: spawn_child failed. Do the work yourself or report blocked.' }))

  on('tool.call', { tool: ANSWER_TOOL }, async ($, e: any) => answerChild($, e)).catch((_$: any, _e: any) => ({ result: 'orch: answer failed. Tell the user to answer with orch answer.' }))

  on('tool.call', { tool: SEND_TOOL }, async ($, e: any) => send($, e)).catch((_$: any, _e: any) => ({ result: 'orch: send failed. Tell the user.' }))

  on('tool.call', { tool: QUERY_TOOL }, async ($, e: any) => query($, e)).catch((_$: any, _e: any) => ({ result: 'orch: query failed. Ask the question in your reply text and end your turn.' }))

  on('tool.call', { tool: PLAN_TOOL }, async ($, e: any) => plan($, e)).catch((_$: any, _e: any) => ({ result: 'orch: plan failed. Show the plan in your reply text and end your turn.' }))

  on('tool.call', { tool: QUESTIONS_TOOL }, async ($, _e: any) => listQuestions($)).catch((_$: any, _e: any) => ({ result: 'orch: the list failed. Run orch decisions.' }))

  on('tool.call', { tool: ASK_TOOL }, async ($, e: any) => askUser($, e)).catch((_$: any, _e: any) => ({ result: 'orch: ask_user failed. Ask the question in your reply text and end your turn.' }))

  for (const shell of ['Bash', 'PowerShell'])
    on('tool.call', { tool: shell }, async ($: any, e: any, next: any) => (S.dir && GH_PR_MERGE.test(String(e.command ?? '')) ? { deny: MERGE_IN_ORCH } : next(e)))

  on('tool.describe', { tool: 'AskUserQuestion' }, async () => ({ description: `Do not use this tool. ${ASK_IN_TEXT}`, isDeferred: true }))

  on('tool.call', { tool: 'AskUserQuestion' }, async () => ({ deny: S.dir ? ASK_IN_ORCH : ASK_IN_TEXT }))
}
