import { Store, type Decision, type Pending } from './store.ts'
import { defaultNote, levelOf, reviewText, type Policy, type Verdict } from './policy.ts'

export type TurnRequest = { id: string; cwd: string; prompt: string; isNew: boolean }
export type TurnResult = { ok: boolean; text?: string; error?: string; seconds: number }
export type RunTurn = (request: TurnRequest) => Promise<TurnResult>

export type DocsRequest = { cwd: string; question: string; options: string[]; docs: string[] }
export type DocsResult = { answered: boolean; answer?: string; source?: string }
export type CheckDocs = (request: DocsRequest) => Promise<DocsResult>

export type SupervisorOptions = { maxRuns: number; maxFailures: number; maxChecks: number; policy: Policy; checkDocs: CheckDocs; now?: () => number }

type Forwarded = { at: string }

export class Supervisor {
  readonly store: Store
  readonly runTurn: RunTurn
  readonly options: SupervisorOptions
  readonly running = new Map<string, Promise<void>>()
  readonly failures = new Map<string, number>()
  readonly checks = new Map<string, Promise<void>>()

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
      this.applyPolicy(id, now)
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
    this.store.event('report.forward', `session:${id}`, `session:${meta.parent}`, { state: report.state, summary: report.summary }, { child: id }, meta.parent)
    this.store.log(`${id} report ${report.state} forwarded to ${meta.parent}`)
  }

  applyPolicy(id: string, now: number): void {
    for (const decision of this.store.openDecisions(id)) {
      const level = levelOf(this.options.policy, decision.risk)
      if (level.checkDocs) this.startDocsCheck(id, decision)
      if (level.timeoutSeconds === null || !decision.defaultOption) continue
      if (now - Date.parse(decision.asked) < level.timeoutSeconds * 1000) continue
      if (this.store.isAnswered(id, decision.id)) continue
      const reason = `level ${level.name}, no answer in ${level.timeoutSeconds} s`
      this.store.answer(id, decision.id, { answer: decision.defaultOption, by: 'policy', note: defaultNote(this.options.policy, decision.id, reason) })
      this.store.ledger({ session: id, decision: decision.id, question: decision.question, answer: decision.defaultOption, by: 'policy', reason })
      this.store.event('answer', 'policy', `session:${id}`, { text: decision.defaultOption, reason }, { decision: decision.id }, id)
      this.store.log(`${id} decision ${decision.id} default ${decision.defaultOption} (${reason})`)
    }
  }

  startDocsCheck(id: string, decision: Decision): void {
    const key = `${id}/${decision.id}`
    const path = this.store.path(id, 'docs-checks', `${decision.id}.json`)
    if (this.checks.has(key) || this.store.readText(path) !== undefined || this.checks.size >= this.options.maxChecks) return
    const cwd = this.store.cwd(id)
    if (!cwd) return
    const request = { cwd, question: decision.question ?? '', options: decision.options ?? [], docs: this.options.policy.docs }
    const work = this.options
      .checkDocs(request)
      .catch(error => ({ answered: false, error: String(error) }) as DocsResult)
      .then(result => {
        this.store.writeJson(path, { ...result, at: new Date().toISOString() })
        if (!result.answered || !result.answer || this.store.isAnswered(id, decision.id)) return
        const note = `This answer comes from the documentation (${result.source ?? 'no source given'}), not from the user. The user can review it later.`
        this.store.answer(id, decision.id, { answer: result.answer, by: 'docs', source: result.source, note })
        this.store.ledger({ session: id, decision: decision.id, question: decision.question, answer: result.answer, by: 'docs', source: result.source })
        this.store.event('answer', 'docs', `session:${id}`, { text: result.answer, source: result.source }, { decision: decision.id }, id)
        this.store.log(`${id} decision ${decision.id} answered from the docs: ${result.answer} (${result.source})`)
      })
    this.checks.set(key, work)
    void work.finally(() => this.checks.delete(key))
  }

  review(id: string, decision: string, verdict: Verdict, text: string): string {
    const question = this.store.readJson<Decision>(this.store.path(id, 'decisions', `${decision}.json`))?.question ?? 'question'
    this.store.writeJson(this.store.path(id, 'reviews', `${decision}.json`), { verdict, text, at: new Date().toISOString() })
    this.store.ledger({ session: id, decision, review: verdict, text })
    this.store.event('review', 'user', `session:${id}`, { verdict, text }, { decision }, id)
    return this.store.send(id, reviewText(decision, question, verdict, text))
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
    this.store.event('turn.start', 'supervisor', `session:${id}`, { isNew: request.isNew, prompt: request.prompt }, { inbox: pending.inbox, answers: pending.answers }, id)
    const result = await this.runTurn(request).catch(error => ({ ok: false, error: String(error), seconds: 0 }))
    if (result.ok) {
      this.store.markSent(id, pending)
      this.failures.delete(id)
      this.store.event('turn.end', `session:${id}`, 'supervisor', { ok: true, seconds: result.seconds, text: String(result.text ?? '') }, {}, id)
      this.store.log(`${id} turn done ${result.seconds}s: ${String(result.text ?? '').slice(0, 200)}`)
      return
    }
    if (request.isNew) this.store.saveMeta({ ...this.store.meta(id), started: false })
    const count = (this.failures.get(id) ?? 0) + 1
    this.failures.set(id, count)
    this.store.writeJson(this.store.path(id, 'blocked.json'), { reason: `supervisor turn failed ${count} times: ${result.error}`, since: new Date().toISOString() })
    this.store.event('turn.fail', `session:${id}`, 'supervisor', { count, error: result.error }, {}, id)
    this.store.log(`${id} turn failed (${count}): ${result.error}`)
  }

  async idle(): Promise<void> {
    while (this.running.size > 0 || this.checks.size > 0) await Promise.all([...this.running.values(), ...this.checks.values()])
  }
}
