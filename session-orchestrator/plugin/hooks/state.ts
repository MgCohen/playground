export const HOME = 'C:/Users/matheus.cohen/Documents/Projects/playground/session-orchestrator'
export const DEFAULT_STORE = `${HOME}/store`
export const TICK_MS = 3000
export const MAX_WAIT_SECONDS = 580
export const ROW_CHARS = 4000

export type Decision = {
  id: string
  session: string
  kind: 'ask_user'
  question?: string
  context?: string
  snippet?: string
  options?: string[]
  recommendation?: string
  risk: string
  defaultOption?: string
  mode: 'wait' | 'pending'
  waitSeconds: number
  asked: string
}

export const S: { id?: string; dir?: string; store?: string; cwd?: string; holder: string; cursor: string; busy: boolean; idle: boolean; dropped: string; timers?: boolean } = {
  dropped: '',
  holder: 'live',
  cursor: '',
  busy: false,
  idle: true,
}

export async function now($: any): Promise<string> {
  return new Date(await $.clock.now()).toISOString()
}

export async function readText($: any, path: string): Promise<string | undefined> {
  return (await $.fs.exists(path)) ? String(await $.fs.read(path)) : undefined
}

export async function readJson($: any, path: string): Promise<any> {
  const text = await readText($, path)
  if (text === undefined) return undefined
  try {
    return JSON.parse(text)
  } catch {
    return { text }
  }
}

export async function writeJson($: any, path: string, value: unknown): Promise<void> {
  await $.fs.write(path, JSON.stringify(value, null, 2))
}

export async function log($: any, line: string): Promise<void> {
  if (!S.dir) return
  const path = `${S.dir}/log-${S.holder}.txt`
  const old = (await readText($, path)) ?? ''
  const kept = old.length > 200000 ? old.slice(-100000) : old
  await $.fs.write(path, `${kept}${await now($)} ${line}\n`)
}

export function byName(a: string, b: string): number {
  return a.localeCompare(b, 'en', { numeric: true })
}

export async function names($: any, folder: string): Promise<string[]> {
  if (!(await $.fs.exists(folder))) return []
  const entries = await $.fs.list(folder)
  return entries.map((x: any) => String(x.name)).filter((name: string) => !name.endsWith('.tmp')).sort(byName)
}

export function newId(): string {
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = '89ab'.charAt(Math.floor(Math.random() * 4))
  const h = hex.join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

export async function storeRoot($: any): Promise<string> {
  return (await $.env.get('ORCH_STORE')) ?? DEFAULT_STORE
}

export async function sessionDir($: any): Promise<string> {
  return `${await storeRoot($)}/sessions/${await $.session.id()}`
}
