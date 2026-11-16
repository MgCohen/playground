import { S, newId, now, readJson, writeJson } from './state.ts'

export async function emit($: any, kind: string, from: string, to: string, data: Record<string, unknown>, refs: Record<string, unknown> = {}): Promise<string> {
  if (!S.store) return ''
  const ms = await $.clock.now()
  const at = new Date(ms).toISOString()
  const id = `${ms}-${newId().slice(0, 8)}-${S.holder}`
  const body = `bodies/${at.slice(0, 10)}/${id}.json`
  await writeJson($, `${S.store}/${body}`, data)
  await writeJson($, `${S.store}/events/${at.slice(0, 10)}/${id}.json`, { id, at, session: S.id, kind, from, to, subject: subject(data), body, refs })
  return id
}

export function subject(data: Record<string, unknown>): string {
  const text = String(data.question ?? data.summary ?? data.title ?? data.text ?? '')
  return text.replace(/\s+/g, ' ').trim().slice(0, 80)
}

export function blocksOf(message: any): any[] {
  const content = message?.content ?? message?.message?.content
  if (typeof content === 'string') return [{ type: 'text', text: content }]
  return Array.isArray(content) ? content : []
}

export function textOf(message: any): string {
  return blocksOf(message)
    .filter((block: any) => block.type === 'text' && typeof block.text === 'string')
    .map((block: any) => block.text)
    .join(' ')
    .trim()
}

export function rowText(message: any): string {
  const parts = blocksOf(message).map((block: any) =>
    block.type === 'text' ? block.text : block.type === 'tool_result' ? '[tool result]' : '',
  )
  return parts.filter((text: string) => text !== '').join(' ')
}

export async function inputSender($: any, origin: string): Promise<{ from: string; turnStart?: string }> {
  if (S.holder !== 'supervisor' || !S.dir) return { from: origin }
  const turn = await readJson($, `${S.dir}/turn.json`)
  return typeof turn?.from === 'string' ? { from: turn.from, turnStart: turn.event } : { from: 'supervisor' }
}

export async function emitRow($: any, e: any): Promise<void> {
  if (e.door === 'prompt') {
    const origin = String(e.origin?.kind ?? 'unknown')
    const sender = await inputSender($, origin)
    const refs: Record<string, unknown> = { row: e.uuid }
    if (sender.turnStart) refs.turnStart = sender.turnStart
    await emit($, 'session.input', sender.from, `session:${S.id}`, { text: rowText(e.message), origin }, refs)
    return
  }
  if (e.door !== 'response') return
  const to = S.holder === 'live' ? 'user' : 'supervisor'
  for (const block of blocksOf(e.message)) {
    if (block.type !== 'tool_use') continue
    const input = JSON.stringify(block.input ?? {}).slice(0, 2000)
    await emit($, 'session.tool', `session:${S.id}`, to, { text: String(block.name), name: block.name, input }, { row: e.uuid, tool: block.id })
  }
  const text = textOf(e.message)
  if (text === '') return
  const id = await emit($, 'session.output', `session:${S.id}`, to, { text }, { row: e.uuid })
  await writeJson($, `${S.dir}/last-output.json`, { event: id, at: await now($) })
}
