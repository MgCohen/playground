import { S, byName, log, names, now, readJson, readText, writeJson, type Decision } from './state.ts'

export function answerText(answer: any): string {
  if (answer === undefined || answer === null) return ''
  if (typeof answer.answer === 'string') return typeof answer.note === 'string' ? `${answer.answer}\n\n${answer.note}` : answer.answer
  if (typeof answer.text === 'string') return answer.text
  return JSON.stringify(answer.answers ?? answer)
}

export async function markDelivered($: any, id: string, by: string): Promise<void> {
  if (!S.dir) return
  await writeJson($, `${S.dir}/delivered/${id}.json`, { by, at: await now($) })
}

export async function waitForFile($: any, path: string, seconds: number): Promise<string | undefined> {
  const script = `$f='${path}'; $end=(Get-Date).AddSeconds(${seconds}); while (-not (Test-Path $f) -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 1000 }; if (Test-Path $f) { Get-Content -Raw $f }`
  const run = await $.process.run(['powershell', '-NoProfile', '-Command', script], { timeoutMs: Math.min(600000, (seconds + 15) * 1000) })
  if (run.exitCode !== 0) await log($, `wait exit=${run.exitCode} err=${String(run.stderr ?? '').slice(0, 200)}`)
  const text = String(run.stdout ?? '').trim()
  return text.length > 0 ? text : undefined
}

async function submit($: any, text: string): Promise<boolean> {
  const result = await $.prompt.submit({ text })
  if (result && typeof result.drop === 'string') {
    if (S.dropped !== result.drop) {
      await log($, `submit dropped, will try again: ${result.drop}`)
      await writeJson($, `${S.dir}/blocked.json`, { reason: result.drop, since: await now($) })
    }
    S.dropped = result.drop
    return false
  }
  if (S.dropped !== '') await writeJson($, `${S.dir}/blocked.json`, { reason: null, since: await now($) })
  S.dropped = ''
  return true
}

async function drainInbox($: any): Promise<void> {
  if (!S.dir) return
  for (const name of await names($, `${S.dir}/inbox`)) {
    if (S.cursor !== '' && byName(name, S.cursor) <= 0) continue
    const raw = (await readText($, `${S.dir}/inbox/${name}`)) ?? ''
    const text = name.endsWith('.json') ? answerText(JSON.parse(raw)) : raw
    if (!(await submit($, text))) return
    S.cursor = name
    await $.fs.write(`${S.dir}/cursor.txt`, name)
    await log($, `inbox ${name} submitted`)
  }
}

async function deliverAnswers($: any): Promise<void> {
  if (!S.dir) return
  for (const name of await names($, `${S.dir}/decisions`)) {
    const id = name.replace(/\.json$/, '')
    if (await $.fs.exists(`${S.dir}/delivered/${id}.json`)) continue
    const decision: Decision | undefined = await readJson($, `${S.dir}/decisions/${name}`)
    if (!decision || decision.mode !== 'pending') continue
    const answer = await readJson($, `${S.dir}/answers/${id}.json`)
    if (answer === undefined) continue
    const text = `Answer to decision ${id} (${decision.question ?? 'question'}): ${answerText(answer)}`
    if (!(await submit($, text))) return
    await markDelivered($, id, 'inbox')
    await log($, `decision ${id} answer submitted`)
  }
}

export async function tick($: any): Promise<void> {
  if (!S.dir || S.busy || !S.idle || S.holder !== 'live') return
  S.busy = true
  try {
    await drainInbox($)
    await deliverAnswers($)
  } catch (error) {
    await log($, `tick failed: ${String(error)}`)
  } finally {
    S.busy = false
  }
}
