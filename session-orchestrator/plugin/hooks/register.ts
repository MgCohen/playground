import type { Register } from 'claude-code'
import { ROW_CHARS, S, TICK_MS, log, now, readJson, readText, sessionDir, storeRoot, writeJson } from './state.ts'
import { emitRow } from './events.ts'
import { beat, releaseLease, writeLease } from './lease.ts'
import { tick } from './inbox.ts'
import { ASK_TOOL, REPORT_TOOL, SPAWN_TOOL, UPDATE_TOOL, askUser, registerTools, report, spawnChild, update } from './tools.ts'

const MIRROR_DOORS = ['prompt', 'response', 'tool-result']
const ASK_IN_TEXT =
  'The user does not use this tool, because it allows only a click on fixed options. Write each question in your reply text: the question, the context, the options and your recommendation. Then end your turn. The user answers in free text.'
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
  await log($, `activate plugin holder=${S.holder} cursor=${S.cursor} ${JSON.stringify(reason)}`)
  await registerTools($)
  $.clock.every(TICK_MS, () => beat($))
  $.clock.every(TICK_MS, () => tick($))
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const result = await next(e)
    const dir = await sessionDir($)
    if (await $.fs.exists(`${dir}/enabled`)) await activate($, dir, { event: 'session.start', start: e })
    await $.command.register({ name: 'orch-enable', description: 'Put this session under the orchestrator: inbox, mirror, lease and the orch tools.' })
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
    if (S.dir && e.agentId === undefined && MIRROR_DOORS.includes(e.door)) await mirror($, e)
    return next(e)
  })

  on('session.end', async ($, e, next) => {
    await releaseLease($, e)
    return next(e)
  })

  on('tool.call', { tool: REPORT_TOOL }, async ($, e: any) => report($, e)).catch((_$: any, _e: any) => ({ result: 'orch: the report was not recorded. Continue your work.' }))

  on('tool.call', { tool: UPDATE_TOOL }, async ($, e: any) => update($, e)).catch((_$: any, _e: any) => ({ result: 'orch: the update was not recorded. Continue your work.' }))

  on('tool.call', { tool: SPAWN_TOOL }, async ($, e: any) => spawnChild($, e)).catch((_$: any, _e: any) => ({ result: 'orch: spawn_child failed. Do the work yourself or report blocked.' }))

  on('tool.call', { tool: ASK_TOOL }, async ($, e: any) => askUser($, e)).catch((_$: any, _e: any) => ({ result: 'orch: ask_user failed. Ask the question in your reply text and end your turn.' }))

  on('tool.describe', { tool: 'AskUserQuestion' }, async () => ({ description: `Do not use this tool. ${ASK_IN_TEXT}`, isDeferred: true }))

  on('tool.call', { tool: 'AskUserQuestion' }, async () => ({ deny: S.dir ? ASK_IN_ORCH : ASK_IN_TEXT }))
}
