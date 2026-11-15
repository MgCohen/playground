import { query } from '@anthropic-ai/claude-agent-sdk'
for await (const m of query({ prompt: 'Add GRAPE (supervisor SDK turn 7). Reply only with the full list.', options: { cwd: 'C:/Users/matheus.cohen/Documents/Projects/playground/session-orchestrator/spikes', settingSources: ['user'], resume: '6ad6c434-9b1a-44cc-b313-0f5b8a19dac1' } }))
  if (m.type === 'result') console.log('SDK result:', JSON.stringify(m.result))
