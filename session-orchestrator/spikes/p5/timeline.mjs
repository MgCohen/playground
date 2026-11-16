// Prints a store as a timeline: node timeline.mjs <store folder>. Reads only the event files, not the bodies.
import { readFileSync, readdirSync } from 'node:fs'
const root = process.argv[2]
const events = readdirSync(`${root}/events`).flatMap(day => readdirSync(`${root}/events/${day}`).map(n => JSON.parse(readFileSync(`${root}/events/${day}/${n}`, 'utf8')))).sort((a, b) => a.id.localeCompare(b.id, 'en', { numeric: true }))
const titles = {}
for (const e of events) if (e.kind === 'brief') titles[e.to.replace('session:', '')] = JSON.parse(readFileSync(`${root}/${e.body}`, 'utf8')).title
const name = who => who.startsWith('session:') ? `${titles[who.slice(8)] ?? '?'} (${who.slice(8, 16)})` : who
const t0 = Date.parse(events[0].at)
for (const e of events) {
  const seconds = String(Math.round((Date.parse(e.at) - t0) / 1000)).padStart(4)
  console.log(`${seconds}s  ${e.kind.padEnd(15)}${`${name(e.from)} → ${name(e.to)}`.padEnd(52)}${e.subject.slice(0, 45).padEnd(46)}${e.body.split('/').pop().slice(14, 22)}`)
}
