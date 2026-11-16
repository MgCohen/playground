import { S, log, now, readJson, writeJson } from './state.ts'

export async function writeLease($: any, holder: string, reason: unknown): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/lease.json`, { holder, since: await now($), reason })
}

export async function beat($: any): Promise<void> {
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

export async function releaseLease($: any, e: unknown): Promise<void> {
  if (!S.dir) return
  const lease = await readJson($, `${S.dir}/lease.json`)
  if (lease?.holder === S.holder) await writeLease($, 'none', { event: 'session.end', end: e })
  await log($, `session.end ${JSON.stringify(e)}`)
}
