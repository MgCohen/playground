import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync, renameSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { randomUUID } from 'node:crypto'

export const LIVE_HEARTBEAT_MS = 15000

export type Lease = { holder: 'live' | 'supervisor' | 'none'; since: string; reason?: any }
export type Meta = { id: string; parent?: string; title?: string; cwd?: string; createdBy?: string; createdAt?: string; started?: boolean }
export type Decision = {
  id: string
  session: string
  kind: 'ask_user' | 'AskUserQuestion'
  question?: string
  options?: string[]
  questions?: any
  risk: string
  defaultOption?: string
  mode: 'wait' | 'pending'
  waitSeconds: number
  asked: string
}
export type Report = { state: 'working' | 'blocked' | 'done'; summary: string; details?: string; at: string }
export type Pending = { text: string; inbox: string[]; answers: string[] }

export function byName(a: string, b: string): number {
  return a.localeCompare(b, 'en', { numeric: true })
}

export function subject(data: Record<string, unknown>): string {
  const text = String(data.question ?? data.summary ?? data.title ?? data.text ?? '')
  return text.replace(/\s+/g, ' ').trim().slice(0, 80)
}

export class Store {
  readonly root: string
  constructor(root: string) {
    this.root = root
  }

  dir(id: string): string {
    return join(this.root, 'sessions', id)
  }

  path(id: string, ...parts: string[]): string {
    return join(this.dir(id), ...parts)
  }

  readText(path: string): string | undefined {
    return existsSync(path) ? readFileSync(path, 'utf8') : undefined
  }

  readJson<T>(path: string): T | undefined {
    const text = this.readText(path)
    if (text === undefined) return undefined
    try {
      return JSON.parse(text) as T
    } catch {
      return undefined
    }
  }

  write(path: string, text: string): void {
    mkdirSync(dirname(path), { recursive: true })
    const temp = `${path}.${process.pid}.tmp`
    writeFileSync(temp, text)
    renameSync(temp, path)
  }

  writeJson(path: string, value: unknown): void {
    this.write(path, JSON.stringify(value, null, 2))
  }

  names(path: string): string[] {
    if (!existsSync(path)) return []
    return readdirSync(path).filter(name => !name.endsWith('.tmp')).sort(byName)
  }

  sessions(): string[] {
    return this.names(join(this.root, 'sessions')).filter(id => existsSync(this.path(id, 'enabled')))
  }

  meta(id: string): Meta {
    return this.readJson<Meta>(this.path(id, 'meta.json')) ?? { id }
  }

  saveMeta(meta: Meta): void {
    this.writeJson(this.path(meta.id, 'meta.json'), meta)
  }

  lease(id: string): Lease {
    return this.readJson<Lease>(this.path(id, 'lease.json')) ?? { holder: 'none', since: '' }
  }

  heartbeatAge(id: string, now: number): number {
    const text = this.readText(this.path(id, 'heartbeat.txt'))
    if (text === undefined) return Infinity
    const at = Date.parse(text.trim())
    return Number.isNaN(at) ? Infinity : now - at
  }

  isLive(id: string, now: number): boolean {
    const lease = this.lease(id)
    if (lease.holder !== 'live') return false
    const leaseAge = now - Date.parse(lease.since)
    return this.heartbeatAge(id, now) < LIVE_HEARTBEAT_MS || leaseAge < LIVE_HEARTBEAT_MS
  }

  cwd(id: string): string | undefined {
    return this.meta(id).cwd ?? this.lease(id).reason?.start?.cwd
  }

  create(meta: Omit<Meta, 'id'>, brief: string): string {
    const id = randomUUID()
    this.saveMeta({ id, ...meta, createdAt: new Date().toISOString() })
    this.write(this.path(id, 'inbox', '1.txt'), brief)
    this.write(this.path(id, 'enabled'), '')
    return id
  }

  send(id: string, text: string): string {
    const name = `${Date.now()}-${randomUUID().slice(0, 8)}.txt`
    this.write(this.path(id, 'inbox', name), text)
    return name
  }

  isAnswered(id: string, decision: string): boolean {
    return existsSync(this.path(id, 'answers', `${decision}.json`))
  }

  ledger(entry: Record<string, unknown>): void {
    const path = join(this.root, 'ledger.jsonl')
    writeFileSync(path, `${JSON.stringify({ at: new Date().toISOString(), ...entry })}\n`, { flag: 'a' })
  }

  answer(id: string, decision: string, value: { answer?: string; answers?: Record<string, string>; by?: string; source?: string; note?: string }): void {
    this.writeJson(this.path(id, 'answers', `${decision}.json`), value)
  }

  decisions(id: string): Decision[] {
    return this.names(this.path(id, 'decisions'))
      .map(name => this.readJson<Decision>(this.path(id, 'decisions', name)))
      .filter((d): d is Decision => d !== undefined)
  }

  isDelivered(id: string, decision: string): boolean {
    return existsSync(this.path(id, 'delivered', `${decision}.json`))
  }

  openDecisions(id: string): Decision[] {
    return this.decisions(id).filter(d => !this.isDelivered(id, d.id) && !existsSync(this.path(id, 'answers', `${d.id}.json`)))
  }

  report(id: string): Report | undefined {
    return this.readJson<Report>(this.path(id, 'report.json'))
  }

  pending(id: string): Pending {
    const cursor = (this.readText(this.path(id, 'cursor.txt')) ?? '').trim()
    const inbox = this.names(this.path(id, 'inbox')).filter(name => cursor === '' || byName(name, cursor) > 0)
    const texts = inbox.map(name => {
      const raw = this.readText(this.path(id, 'inbox', name)) ?? ''
      return name.endsWith('.json') ? String(JSON.parse(raw).text ?? '') : raw
    })
    const answers: string[] = []
    for (const d of this.decisions(id)) {
      if (d.mode !== 'pending' || this.isDelivered(id, d.id)) continue
      const answer = this.readJson<any>(this.path(id, 'answers', `${d.id}.json`))
      if (answer === undefined) continue
      answers.push(d.id)
      const note = answer.note ? `\n\n${answer.note}` : ''
      texts.push(`Answer to decision ${d.id} (${d.question ?? 'question'}): ${answer.answer ?? JSON.stringify(answer.answers ?? answer)}${note}`)
    }
    return { text: texts.join('\n\n---\n\n'), inbox, answers }
  }

  markSent(id: string, pending: Pending): void {
    const last = pending.inbox.at(-1)
    if (last !== undefined) this.write(this.path(id, 'cursor.txt'), last)
    for (const decision of pending.answers) this.writeJson(this.path(id, 'delivered', `${decision}.json`), { by: 'supervisor', at: new Date().toISOString() })
  }

  event(kind: string, from: string, to: string, data: Record<string, unknown>, refs: Record<string, unknown> = {}, session?: string): void {
    const at = new Date()
    const id = `${at.getTime()}-${randomUUID().slice(0, 8)}-supervisor`
    const day = at.toISOString().slice(0, 10)
    const body = `bodies/${day}/${id}.json`
    this.write(join(this.root, body), JSON.stringify(data, null, 2))
    const event = { id, at: at.toISOString(), session, kind, from, to, subject: subject(data), body, refs }
    this.write(join(this.root, 'events', day, `${id}.json`), JSON.stringify(event, null, 2))
  }

  events(): any[] {
    const root = join(this.root, 'events')
    return this.names(root).flatMap(day =>
      this.names(join(root, day))
        .map(name => this.readJson<any>(join(root, day, name)))
        .filter(e => e !== undefined),
    ).sort((a, b) => byName(a.id, b.id))
  }

  log(line: string): void {
    const path = join(this.root, 'supervisor', 'log.txt')
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, `${new Date().toISOString()} ${line}\n`, { flag: 'a' })
  }
}
