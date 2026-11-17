import { S, log, now, orch, readJson, writeJson } from './state.ts'

// The process that holds the lease: orch process prints the id and the start time of its parent, the claude process
// of this session. The supervisor releases a stale lease only when this process is gone (two writers, S0).
export async function findProcess($: any): Promise<void> {
  if (S.pid !== undefined) return
  try {
    const run = await orch($, ['process'], 15000)
    const value = run.ok ? JSON.parse(run.text) : undefined
    if (typeof value?.pid === 'number') {
      S.pid = value.pid
      S.started = String(value.started ?? '')
    } else await log($, `orch process failed: ${run.text.slice(0, 200)}`)
  } catch (error) {
    await log($, `orch process failed: ${String(error)}`)
  }
}

export async function writeLease($: any, holder: string, reason: unknown): Promise<void> {
  if (!S.dir) return
  const lease: Record<string, unknown> = { holder, since: await now($), reason }
  if (holder !== 'none' && S.pid !== undefined) {
    lease.pid = S.pid
    lease.started = S.started
  }
  await writeJson($, `${S.dir}/lease.json`, lease)
}

export async function beat($: any): Promise<void> {
  if (!S.dir) return
  if (!(await $.fs.exists(`${S.dir}/enabled`))) {
    await writeLease($, 'none', { event: 'archived' })
    await log($, 'the enabled file is gone: lease released, plugin inactive')
    S.dir = undefined
    return
  }
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

export async function releaseLease($: any, e: unknown): Promise<void> {
  if (!S.dir) return
  const lease = await readJson($, `${S.dir}/lease.json`)
  if (lease?.holder === S.holder) await writeLease($, 'none', { event: 'session.end', end: e })
  await log($, `session.end ${JSON.stringify(e)}`)
}
