// Prints an events.json file (GET /api/events) as a timeline: node timeline.mjs events.json
import { readFileSync } from 'node:fs'
const events = JSON.parse(readFileSync(process.argv[2], 'utf8'))
const t0 = Date.parse(events[0].at)
for (const e of events) {
  const d = e.data ?? {}
  const text = String(d.text ?? d.question ?? d.summary ?? '').replace(/\s+/g, ' ').slice(0, 70)
  const seconds = String(Math.round((Date.parse(e.at) - t0) / 1000)).padStart(4)
  console.log(`${seconds}s  ${e.kind.padEnd(15)}${`${e.from} → ${e.to}`.padEnd(50)}${text}`)
}
