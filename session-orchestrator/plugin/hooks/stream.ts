// The TypeScript binding of the Stream API: post, ack and nudge. The C# binding (EventStream.cs) is the reference.
// Only C# renders: the plugin gets the text for the model from orch watch or orch read --for.
import { S, newId, readJson } from './state.ts'

export type Envelope = { id: string; kind: string; from: string; to: string; thread?: string; needs?: string; subject?: string; refs?: Record<string, unknown>; text?: string }

export type PostOptions = { thread?: string; needs?: string; refs?: Record<string, unknown> }

const META: Record<string, any> = {}

// The one subject rule. Subjects.Of in Event.cs has the same rule, and the contract tests check both.
export function subject(kind: string, data: Record<string, unknown>): string {
  let text: string
  if (kind === 'answer' && data.text !== undefined) text = String(data.text ?? '')
  else if (kind === 'review' && data.verdict !== undefined) text = `${data.verdict}${typeof data.text === 'string' && data.text.length > 0 ? `: ${data.text}` : ''}`
  else if (kind === 'ack' && data.state !== undefined) text = `${data.state} (${data.by ?? ''})`
  else if (kind === 'notice' && data.kind !== undefined) text = `${data.kind}${typeof data.question === 'string' && data.question.length > 0 ? `: ${data.question}` : ''}`
  else text = String(data.question ?? data.summary ?? data.title ?? data.text ?? '')
  return text.replace(/\s+/g, ' ').trim().slice(0, 80)
}

export function classOf(party: string): string {
  return party.startsWith('session:') ? 'session' : party
}

async function meta($: any, kind: string): Promise<any> {
  if (META[kind] !== undefined) return META[kind]
  const value = (await readJson($, `${S.home}/templates/${kind}/meta.json`)) ?? null
  const schema = (await readJson($, `${S.home}/templates/${kind}/schema.json`)) ?? null
  META[kind] = value === null ? null : { ...value, required: schema?.required ?? [] }
  return META[kind]
}

function allows(list: string[], party: string): boolean {
  return list.includes('*') || list.includes(classOf(party))
}

// Level 2 and 3 of the type check: a known type, the required fields, and the from and to of meta.json.
// The engine checks the full schema before the tool call (level 1).
async function check($: any, kind: string, from: string, to: string, body: Record<string, unknown>): Promise<void> {
  const m = await meta($, kind)
  if (m === null) return
  const missing = (m.required as string[]).filter(field => body[field] === undefined || body[field] === null)
  if (missing.length > 0) throw new Error(`${kind}: missing fields ${missing.join(', ')}`)
  if (!allows(m.from, from)) throw new Error(`${kind}: from ${from} is not allowed. Allowed: ${m.from.join(', ')}`)
  if (!allows(m.to, to)) throw new Error(`${kind}: to ${to} is not allowed. Allowed: ${m.to.join(', ')}`)
}

function clean(data: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(data)) if (value !== undefined && value !== null) out[key] = value
  return out
}

// Post: the body first, then the envelope. The envelope is the commit point.
export async function post($: any, kind: string, from: string, to: string, data: Record<string, unknown>, options: PostOptions = {}): Promise<string> {
  if (!S.store) return ''
  const body = clean(data)
  await check($, kind, from, to, body)
  let ms = await $.clock.now()
  if (ms <= S.lastMs) ms = S.lastMs + 1
  S.lastMs = ms
  const at = new Date(await $.clock.now()).toISOString()
  const day = new Date(ms).toISOString().slice(0, 10)
  const id = `${ms}-${newId().slice(0, 8)}-${S.holder}`
  const path = `bodies/${day}/${id}.json`
  await $.fs.write(`${S.store}/${path}`, JSON.stringify(body, null, 2))
  const envelope: Record<string, unknown> = { id, at, session: S.id, kind, from, to, subject: subject(kind, body), body: path, refs: clean(options.refs ?? {}) }
  if (options.thread) envelope.thread = options.thread
  if (options.needs) envelope.needs = options.needs
  await $.fs.write(`${S.store}/events/${day}/${id}.json`, JSON.stringify(envelope, null, 2))
  return id
}

// The pong: an ack with refs.of, from this session to the sender.
export async function ack($: any, envelope: Envelope, by: string): Promise<string> {
  S.handled.add(envelope.id)
  return post($, 'ack', `session:${S.id}`, envelope.from, { state: 'delivered', by }, { thread: envelope.thread, refs: { of: envelope.id } })
}

// A peer nudge to another session: a ping with no content. The receiver consumes it in session.receive.
export const NUDGE = '[orch nudge] look at the stream'

export async function nudgePeer($: any, to: string): Promise<void> {
  if (!to.startsWith('session:')) return
  await $.session.send({ to: { sessionId: to.slice('session:'.length) }, text: NUDGE })
}
