// Converts the p5 events.json (payload inline) to the new shape: stream/events/<day>/<id>.json + stream/bodies/<day>/<id>.json
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
const subject = d => String(d.question ?? d.summary ?? d.title ?? d.text ?? '').replace(/\s+/g, ' ').trim().slice(0, 80)
for (const e of JSON.parse(readFileSync('events.json', 'utf8'))) {
  const data = e.data ?? {}
  if (e.kind === 'session.output' && !data.text) continue
  const day = e.at.slice(0, 10)
  const body = `bodies/${day}/${e.id}.json`
  mkdirSync(`stream/bodies/${day}`, { recursive: true }); mkdirSync(`stream/events/${day}`, { recursive: true })
  writeFileSync(`stream/${body}`, JSON.stringify(data, null, 2))
  const { data: _, ...meta } = e
  writeFileSync(`stream/events/${day}/${e.id}.json`, JSON.stringify({ ...meta, subject: subject(data), body }, null, 2))
}
