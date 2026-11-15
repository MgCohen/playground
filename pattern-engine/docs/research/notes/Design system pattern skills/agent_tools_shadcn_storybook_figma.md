# Agent-era tools that package UI patterns for agents (shadcn, Storybook, Figma, v0, doc platforms)

Research date: 2026-11-15. "Primary" means the vendor's own docs, schema or source code. "Secondary" means an aggregator, a search summary or a competitor's page.

Summary table (the answer first):

| Tool | Unit of the catalog | Pattern-level unit? | Index / search | Selection rules | "No entry fits" rule | Deterministic part | Judgment part |
|---|---|---|---|---|---|---|---|
| shadcn registry + MCP + skill | `registry-item` (types incl. `registry:block`, `registry:page`) | Yes: `registry:block` (composed UI) | `registry.json` `items`; MCP `list`/`search` (fuzzy) | Skill: component-selection table, "use existing components first", search before custom UI, ask which registry | Implicit only: search registries, then custom markup as last resort | JSON schema, CLI `add`, dependency resolution, audit checklist | Which item fits the intent; composition |
| Storybook MCP (10.6, preview) | Component manifest entry + stories | No (components; stories act as examples) | `docs-list` | Agent reads list, then `docs-show`; sample AGENTS.md: ask if undocumented | Ask the user | Manifest build, `test-run` | Component choice |
| Figma Make guidelines | Markdown files in `guidelines/` | Partial: a `composition/` folder, no template | `Guidelines.md` + `overview-*.md` | Per-component "when to use" | None stated | None (prose) | All |
| v0 design systems (legacy registry) | shadcn registry items incl. blocks | Yes: blocks as page starters | Registry | None stated | None stated | Registry JSON | All |
| Chakra UI MCP | Components; Pro templates | Pro templates/blocks (paid) | `list_components` etc. | None found | None found | Tool calls | Choice |
| Mantine | llms.txt, MCP, skills | No evidence | llms.txt index | None found | None found | — | — |
| zeroheight / Supernova / Knapsack MCP | Doc pages, tokens, components | No pattern type found | Doc search | None found | None found | — | — |
| EightShapes / Brad Frost | Doc practice, not a tool | Yes (concept: patterns, recipes) | — | — | Recipes and snowflakes layer | — | — |

## shadcn/ui: registry schema, blocks, MCP server, agent skill

### Takeaway
shadcn is the only tool found with a machine-readable, installable pattern unit (`registry:block`) plus an MCP search and a skill with selection rules. The schema has no "intent" or "when to use" field. Intent lives in free text (`description`, `docs`, `categories`, `meta`), and selection is judgment guided by the skill.

### Cited Findings
- `registry-item.json` top-level fields: `name` (required), `type` (required), `description` ("a brief overview of the item"), `title`, `author`, `dependencies`, `devDependencies`, `registryDependencies`, `files` (`path`, `type`, `target`, optional `content`), `tailwind`, `cssVars`, `css`, `envVars`, `meta` (free key-value), `docs` (markdown string), `categories` (string array), `extends`, `style`/`iconLibrary`/`baseColor`/`theme` (only for `registry:base`), `font` — [registry-item schema, primary](https://ui.shadcn.com/schema/registry-item.json)
- Item `type` enum: `registry:lib`, `registry:block`, `registry:component`, `registry:ui`, `registry:hook`, `registry:theme`, `registry:page`, `registry:file`, `registry:style`, `registry:base`, `registry:font`, `registry:item` — [registry-item schema, primary](https://ui.shadcn.com/schema/registry-item.json)
- No dedicated field for intent or usage. The nearest are `description`, `docs`, `categories` and `meta` — [registry-item schema, primary](https://ui.shadcn.com/schema/registry-item.json)
- `registryDependencies` lists other registry items by shadcn name or by URL. This is the composition field: a block declares the components it is made of — [registry-item schema, primary](https://ui.shadcn.com/schema/registry-item.json)
- `registry.json` (the index): `$schema`, `name`, `homepage`, `include` (paths to other `registry.json` files; item names unique across them), `items` (each a registry-item) — [registry.json docs, primary](https://ui.shadcn.com/docs/registry/registry-json)
- CLI 3.0 (August 2025, older but current base) added namespaced registries (`@registry/name`), private registry auth, `view`, `search` (e.g. `shadcn search @tweakcn -q "dark"`), `list`, and the MCP server — [shadcn changelog, primary](https://ui.shadcn.com/docs/changelog/2025-08-cli-3-mcp)
- MCP capabilities: browse, search across registries, install by natural language; example prompt "Find me a login form from the shadcn registry". Registries are configured in `components.json` `registries` with a `{name}` URL template and optional headers — [shadcn MCP docs, primary](https://ui.shadcn.com/docs/mcp)
- MCP source registers 7 tools: `get_project_registries`, `list_items_in_registries` (filters `registries`, `types`, `limit`, `offset`), `search_items_in_registries` (fuzzy match on `query`), `view_items_in_registries` (name, description, type, file contents), `get_item_examples_from_registries` (query pattern `{item-name}-demo`), `get_add_command_for_items`, `get_audit_checklist` (post-generation checks: imports, deps, lint, TypeScript, Playwright if present) — [MCP source index.ts, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/packages/shadcn/src/mcp/index.ts)
- Tool descriptions chain the agent: after search, call examples; run the audit after all steps. One description points to an `init_project` tool that the server does not register — [MCP source, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/packages/shadcn/src/mcp/index.ts)
- The official agent skill is `skills/shadcn/SKILL.md` in the monorepo; install `npx skills add shadcn-ui/ui --skill shadcn` — [skill file, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/skills/shadcn/SKILL.md); install command from [claudeskills.info, secondary](https://claudeskills.info/skills/shadcn-ui/ui/shadcn/)
- Skill structure: injected project context from `npx shadcn@latest info --json`; Principles ("Use existing components first", compose, built-in variants, semantic colors); Critical Rules; Key Patterns; a Component Selection table (need → component); Workflow (8 steps); Design Systems from DESIGN.md; Updating Components (`--dry-run`, `--diff`). References: `rules/forms.md`, `rules/composition.md`, `rules/chat.md`, `rules/icons.md`, `rules/styling.md`, `rules/base-vs-radix.md`, `cli.md`, `registry.md`, `customization.md`, `design-system.md`, `design-system-page.md` — [SKILL.md, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/skills/shadcn/SKILL.md)
- Skill selection rules: run `npx shadcn@latest search` before custom UI, check community registries, use `view` and `docs` before use. If the user asks for a block without a registry, ask which one; do not pick a default. After add, read every added file and fix issues — [SKILL.md, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/skills/shadcn/SKILL.md)
- `rules/composition.md` has 13 rules, each written as a title plus a correct/incorrect pair, for example: items inside their Group, "Callouts use Alert", "Empty states use Empty component", choose the overlay by use case (`Dialog` vs `AlertDialog`), Dialog/Sheet/Drawer need a Title, full Card structure, loading via `Spinner` + `disabled`, Separator/Skeleton/Badge instead of hand-made markup — [composition.md, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/skills/shadcn/rules/composition.md)
- No explicit "no entry fits" procedure. The skill treats custom markup as a last resort after search, without a defined step — [SKILL.md, primary](https://raw.githubusercontent.com/shadcn-ui/ui/main/skills/shadcn/SKILL.md)

### Inferences
- A shadcn "pattern entry" is: `name` + `type: registry:block` + `title` + `description` + `categories` + `registryDependencies` (the composed components) + `files` (code). Intent and "when to use" must go into `description` or `docs`.
- Deterministic: schema validation, `registry build`, `add` install, dependency resolution, fuzzy search, audit checklist. Judgment: matching intent to an item, composition, the fallback to custom code.
- The rule format in `rules/*.md` (rule title, one line, correct/incorrect code) is a reusable template for composition rules.

### Gaps
- The search ranking fields (which item fields fuzzy search matches) are in formatter code that I did not read.
- I did not read `ui.shadcn.com/llms.txt` or the "Registry Directory" and "Dynamic Search" pages.
- I did not read `references/registry.md` and `design-system.md` in the skill.

## Storybook MCP, Figma Make guidelines, Figma Dev Mode MCP, v0, Chakra, MUI, Mantine

### Takeaway
These tools expose components, not patterns. Figma Make has a `composition/` guidelines folder but no template for it. v0 reuses shadcn blocks as page starters. Chakra has paid templates. No tool here has a "no entry fits" rule, except Storybook's sample AGENTS.md: ask the user.

### Cited Findings
- Storybook MCP (docs version 10.6, AI features in preview, "The API may change"): toolsets Development (`stories-changed`, `get-storybook-story-instructions`, `stories-preview`, `stories-find-by-component`, `review-create`), Docs (`docs-list`, `docs-show`, `docs-show-story`), Testing (`test-run`) — [Storybook MCP overview, primary](https://storybook.js.org/docs/ai/mcp/overview)
- `docs-show` returns props, the first three stories, an index of other stories and extra docs. Manifest generation: `features.componentsManifest: true`; React, Angular-Vite, Vue3-Vite only. Composed Storybooks merge their manifests — [Storybook MCP overview, primary](https://storybook.js.org/docs/ai/mcp/overview)
- Storybook selection is a workflow: `docs-list` → `docs-show` → `docs-show-story`. Sample AGENTS.md: use only documented props or props in stories; ask the user if undocumented — [Storybook MCP overview, primary](https://storybook.js.org/docs/ai/mcp/overview)
- Figma Make guidelines: `Guidelines.md` is read first and routes to other files; `setup.md`; `components/` (one file per component plus `overview.md`); `composition/` (`layout.md`, `surfaces.md`, `hierarchy.md`, `overview.md`); `foundations/` (`color.md`, `spacing.md`, `modes.md`, `icons.md`). "Multiple short guidelines files are better than a few large files" — [Figma help: write design system guidelines, primary](https://help.figma.com/hc/articles/43602393097239)
- Figma's template is for component files only: when to use, semantic purpose, correct and incorrect examples, API. No pattern template and no "no component fits" rule; example text says "Always use design system components over raw HTML elements" — [Figma help, primary](https://help.figma.com/hc/articles/43602393097239)
- Make kits ship the package with guidelines; Figma calls guidelines critical for reliable output — [Figma developer docs: bring your package, primary](https://developers.figma.com/docs/code/bring-your-design-system-package)
- Figma MCP has 24 published tools, covers the canvas and links nodes to code via Code Connect — [Supernova blog, secondary and competitor](https://www.supernova.io/blog/design-system-mcp-servers)
- v0 legacy registry guide (updated 2026-11-13; points to "Design Systems 2.0" as current): a shadcn registry gives components, blocks and tokens. Blocks are page-level starters (blank app, dashboard, store) defined with `files` and `registryDependencies`. No instructions or fallback rules — [v0 design systems docs, primary](https://v0.app/docs/design-systems)
- "Open in v0" sends a block's metadata, files and styles to v0; it does not support `cssVars`, `css`, `envVars` or namespaced registries; v0 "may struggle with any customizations" of shadcn primitives — [shadcn Open in v0 docs, primary](https://ui.shadcn.com/docs/registry/open-in-v0)
- Chakra UI MCP (`@chakra-ui/react-mcp`): docs list `list_components`, `get_component_props`, `get_component_example`, plus two Pro template tools that need `CHAKRA_PRO_API_KEY`. The npm listing shows `get_theme`, `list_blocks`, `v2_to_v3_code_review` — [Chakra MCP docs, primary](https://www.chakra-ui.com/docs/get-started/ai/mcp-server); [socket.dev npm listing, secondary](https://socket.dev/npm/package/@chakra-ui/react-mcp)
- Mantine offers llms.txt (index of per-page `.md`), `llms-full.txt`, `@mantine/mcp-server` and skills. A third-party page lists tools `list_items`, `get_item_doc`, `get_item_props`, `search_docs` — [Mantine LLMs guide, primary](https://mantine.dev/guides/llms); tool names secondary
- MUI: the search found only community MCP servers (for example jgentes, fetches mui.com docs) — [lobehub listing, secondary](https://lobehub.com/mcp/jgentes-mui-mcp-cloudflare)

### Inferences
- Figma Make's `composition/` folder is the nearest official place for pattern guidance outside shadcn, but it is prose with no schema.
- Storybook can carry patterns only if a team writes pattern stories; the manifest has no pattern type.
- Deterministic: Storybook manifest build and `test-run`; registry JSON for v0. Judgment: all selection in all these tools.

### Gaps
- I did not verify the current Storybook manifest field list.
- I did not read v0 "Design Systems 2.0" docs; it can include rules or patterns.
- I did not read Figma Dev Mode MCP tool docs directly (only a secondary count).
- MUI: memory says an official MUI MCP exists, but I found no primary source in this run.
- I did not confirm Chakra Pro template content or `list_blocks` output.

## Design-system documentation platforms (zeroheight, Supernova, Knapsack)

### Takeaway
All three have MCP servers. None of the sources shows a pattern content type or a pattern template. They serve documentation pages, tokens and components.

### Cited Findings
- zeroheight MCP connects Claude, Cursor, Figma Make and Copilot via `mcp.zeroheight.com/mcp`; a local option reads private pages — [zeroheight help, primary, via search snippet; page returned 403](https://help.zeroheight.com/hc/en-us/articles/48004395674011)
- zeroheight returns documentation pages, not entities; 4 read-only tools; 500 calls per month on Free and Starter — [Supernova blog 2026-08-30, secondary and competitor](https://www.supernova.io/blog/design-system-mcp-servers)
- Supernova Relay (remote MCP) launched 2025-09-18 (older); OAuth, Builder+ role, URL `mcp.supernova.io/mcp/ds/{ID}` — [Supernova release notes, primary](https://learn.supernova.io/latest/releases/september-2025/introducing-supernova-relay-lWfNcpwJ); [Supernova MCP docs, primary](https://learn.supernova.io/latest/design-systems/features/mcp-for-design-system-LIHAMhjr-LIHAMhjr)
- Supernova MCP is read and write: documentation, tokens (with resolved values and users), components, usage rules, releases, component code; skills bundled into the server. No patterns or recipes mentioned — [Supernova blog, vendor](https://www.supernova.io/blog/design-system-mcp-servers)
- Knapsack MCP was announced as beta (July 2025, older), self-hosted and open source; it queries documentation, components and tokens — [Knapsack blog, primary](https://www.knapsack.cloud/blog/knapsacks-mcp-server-turns-design-systems-into-production-engines); details from [Supernova blog, secondary](https://www.supernova.io/blog/design-system-mcp-servers)

### Inferences
- On these platforms, a pattern is a documentation page that a team writes. The MCP returns it as page text. The structure depends on the team's own page template.

### Gaps
- I did not find zeroheight or Knapsack pattern-page templates; zeroheight help pages returned 403.
- The tool names for the Supernova and Knapsack servers are not listed in the sources I read.

## Published pattern documentation templates and agent use

### Takeaway
Brad Frost's "recipes" give the concept of a pattern layer above the core system. I found no published pattern-entry template from EightShapes, and no agent tool that uses these models.

### Cited Findings
- Recipes are UI patterns built fully or partly from system ingredients (components, tokens, icons, styles) and form their own layer — [Brad Frost, The art of design system recipes, primary via search summary](https://bradfrost.com/blog/post/the-art-of-design-system-recipes/)
- A separate recipe layer stops product-specific components from filling the core; recipes can graduate into core components or stay product-specific — [Brad Frost, primary via search summary](https://bradfrost.com/blog/post/the-art-of-design-system-recipes/)
- Ecosystem layers: core system, then implementation, recipes, smart components, product — [Brad Frost, The design system ecosystem, primary via search summary](https://bradfrost.com/blog/post/the-design-system-ecosystem/)
- Keep recipes and "snowflakes" in a separate extras library — [Brad Frost, components, recipes and snowflakes, primary via search summary](https://bradfrost.com/?p=13989)
- Nathan Curtis wrote "UX Patterns ≠ UI Components" and "Documenting Components" (2018) and "Component Specifications" (2023); his 2019 deck lists UX patterns as a system category — [EightShapes articles index, secondary search summary](https://eightshapes.com/articles); [Beyond the Toolkit deck](https://speakerdeck.com/nathanacurtis/beyond-the-toolkit-tel-aviv-february-2019)

### Inferences
- Frost's recipes layer and the snowflake idea are the nearest published model for the "no entry fits" case: build a product-local recipe, then promote it if it proves general.
- shadcn's `registry:block` with `registryDependencies` is the closest machine form of a recipe.

### Gaps
- The Curtis pattern article returned 403. I could not confirm its recommended sections.
- I did not open the Frost pages directly; the findings are search summaries of them.
- I found no agent tool that uses an EightShapes or Frost template.
