import { query } from '@anthropic-ai/claude-agent-sdk'
import type { CheckDocs, DocsResult, RunTurn } from './supervisor.ts'

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

export function parseDocsReply(text: string): DocsResult {
  const match = text.match(/\{[\s\S]*\}/)
  if (!match) return { answered: false }
  try {
    const value = JSON.parse(match[0])
    return value.answered === true && typeof value.answer === 'string' ? { answered: true, answer: value.answer, source: value.source } : { answered: false }
  } catch {
    return { answered: false }
  }
}

export function sdkDocsChecker(): CheckDocs {
  return async request => {
    const prompt = [
      'You check if the project documentation already answers a question. Do not change any file.',
      `Read only these paths in the working folder, if they exist: ${request.docs.join(', ')}.`,
      `Question: ${request.question}`,
      `Options: ${request.options.join(', ') || 'none given'}`,
      'If the documentation gives a clear answer, reply with only this JSON: {"answered": true, "answer": "<the answer>", "source": "<file and section>"}.',
      'If the answer is not clear in the documentation, reply with only: {"answered": false}.',
    ].join('\n')
    let text = ''
    for await (const message of query({
      prompt,
      options: { cwd: request.cwd, settingSources: ['user'], allowedTools: ['Read', 'Glob', 'Grep'], disallowedTools: ['Edit', 'Write', 'Bash'], maxTurns: 12 },
    })) {
      if (message.type === 'result' && message.subtype === 'success') text = message.result
    }
    return parseDocsReply(text)
  }
}
