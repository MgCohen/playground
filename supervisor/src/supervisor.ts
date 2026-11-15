import { Store, type Pending } from './store.ts'

export type TurnRequest = { id: string; cwd: string; prompt: string; isNew: boolean }
export type TurnResult = { ok: boolean; text?: string; error?: string; seconds: number }
export type RunTurn = (request: TurnRequest) => Promise<TurnResult>

export type SupervisorOptions = { maxRuns: number; maxFailures: number; now?: () => number }

type Forwarded = { at: string }

export class Supervisor {
  readonly store: Store
  readonly runTurn: RunTurn
  readonly options: SupervisorOptions
  readonly running = new Map<string, Promise<void>>()
  readonly failures = new Map<string, number>()

  constructor(store: Store, runTurn: RunTurn, options: SupervisorOptions) {
    this.store = store
    this.runTurn = runTurn
    this.options = options
  }

  now(): number {
    return this.options.now?.() ?? Date.now()
  }

  tick(): void {
    const now = this.now()
    for (const id of this.store.sessions()) {
      this.releaseStaleLease(id, now)
      this.forwardReport(id)
    }
    for (const id of this.store.sessions()) {
      if (this.running.size >= this.options.maxRuns) return
      this.startTurn(id, now)
    }
  }

  releaseStaleLease(id: string, now: number): void {
    const lease = this.store.lease(id)
    if (lease.holder !== 'live' || this.store.isLive(id, now)) return
    this.store.writeJson(this.store.path(id, 'lease.json'), { holder: 'none', since: new Date(now).toISOString(), reason: { event: 'stale heartbeat' } })
    this.store.log(`${id} lease released: stale heartbeat`)
  }

  forwardReport(id: string): void {
    const meta = this.store.meta(id)
    const report = this.store.report(id)
    if (!meta.parent || !report || report.state === 'working') return
    const path = this.store.path(id, 'forwarded.json')
    if (this.store.readJson<Forwarded>(path)?.at === report.at) return
    const details = report.details ? `\n\n${report.details}` : ''
    this.store.send(meta.parent, `Report from child "${meta.title ?? id}" (${id}): ${report.state}. ${report.summary}${details}`)
    this.store.writeJson(path, { at: report.at })
    this.store.log(`${id} report ${report.state} forwarded to ${meta.parent}`)
  }

  canRun(id: string, now: number): boolean {
    if (this.running.has(id)) return false
    if ((this.failures.get(id) ?? 0) >= this.options.maxFailures) return false
    return !this.store.isLive(id, now)
  }

  startTurn(id: string, now: number): void {
    if (!this.canRun(id, now)) return
    const pending = this.store.pending(id)
    if (pending.text === '') return
    const cwd = this.store.cwd(id)
    if (!cwd) {
      this.store.log(`${id} skipped: no cwd`)
      this.failures.set(id, this.options.maxFailures)
      return
    }
    const meta = this.store.meta(id)
    const isNew = meta.createdBy !== undefined && meta.started !== true
    const work = this.run(id, { id, cwd, prompt: pending.text, isNew }, pending)
    this.running.set(id, work)
    void work.finally(() => this.running.delete(id))
  }

  async run(id: string, request: TurnRequest, pending: Pending): Promise<void> {
    this.store.log(`${id} turn start new=${request.isNew} inbox=${pending.inbox.join(',')} answers=${pending.answers.join(',')}`)
    if (request.isNew) this.store.saveMeta({ ...this.store.meta(id), started: true })
    const result = await this.runTurn(request).catch(error => ({ ok: false, error: String(error), seconds: 0 }))
    if (result.ok) {
      this.store.markSent(id, pending)
      this.failures.delete(id)
      this.store.log(`${id} turn done ${result.seconds}s: ${String(result.text ?? '').slice(0, 200)}`)
      return
    }
    if (request.isNew) this.store.saveMeta({ ...this.store.meta(id), started: false })
    const count = (this.failures.get(id) ?? 0) + 1
    this.failures.set(id, count)
    this.store.writeJson(this.store.path(id, 'blocked.json'), { reason: `supervisor turn failed ${count} times: ${result.error}`, since: new Date().toISOString() })
    this.store.log(`${id} turn failed (${count}): ${result.error}`)
  }

  async idle(): Promise<void> {
    while (this.running.size > 0) await Promise.all(this.running.values())
  }
}
