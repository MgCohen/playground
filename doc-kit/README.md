# Doc kit

One simple look for the blocks that our documents use. `kit.js` draws each block from a JSON file.

Gallery: https://claude.ai/artifact/JhMVKhiiGxUQrNJf6cnv6p

| Path | Content |
|---|---|
| `kit.css` | Tokens (light and dark) and the style of all blocks. Class prefix `dk-` |
| `kit.js` | `DocKit.render(el, type, data)`, `DocKit.fit(root)`, `DocKit.block(b)`. An element with `data-dk` and `data-src` is drawn on load |
| `data/*.json` | Real content of the six reference blocks |
| `index.html` | The gallery page: each block and its data shape |
| `doc.html` | The doc template: draws `doc.json` |
| `tools/notion2doc.mjs` | Notion page → `doc.json` (REST API, no packages) |
| `tools/build.mjs` | `doc.json` → a folder to preview or publish |
| `examples/` | `doc.json` files |
| `tools/extract.py` | Makes `data/*.json` from the saved source artifacts |
| `walkthrough/` | The walkthrough page, its copies of the kit, and `shots/*.png`. `local.html` is a doctype copy for a local preview |

## The two ways to make a doc

```
Notion page (from "Doc kit · Template")  ──► tools/notion2doc.mjs ──► doc.json ──► tools/build.mjs ──► folder: index.html + kit + doc.json
Claude writes doc.json (artifact side)  ─────────────────────────────┘                                    ├─► local preview
                                                                                                           └─► artifact
```

| Item | Where |
|---|---|
| Notion template | https://app.notion.com/p/41a91e245786819da063dd4956191390 (Lab) |
| Notion sample: 4-bit ripple-carry adder | https://app.notion.com/p/41a91e2457868137ba84f5f5e871a703 (Lab), exported to `examples/notion-sample.json` |
| Adder, rendered by the kit | https://claude.ai/artifact/Lxk8FpcaMxv9F2tDdENwWR |
| Traffic light controller, written as doc.json | https://claude.ai/artifact/BjCJtS6BRBPYweXE5GFuMK (source `examples/spike-report.json`) |
| Walkthrough of the spike: ways, architecture, screenshots, findings | https://claude.ai/artifact/VmHm2a6U9Z3hkTiyAyZddY (source `walkthrough/`) |
| Mapping rules | `CONVENTIONS.md` |

### From Notion

1. Duplicate the template page in Notion and write the content. Follow `CONVENTIONS.md`.
2. Export: `with-secrets.sh node tools/notion2doc.mjs <page id or url> examples/<name>.json` (needs `NOTION_TOKEN`; the page must be in an area that the "Claude workflow" connection can read).
3. Build: `node tools/build.mjs examples/<name>.json out/<name>`. Add `--doctype` for a local preview.
4. Publish `out/<name>/index.html` as an artifact, with `kit.css`, `kit.js` and `doc.json` as files.

### As an artifact

1. Write `doc.json`: `title`, `summary`, `sections` of `{ title, blocks }`. A block is a content block (`p`, `h`, `callout`, `code`, `list`, `kv`, `facts`, `table`, `cols`) or a kit block `{ "type": "tree" | "columns" | "graph" | "stack" | "folds", "data": … }`.
2. Do steps 3 and 4 above.

## Blocks

| Type | Data | Source artifact |
|---|---|---|
| `tree` | `rows` of `path`, `desc`, `kind`, `when`, `entry`, `install` | Umpire Folder Layout |
| `columns` | `groups` of `items`, optional `bar` and `flags` | VSA Split Map |
| `graph` | `rows` (columns in each row), `nodes` with `row` and `col`, `edges` | Q6 Anchor and Host Types |
| `stack` | `groups` of `stacks`, each a tree of `call`, `note`, `children` | Orchestrator Internals |
| `folds` | `items` of `title`, `summary`, `tags`, `body`, `children` | Unity Test Suite Map, Umpire File Map |

Content blocks in a fold body: `p`, `code`, `list`, `kv`, `facts`, `table`, `cols`. Inline text uses `` `code` `` and `**bold**` only.

Wide code and wide call stacks become smaller until they fit the width, not below 7px.

## Publish

Publish `index.html` with `kit.css`, `kit.js` and `data/*.json` as files of the same artifact.

## Preview

Serve this folder: `python -m http.server 5311 --directory doc-kit`. Then open `http://127.0.0.1:5311/`. The pages have no doctype, because the artifact host adds it. For a local layout check, build with `--doctype`, or make a copy with `<!doctype html>` on the first line.
