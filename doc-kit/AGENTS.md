# Doc kit: instructions for agents

Read `README.md` first, then `CONVENTIONS.md`.

## Layout

| Path | Content |
|---|---|
| `kit.css`, `kit.js` | The kit: tokens, styles and the block renderer |
| `index.html`, `data/` | The gallery of the six reference blocks and their data |
| `doc.html` | The doc template: draws `doc.json` |
| `tools/` | `notion2doc.mjs` (Notion page → `doc.json`), `build.mjs` (`doc.json` → a folder to publish), `extract.py` |
| `examples/` | `doc.json` files of the two samples |
| `walkthrough/` | The walkthrough page of the spike, with copies of the kit and the screenshots |

## Rules

- Keep all files of this project in this folder.
- Do not put a build output in git. `out/` is ignored. Make it again with `tools/build.mjs`.
- `walkthrough/kit.css` and `walkthrough/kit.js` are copies. If you change the kit, copy the files again.
- The exporter needs `NOTION_TOKEN`. Give it with the `core:secrets` wrapper. Never write the token in a file.
- Do not add a doctype to a page that you publish as an artifact. The host adds it.

## Before you report a change

1. Build each example: `node tools/build.mjs examples/<name>.json out/<name> --doctype`.
2. Open the gallery and each built example at 1280 px and at 390 px.
3. Make sure that the page does not scroll sideways and that each block draws.
