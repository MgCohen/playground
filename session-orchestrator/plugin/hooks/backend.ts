// The store backend of the plugin hooks: the only hook module with store IO ($.fs).
// Version 1 and version 2 both use local files here: a hook has a 10 s budget, so the plugin never calls Notion.
// The other modules name an entity (a session file, an envelope, a template), and this module knows the paths.
import { S, now } from './state.ts'

// The files in the folder of one session.
export type SessionFile = 'lease.json' | 'turn.json' | 'status.json' | 'meta.json' | 'control.json' | 'last-output.json'

export type TemplateFile = 'meta' | 'schema'

// A write that breaks a store rule: a second create of an id, or a save of an old version.
export class StoreConflict extends Error {}

async function readText($: any, path: string): Promise<string | undefined> {
  return (await $.fs.exists(path)) ? String(await $.fs.read(path)) : undefined
}

async function readJson($: any, path: string): Promise<any> {
  const text = await readText($, path)
  if (text === undefined) return undefined
  try {
    return JSON.parse(text)
  } catch {
    return { text }
  }
}

function json(value: unknown): string {
  return JSON.stringify(value, null, 2)
}

// Rule 1: a create of an id that exists fails (claims, first answer).
async function create($: any, path: string, text: string): Promise<void> {
  if (await $.fs.exists(path)) throw new StoreConflict(`${path}: the id exists`)
  await $.fs.write(path, text)
}

// Rule 2: if the entity on disk has a version, a save with an old version fails. With no expected version, the save
// replaces the file. No hook file has a version on the C# side yet; a caller passes the version that it read.
async function replace($: any, path: string, value: unknown, expected?: number): Promise<void> {
  if (expected !== undefined) {
    const old = await readJson($, path)
    const version = typeof old?.version === 'number' ? old.version : undefined
    if (version !== undefined && version !== expected) throw new StoreConflict(`${path}: version ${expected} is old, the store has ${version}`)
  }
  await $.fs.write(path, json(value))
}

export async function storeRoot($: any): Promise<string> {
  return (await $.env.get('ORCH_STORE')) ?? `${S.home}/store`
}

export async function sessionDir($: any): Promise<string> {
  return `${await storeRoot($)}/sessions/${await $.session.id()}`
}

// The store contract version (version.json), read before the plugin activates.
export async function storeVersion($: any): Promise<any> {
  return readJson($, `${await storeRoot($)}/version.json`)
}

export async function readSession($: any, file: SessionFile, dir: string | undefined = S.dir): Promise<any> {
  return readJson($, `${dir}/${file}`)
}

export async function writeSession($: any, file: SessionFile, value: unknown, dir: string | undefined = S.dir, expectedVersion?: number): Promise<void> {
  await replace($, `${dir}/${file}`, value, expectedVersion)
}

export async function isEnabled($: any, dir: string | undefined = S.dir): Promise<boolean> {
  return $.fs.exists(`${dir}/enabled`)
}

export async function enable($: any, dir: string): Promise<void> {
  await $.fs.write(`${dir}/enabled`, '')
}

export async function writeHeartbeat($: any, dir: string | undefined = S.dir): Promise<void> {
  await $.fs.write(`${dir}/heartbeat.txt`, await now($))
}

export async function writeMirror($: any, ms: number, uuid: unknown, row: unknown): Promise<void> {
  await $.fs.write(`${S.dir}/mirror/${ms}-${uuid}.json`, json(row))
}

// The lifecycle control of the session: active, stopped or archived (control.json).
export async function controlState($: any): Promise<string> {
  if (!S.dir) return 'active'
  const control = await readSession($, 'control.json')
  return typeof control?.state === 'string' ? control.state : 'active'
}

// The log of this process (log-<holder>.txt). It keeps the last 100,000 characters when it grows past 200,000.
export async function log($: any, line: string): Promise<void> {
  if (!S.dir) return
  const path = `${S.dir}/log-${S.holder}.txt`
  const old = (await readText($, path)) ?? ''
  const kept = old.length > 200000 ? old.slice(-100000) : old
  await $.fs.write(path, `${kept}${await now($)} ${line}\n`)
}

export async function readPolicy($: any): Promise<any> {
  return S.store ? readJson($, `${S.store}/policy.json`) : undefined
}

export async function readTemplate($: any, kind: string, file: TemplateFile): Promise<any> {
  return readJson($, `${S.home}/templates/${kind}/${file}.json`)
}

// An envelope: the body first, then the envelope. The envelope is the commit point. Both are creates.
export async function createEnvelope($: any, day: string, id: string, body: unknown, envelope: Record<string, unknown>): Promise<void> {
  await create($, `${S.store}/${envelope.body}`, json(body))
  await create($, `${S.store}/events/${day}/${id}.json`, json(envelope))
}

export function bodyPath(day: string, id: string): string {
  return `bodies/${day}/${id}.json`
}
