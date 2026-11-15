import { query } from '@anthropic-ai/claude-agent-sdk'
import type { RunTurn } from './supervisor.ts'

export function sdkRunner(storeRoot: string): RunTurn {
  return async request => {
    const started = Date.now()
    const session = request.isNew ? { sessionId: request.id } : { resume: request.id }
    let text: string | undefined
    let error: string | undefined
    for await (const message of query({
      prompt: request.prompt,
      options: {
        cwd: request.cwd,
        settingSources: ['user'],
        ...session,
        env: { ...process.env, ORCH_HOLDER: 'supervisor', ORCH_STORE: storeRoot },
      },
    })) {
      if (message.type !== 'result') continue
      if (message.subtype === 'success') text = message.result
      else error = message.subtype
    }
    const seconds = Math.round((Date.now() - started) / 1000)
    return error === undefined ? { ok: true, text, seconds } : { ok: false, error, seconds }
  }
}
