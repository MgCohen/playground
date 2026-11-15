# Pattern models and agent models: Polaris, Primer, SLDS, Fluent

Research date: 2026-11-15. Primary sources are marked [P]. Secondary sources are marked [S]. Older information is marked [older].

## Summary table: what each system gives an agent about patterns

| System | Pattern unit | Agent artifact | Agent covers patterns? | Selection rule | Deterministic check |
|---|---|---|---|---|---|
| Polaris (Shopify) | Templates and compositions | `shopify` skill, Dev MCP | Yes, names only in skill | Free text, "Use cases" lists | `validate.mjs`, mandatory |
| Primer (GitHub) | Scenario and UI patterns | `@primer/mcp` 1.1.2 | Yes, two tools | "Prefer scenario, fall back to UI" | `lint_css` tool (stylelint) |
| SLDS (Salesforce) | Blueprints; SLDS 2 "patterns" | `design-systems-slds-*` skills | Blueprints only | Decision tree, 4 levels | SLDS Linter, mandatory |
| Fluent 2 (Microsoft) | No pattern section | Contributor AGENTS.md; Blazor MCP | No | None | None found for consumers |

## Polaris: pattern page sections, and do the agent tools cover patterns?

### Takeaway
Shopify has two pattern generations. The current one (shopify.dev, Polaris web components) has "templates" (full pages) and "compositions" (groups of components), each page with fixed sections. The Shopify AI Toolkit skill names all patterns but tells the agent only to search docs and validate code; it gives no pattern choice rule.

### Cited Findings

**Current model (shopify.dev App Home, API v2.0) [P]**
- The patterns index defines two types: "Templates: Full page patterns for common Shopify app pages" and "Compositions: Smaller groupings of web components and APIs that solve specific tasks". — [Shopify App Home patterns (md)](https://shopify.dev/docs/api/app-home/patterns.md)
- The index says each pattern meets Built for Shopify requirements "out of the box". It tells the developer to use a template "as your starting point". — [Shopify App Home patterns](https://shopify.dev/docs/api/app-home/patterns)
- Templates: Homepage, Index, Details, Settings. Compositions: Account connection, App card, Callout card, Empty state, Footer help, Index table, Interstitial nav, Media card, Metrics card, Resource list, Setup guide. — [Shopify App Home patterns (md)](https://shopify.dev/docs/api/app-home/patterns.md); [Shopify skill reference polaris-app-home.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/references/polaris-app-home.md)
- Template page sections (Homepage), in order: description, "Related Components (14) APIs (2) Compositions (5)", "Supported components", "Available APIs", "Recommended compositions", "Use cases", "Examples" (each example in jsx and html). — [Homepage template (md)](https://shopify.dev/docs/api/app-home/patterns/templates/homepage.md)
- Composition page sections (Media card) mirror the template page. The difference is "Recommended templates" in place of "Recommended compositions". Its examples combine the composition with APIs, for example "Confirm delete with Modal API" and "Copy link with Toast API". — [Media card composition (md)](https://shopify.dev/docs/api/app-home/patterns/compositions/media-card.md)
- Homepage "Use cases" are bullets, for example "Surfacing items that need merchant attention". — [Homepage template (md)](https://shopify.dev/docs/api/app-home/patterns/templates/homepage.md)
- Pattern relations are explicit two-way links: a template lists its recommended compositions, and a composition lists its recommended templates. — [Homepage](https://shopify.dev/docs/api/app-home/patterns/templates/homepage.md); [Media card](https://shopify.dev/docs/api/app-home/patterns/compositions/media-card.md)
- Each page has an `.md` twin with front matter (`title`, `description`, `api_version`, `source_url.md`). This is an agent-readable form of each pattern. — [Media card (md)](https://shopify.dev/docs/api/app-home/patterns/compositions/media-card.md)

**Earlier model (polaris.shopify.com, Polaris React) [P, older]**
- The repo `Shopify/polaris-react` redirects to `Shopify/polaris-react-archive`, which is archived (last push 2026-09-11). — [GitHub API, Shopify/polaris-react](https://api.github.com/repos/Shopify/polaris-react)
- The index defined patterns as "Preferred solutions to common merchant goals in the admin". Patterns: app settings layout, card layout, common actions, date picking, new features, resource details layout, resource index layout. — [patterns/index.mdx](https://raw.githubusercontent.com/Shopify/polaris-react-archive/main/polaris.shopify.com/content/patterns/index.mdx)
- A contribution template exists: `variant.md.template`. Its sections: description; "How it helps merchants" (image, ordered list); "Use when merchants need to:" (two-column definition table); "Using this pattern" ("This pattern uses the ... components", live preview code); "Useful to know" (side-by-side text and image). — [variant.md.template](https://raw.githubusercontent.com/Shopify/polaris-react-archive/main/polaris.shopify.com/content/patterns/variant.md.template)
- A pattern had variants. For example, Date picking has single date, date range and date list, plus "Related resources" and a GitHub Discussions link. — [date-picking/index.mdx](https://raw.githubusercontent.com/Shopify/polaris-react-archive/main/polaris.shopify.com/content/patterns/date-picking/index.mdx)

**Agent artifacts [P]**
- The Dev MCP setup page gives only `npx -y @shopify/dev-mcp@latest` and tells the user to "tell your agent to use available tools". It does not name tools or patterns. — [Use Polaris with the Shopify Dev MCP server](https://shopify.dev/docs/api/polaris/using-mcp)
- The Shopify AI Toolkit replaces per-surface skills with one `shopify` skill. The agent "reads the guidance for a surface only when a task needs it". — [Shopify AI Toolkit](https://shopify.dev/docs/apps/build/devmcp)
- The toolkit repo ships plugin manifests for Claude, Codex, Cursor, Gemini and other hosts, plus `.mcp.json`, hooks and `skills/shopify/`. — [Shopify/shopify-ai-toolkit](https://github.com/Shopify/shopify-ai-toolkit)
- The skill workflow has four steps: pick the topic from a routing table, read `references/<topic>.md`, search with `search_docs.mjs`, validate with `validate.mjs`. — [shopify SKILL.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/SKILL.md)
- The skill calls the reference file "the topic's instruction set, not a lookup for when you get stuck". — [shopify SKILL.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/SKILL.md)
- Validation rule: "Validation is mandatory". On failure: search for the exact field, fix it, run again. After three attempts, "return your best effort with an explanation". — [shopify SKILL.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/SKILL.md)
- The App Home reference lists the patterns by name only, under a "Patterns" heading. It says Polaris App Home has "ready to use UI design patterns and templates". It gives no choice rule. — [polaris-app-home.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/references/polaris-app-home.md)
- Toolkit changelog 2.2.0: "keep App Home version guidance synchronized with the API catalog". — [CHANGELOG.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/CHANGELOG.md)
- [S] A community skill copies this loop: catalog lookup, MCP docs search, write markup, MCP validation, stop after two failures. — [skills.sh polaris-web-components](https://skills.sh/preetamnath/agent-skills/polaris-web-components)

### Inferences
- Shopify gives the agent pattern names plus a docs search. The agent must search for the pattern page to get its components and examples. The routing table chooses a surface, not a pattern.
- Pattern selection is free text: "Use cases" bullets on each page. There is no "when not to use" section and no decision tree in the current model. The archived model had "Use when merchants need to:".
- The deterministic layer is the validator: it checks components and props against a versioned catalog, not pattern conformance.

### Gaps
- No current public contribution template for a new template or composition on shopify.dev. The docs generator source is not public.
- I did not read the `search_docs.mjs` or `validate.mjs` source. I cannot confirm whether search ranks pattern pages.
- Polaris codemods (`@shopify/polaris-migrator`) were not checked in this session.

## Primer: section headings, and does Primer have an MCP server, agent instructions or skills?

### Takeaway
Primer has two pattern kinds: "scenario patterns" (user tasks: Copy, Create, Delete, ...) and "UI patterns" (Forms, Empty states, Loading, ...). The official `@primer/mcp` server has `list_patterns` and `get_pattern`, and its tool text gives an explicit fallback rule: prefer a scenario pattern, else a UI pattern.

### Cited Findings

**Pattern model [P]**
- Scenario patterns in `@primer/mcp`: Copy, Create, Customize, Delegate, Delete, Edit, Filter, Search, View. UI patterns: Data Visualization, Degraded Experiences, Empty States, Feature Onboarding, Forms, Loading, Navigation, Notification message, Progressive disclosure, Saving. — [primer.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/primer.ts)
- Scenario patterns are served from `/product/scenario-patterns/<id>`. UI patterns are served from `/product/ui-patterns/<id>`. — [primer.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/primer.ts)
- Commit "Add missing MCP scenario patterns (#8342)" is dated 2026-08-31. — [GitHub commits, primer.ts](https://github.com/primer/react/commits/main/packages/mcp/src/primer.ts)
- The UI patterns index describes them as "Design guidelines covering common user workflows". — [ui-patterns/index.mdx](https://raw.githubusercontent.com/primer/design/main/content/ui-patterns/index.mdx)
- Scenario pattern headings (Delete): "Where this happens", "Implementation guidelines", "User experience principles" with principle subheadings such as "Match friction to the cost of a mistake" and "Prefer reversibility over confirmation". — [Primer Delete scenario](https://primer.style/product/scenario-patterns/delete)
- The Delete page names ConfirmationDialog, Dialog, ActionList.Item, Button and Banner. It links to Notification messaging and to ConfirmationDialog guidelines. It has paired do and don't examples. It has no "when to use" section and no decision tree. — [Primer Delete scenario](https://primer.style/product/scenario-patterns/delete)
- UI pattern headings (Empty states): "The Blankslate component" (Graphic, Primary Text, Secondary text, Primary action, Secondary action, Border), "Variations", "Content and copy", "First time user experiences", "Error states". The page links to Degraded experiences. — [Primer Empty states](https://primer.style/product/ui-patterns/empty-states)

**Agent artifacts [P]**
- `@primer/mcp` latest is 1.1.2, with release candidates on 2026-11-14. — [npm registry @primer/mcp](https://registry.npmjs.org/@primer/mcp)
- The docs list 19 tools, including `list_patterns`, `get_pattern`, `lint_css`, `primer_coding_guidelines` and `review_alt_text`. — [Primer MCP server docs](https://primer.style/product/getting-started/foundations/mcp)
- `list_patterns` description: "Prefer a scenario pattern when one fits the task, and fall back to the more generic UI patterns otherwise." — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- `get_pattern` resolves a scenario name first on a name clash. It fetches the live primer.style page and converts the `main` element to markdown. — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- If no pattern matches, `get_pattern` returns "There is no pattern named ..." and points to `list_patterns`. — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- `primer_coding_guidelines` says: prefer an existing Primer component over a new one, and "Follow patterns from Primer when creating new components". It tells the agent to use `list_patterns` "if one exists". — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- `find_tokens` says: "After identifying tokens and writing CSS, you MUST validate the result using lint_css." — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- The `init` tool tells the agent to "Add appropriate agent instructions (like for copilot) to the project" that prefer Primer packages. — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- The `primer/react` repo has `.github/copilot-instructions.md`, `.github/instructions/*.instructions.md` and two custom agents (`modular-ds-implementer`, `modular-ds-reviewer`). These are for contributors to Primer React. — [primer/react .github](https://github.com/primer/react/tree/main/.github)
- [S] Unrelated packages also use the name "primer-mcp" (a Python planning tool). — [PyPI primer-mcp](https://pypi.org/project/primer-mcp)

### Inferences
- Primer is the only system of the four with a stated pattern fallback order inside the agent tool text: scenario pattern, then UI pattern, then components and coding guidelines.
- Pattern pages are principle-based prose with do and don't pairs. They are not decision trees.
- The MCP server reads the live docs, so the docs page is the single source for patterns.

### Gaps
- No public contribution template for a new Primer pattern found. The Delete page mentions a staff-only "Scenarios" repo.
- I did not read the full Primer pages; WebFetch truncated them after the main headings.

## SLDS: what do the linter, MCP and skills tell an agent?

### Takeaway
SLDS 2 renames "design guidelines" to "patterns", but the Salesforce agent skills do not expose patterns. They expose 85 component "blueprints" with a 4-level decision tree and a mandatory SLDS Linter step.

### Cited Findings
- [S] Trailhead: design guidelines in SLDS 1 are what SLDS 2 calls patterns. lightningdesignsystem.com shows SLDS 2; SLDS 1 is at v1.lightningdesignsystem.com. — [Trailhead, explore SLDS resources](https://trailhead.salesforce.com/es/content/learn/modules/lightning-design-system-basics/explore-slds-resources)
- [older] An SLDS 1 user engagement guideline listed sub-patterns such as welcome mats, popovers and walkthroughs. — [SLDS summer-20 user engagement patterns](https://summer-20.lightningdesignsystem.com/guidelines/user-engagement/patterns)
- The SLDS Linter is an npm package with Stylelint rules for CSS and ESLint rules for HTML. It can write a SARIF report. Its rules are also in Salesforce Code Analyzer. — [SLDS Linter](https://developer.salesforce.com/docs/platform/slds-linter/overview)
- The DX MCP Server has non-GA LWC tools `guide_lwc_slds2_uplift_linter_fixes` and `orchestrate_lwc_slds2_uplift`. They need `--allow-non-ga-tools`. — [LWC Guidelines MCP tools](https://developer.salesforce.com/docs/platform/lwc/guide/mcp-guidelines.html)
- `forcedotcom/sf-skills` has three SLDS skills: `design-systems-slds-apply`, `design-systems-slds-validate`, `design-systems-slds2-migrate`. — [forcedotcom/sf-skills](https://github.com/forcedotcom/sf-skills/tree/main/skills)
- The apply skill counts 85 blueprints ("CSS/HTML patterns for any framework") and 523 styling hooks. — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- The apply skill puts "Design decisions -- visual hierarchy, composition, interaction patterns" out of scope. — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- Selection order: Lightning Base Components (LWC only), then SLDS Blueprints, then custom with styling hooks, then custom CSS as last resort. — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- `component-selection.md` has an ASCII decision tree. Its last branch: "No blueprint match? → Build custom with styling hooks". — [component-selection.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/references/component-selection.md)
- Find step: `node scripts/search-blueprints.cjs --search "<pattern>"`, then read the blueprint YAML. If the search returns no match: "do not use the artifact". — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- Blueprint YAML keys: `name`, `description`, `category`, `slds_classes`, `variants`, `lightning_component`, `accessibility`, `usage_guidelines`, `code_examples`, `styling_hooks`, `dependencies`, `related_components`, `references`, `metadata`. — [cards.yaml](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/assets/blueprints/components/cards.yaml)
- Phase 5 "Validate (Mandatory — Do Not Skip)" runs `npx @salesforce-ux/slds-linter@latest lint`. It says "Do not rationalize violations as acceptable." — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- Phase 1 asks which states the UI needs: "loading, empty, error, success". — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)
- [S] Salesforce notes that agents can skip a linter step even when a skill marks it mandatory. — search summary of [Run SLDS Linter with SLDS AI skills](https://developer.salesforce.com/docs/platform/slds-linter/guide/get-started-linter-AI-skills.html) (the page returned HTTP 403 to direct fetch)

### Inferences
- In the Salesforce agent model, "pattern" means a component blueprint. The SLDS 2 design patterns are not in the skills.
- SLDS has the most deterministic model: a decision tree, search scripts over JSON indexes, and a linter.
- The blueprint YAML is a reusable schema for a machine-readable entry with `usage_guidelines` and `related_components`.

### Gaps
- I did not reach the live SLDS 2 patterns index. Its page template is unknown.
- No standalone "SLDS MCP" server from Salesforce found. Agentforce Vibes design guidance was not found.
- No contribution template for a new SLDS pattern found.

## Fluent: does Fluent 2 have patterns, and is there a Fluent MCP or agent guidance?

### Takeaway
Fluent 2 has no "Patterns" section, but it has pattern-like guides such as "Wait UX". The only official Fluent MCP server is for Fluent UI Blazor. The `microsoft/fluentui` agent files are for contributors, not for consumers.

### Cited Findings
- The Fluent 2 home page shows Design, Develop, Components (Web, iOS, Android, Windows) and Resources. It shows no Patterns section and no MCP or llms.txt. — [Fluent 2](https://fluent2.microsoft.design/)
- The "Wait UX" page has sections "Principles", "Visual patterns" (Spinner, Progress bar, Skeleton, Toast, Morse code, Pulsing dot), "Behavior", "Accessibility", "Content patterns". It has good and bad examples but no "when to use" section. — [Fluent 2 Wait UX](https://fluent2.microsoft.design/wait-ux)
- `microsoft/fluentui` has `AGENTS.md`, `CLAUDE.md`, `.agents/skills`, `.claude/skills` and `.github/skills`. — [microsoft/fluentui](https://github.com/microsoft/fluentui)
- The skills are contributor tasks: `v9-component`, `headless-component`, `token-lookup`, `lint-check`, `visual-test`, `review-pr` and others. — [fluentui .agents/skills](https://github.com/microsoft/fluentui/tree/master/.agents/skills)
- `AGENTS.md` has a "V9 Component Template (the correct pattern)" and "Legacy Anti-Patterns (never copy these)". Here "pattern" means code pattern. — [fluentui AGENTS.md](https://raw.githubusercontent.com/microsoft/fluentui/master/AGENTS.md)
- [S] The Fluent UI Blazor MCP server (`Microsoft.FluentUI.AspNetCore.McpServer`, repo `microsoft/fluentui-blazor`) has tools ListComponents, SearchComponents, GetComponentDetails, GetEnumValues, GetComponentEnums, plus prompt templates for scaffold, form and migration. A skill with `SKILL.md` and `references/` ships with it. — [Fluent UI Blazor MCP server blog](https://blog-dvoituron.azurewebsites.net/2026/02/20/fluentui-blazor-mcp-server)
- [S] Microsoft's `ui-widget-developer` skill requires React and Fluent UI for Copilot Chat widgets. It is not a Fluent reference server. — [mcpservers.org ui-widget-developer](https://mcpservers.org/agent-skills/microsoft/skills/ui-widget-developer)

### Inferences
- Fluent has no pattern model for agents. Pattern-like guidance exists only as prose pages.
- The Blazor MCP tool set is component-only. It has no pattern tools.

### Gaps
- I did not see the full Fluent 2 site navigation. Other pattern-like pages can exist.
- No official MCP server for Fluent 2 or Fluent UI React v9 found.
- No consumer lint or codemod for Fluent pattern use checked.

## Cross-system answers: contribution template, selection style, deterministic checks

### Takeaway
Only the archived Polaris site has a public pattern contribution template. Selection is free text in Polaris and Primer, a decision tree in SLDS, and absent in Fluent. Each agent artifact that covers patterns ends with a mandatory machine check.

### Cited Findings
- Polaris (archived): `variant.md.template` with "Use when merchants need to:". — [variant.md.template](https://raw.githubusercontent.com/Shopify/polaris-react-archive/main/polaris.shopify.com/content/patterns/variant.md.template)
- Polaris (current): "Use cases" bullets and two-way "Recommended" links between templates and compositions. — [Homepage template (md)](https://shopify.dev/docs/api/app-home/patterns/templates/homepage.md)
- Primer: tool-text fallback order, scenario pattern then UI pattern. — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts)
- SLDS: 4-level selection order and an ASCII decision tree. — [component-selection.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/references/component-selection.md)
- Deterministic checks: Shopify `validate.mjs` (mandatory, three attempts) — [shopify SKILL.md](https://raw.githubusercontent.com/Shopify/shopify-ai-toolkit/main/skills/shopify/SKILL.md); Primer `lint_css` (MUST after tokens) — [server.ts](https://raw.githubusercontent.com/primer/react/main/packages/mcp/src/server.ts); SLDS Linter (mandatory, zero violations target) — [slds-apply SKILL.md](https://raw.githubusercontent.com/forcedotcom/sf-skills/main/skills/design-systems-slds-apply/SKILL.md)

### Inferences
- No system lints pattern conformance. All checks act on components, props, classes or tokens.
- Two find mechanisms exist: an MCP list and get pair (Primer), and a skill with search scripts over local indexes (Shopify, SLDS).
- The common fallback, when no pattern fits, is "compose from components and tokens, then run the check". Only Primer and SLDS state it in agent text.

### Gaps
- No public pattern contribution templates for current Polaris, Primer, SLDS 2 or Fluent 2 found.
- No `llms.txt` checked for any of the four sites.
