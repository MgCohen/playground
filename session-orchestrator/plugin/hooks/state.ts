export const DEFAULT_HOME = 'C:/Users/matheus.cohen/Documents/Projects/playground/session-orchestrator'
export const TICK_MS = 3000
export const MAX_WAIT_SECONDS = 580
export const ROW_CHARS = 4000

export const S: {
  id?: string
  dir?: string
  store?: string
  home: string
  cwd?: string
  holder: string
  busy: boolean
  idle: boolean
  dropped: string
  timers?: boolean
  watching: boolean
  wanted: boolean
  handled: Set<string>
  inputFrom: string
  lastMs: number
  pid?: number
  started?: string
} = {
  dropped: '',
  holder: 'live',
  home: DEFAULT_HOME,
  busy: false,
  idle: true,
  watching: false,
  wanted: false,
  handled: new Set<string>(),
  inputFrom: '',
  lastMs: 0,
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

// The lifecycle control of the session: active, stopped or archived (control.json).
export async function controlState($: any): Promise<string> {
  if (!S.dir) return 'active'
  const control = await readJson($, `${S.dir}/control.json`)
  return typeof control?.state === 'string' ? control.state : 'active'
}

export function newId(): string {
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = '89ab'.charAt(Math.floor(Math.random() * 4))
  const h = hex.join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

export async function storeRoot($: any): Promise<string> {
  return (await $.env.get('ORCH_STORE')) ?? `${S.home}/store`
}

export async function homeRoot($: any): Promise<string> {
  return (await $.env.get('ORCH_HOME')) ?? DEFAULT_HOME
}

export async function sessionDir($: any): Promise<string> {
  return `${await storeRoot($)}/sessions/${await $.session.id()}`
}

export async function orch($: any, argv: string[], timeoutMs = 60000): Promise<{ ok: boolean; text: string; code: number }> {
  const run = await $.process.run(['orch', ...argv], { timeoutMs })
  const text = String(run.exitCode === 0 ? run.stdout ?? '' : run.stderr || run.stdout || '').trim()
  return { ok: run.exitCode === 0, text, code: run.exitCode }
}
