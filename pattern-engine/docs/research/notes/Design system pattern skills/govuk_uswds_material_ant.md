# Pattern models and agent models: GOV.UK, USWDS, Material 3, Ant Design, Spectrum

Research date: 2026-11-15. All sources are primary unless the line says "secondary".

Summary table:

| System | Pattern unit | Page template | Selection model | Official agent artifacts | Do agent artifacts cover patterns? |
|---|---|---|---|---|---|
| GOV.UK | Pattern ("Ask users for...", "Help users to...", "Pages") | When to use / When not to use / How it works / Help improve this pattern | "When to use / when not to use" free text | None found (no llms.txt, no MCP) | Only community MCP servers |
| USWDS | Pattern ("Help users to...") with sub-patterns, plus Templates | Why this matters / Help users to... (list of sub-patterns) / Latest updates | Free text, parent page lists sub-patterns | None found | No |
| Material 3 | Canonical layouts (list-detail, supporting pane, feed) | Not retrieved (site renders with JavaScript) | Partly deterministic: pane count by window size class | None official found | No; community MCP and skills only |
| Ant Design | No "patterns" section; design specs (`spec/`) and research pages | Not retrieved | Free text | Official MCP (8 tools, 2 prompts), llms.txt, DESIGN.md, `antd setup` skill mode | No pattern tool; one prompt `antd-page-generator` |
| Spectrum | Guidelines; Spectrum Design Data spec | Not retrieved | Not found | MCP servers, Agent Skill, llms.txt, `.md` pages, agent-surface spec (draft) | `get_guidance` operation is planned, empty until RFC-D |

## GOV.UK Design System: pattern page template, contribution, lifecycle, agent support

### Takeaway
GOV.UK patterns use a fixed "When to use / When not to use / How it works" structure for each variant, and a two-stage contribution model with five named criteria. GDS publishes no llms.txt and no MCP server; only community MCP servers exist.

### Cited Findings
- The "Ask users for Addresses" page has one h2 for each variant (Address lookup, Multiple text inputs, Textarea). Each variant has h3 sections "When to use an address lookup", "When not to use an address lookup", "How an address lookup works". — [GOV.UK Addresses pattern](https://design-system.service.gov.uk/patterns/addresses/)
- The same page ends with "Help improve this pattern" (link to the GitHub discussion) and "Need help?". It shows no status tag. — [GOV.UK Addresses pattern](https://design-system.service.gov.uk/patterns/addresses/)
- The pattern links to components in the guidance text (for example the Error message component). Its code examples import the `input`, `fieldset` and `textarea` component macros. The body text links no other pattern; the side navigation lists pattern groups "Ask users for...", "Help users to..." and "Pages". — [GOV.UK Addresses pattern](https://design-system.service.gov.uk/patterns/addresses/)
- The page does not mention llms.txt, AI, agents or MCP. — [GOV.UK Addresses pattern](https://design-system.service.gov.uk/patterns/addresses/)
- Proposal process: (1) search the "community-backlog" GitHub discussions and upvote an existing idea; (2) start a new discussion with the idea and evidence of user needs. The page tells the proposer not to design or code at this stage. The community prioritises the work each year. — [Propose a component or pattern](https://design-system.service.gov.uk/community/propose-a-component-or-pattern/)
- No formal proposal template is described. The expected content is the idea, evidence of user need, and optional screenshots or links. — [Propose a component or pattern](https://design-system.service.gov.uk/community/propose-a-component-or-pattern/)
- Contribution criteria. Proposal stage: "Useful" (many teams need it) and "Unique" (no duplicate, or better than the existing item). Development stage: "Usable" (user research with a range of users, disabled users included), "Consistent" (reuses styles and components, follows the style guide and Frontend coding standards), "Versatile" (adaptable, tested across browsers, assistive technologies and devices). — [Contribution criteria](https://design-system.service.gov.uk/community/contribution-criteria/)
- Lifecycle statuses: "Trial" (a tag and a message on the guidance page; new or much-changed items that may not meet all criteria) and "Stable" (no tag). A Trial item becomes Stable after 6 months if no negative feedback comes in. — [Component lifecycle statuses](https://design-system.service.gov.uk/community/component-lifecycle-statuses/)
- The old `alphagov/govuk-design-system-backlog` repository was archived on 9 October 2026. The backlog now lives in GitHub Discussions of `alphagov/govuk-design-system`. — [govuk-design-system-backlog (archived)](https://github.com/alphagov/govuk-design-system-backlog)
- `https://design-system.service.gov.uk/llms.txt` returns HTTP 404. — [llms.txt check](https://design-system.service.gov.uk/llms.txt)
- Community MCP servers expose GOV.UK Design System docs to agents (for example `rbeatt/govuk-design-system-mcp`, `georgeherby/govuk-frontend-mcp`, which exposes components, patterns and styles as resources). GDS does not publish them. (secondary, directory listings) — [LobeHub listing](https://lobehub.com/mcp/rbeatt-govuk-design-system-mcp); [claudemarket listing](https://claudemarket.ai/mcp/georgeherby/govuk-frontend-mcp)
- Another US government system, CMS, publishes an llms.txt as "a lightweight, Markdown-based index". This is not GOV.UK. (secondary, search snippet) — [CMS design system llms.txt](https://design.cms.gov/using-ai/llms-txt/)

### Inferences
- The GOV.UK template is per variant, not per page: a pattern page is a set of alternatives, and each alternative has its own when-to-use and when-not-to-use. This gives an agent a choose-one-of-N structure, but the conditions are prose, not machine rules.
- The lifecycle labels in the request ("proposed, experimental, published") do not match the current GOV.UK terms. The current terms are backlog discussion, then Trial, then Stable. "Experimental" is an older term; I did not find a current source that uses it.
- Nothing in the official GOV.UK surface tells an agent how to find, choose, compose or fall back. Fallback is only implicit: "When not to use" sections and the contribution route.

### Gaps
- I did not fetch "Check answers" or "Question pages" directly, so I cannot confirm their exact headings. The heading "Research on this pattern" did not appear on the Addresses page; I could not confirm it on other pages.
- I did not read the tools of the community MCP servers.
- The current proposal discussion template in `alphagov/govuk-design-system` Discussions was not retrieved.

## USWDS: patterns, templates and agent support

### Takeaway
USWDS has three top-level patterns ("Complete a complex form", "Select a language", "Create a user profile"). A parent pattern is an index of sub-patterns. I found no official agent artifact.

### Cited Findings
- The patterns index has one group, "Help users to...", with three patterns. It describes patterns as "practical guidelines and best practices for common user interactions". — [USWDS Patterns](https://designsystem.digital.gov/patterns/)
- Patterns, Components and Templates are separate sections in the site navigation. The index page does not state a hierarchy between them. — [USWDS Patterns](https://designsystem.digital.gov/patterns/)
- "Create a user profile" headings in order: "Why this matters", "Help users to..." (list of sub-patterns), "Latest updates", "Become part of the community". — [Create a user profile](https://designsystem.digital.gov/patterns/create-a-user-profile/)
- Sub-patterns of "Create a user profile": Address, Contact preferences, Date of birth, Email address, Sex, Name, Phone number, Race and ethnicity, Social Security Number. The parent body links no component. — [Create a user profile](https://designsystem.digital.gov/patterns/create-a-user-profile/)
- The patterns pages do not mention AI, agents, llms.txt or MCP. — [USWDS Patterns](https://designsystem.digital.gov/patterns/)

### Inferences
- USWDS uses a two-level pattern tree: parent pattern (a user goal) and sub-patterns (one data item each). This is a natural "find" index for an agent, but USWDS gives no machine form of it.

### Gaps
- I did not fetch a sub-pattern page (for example Address), so the sub-pattern heading template is not confirmed.
- I did not check `designsystem.digital.gov/llms.txt` or a USWDS MCP package.
- I did not find the USWDS contribution or proposal template for patterns.

## Material Design 3: canonical layouts and agent support

### Takeaway
Material 3 uses three canonical layouts (list-detail, supporting pane, feed) as its nearest pattern equivalent. Their selection rules are the most deterministic of the five systems, because pane count follows the window size class. I found no official Material MCP server or llms.txt.

### Cited Findings
- `m3.material.io` canonical layout pages did not render for the fetch tool (one URL returned 404, the other returned only the title). — [M3 canonical layouts](https://m3.material.io/foundations/layout/canonical-layouts)
- Three canonical layouts: list-detail, supporting pane, feed. A layout shows one or two panes. Pane count: compact 1, medium 1, expanded 2, large 2. (secondary: SAP Fiori for Android mirrors M3) — [SAP Fiori Android canonical layouts](https://www.sap.com/design-system/fiori-design-android/v26-4/foundations/adaptive-layout/canonical-layouts)
- List-detail: the list pane has a fixed width and the detail pane is flexible. If the window narrows from expanded, the detail pane stays and the list pane hides. (secondary) — [SAP Fiori Android list-detail](https://www.sap.com/design-system/fiori-design-android/v26-4/layouts/list-detail/list-detail)
- Supporting pane: the primary area takes about two thirds of the window. In compact width, put supporting content below the main content or in a bottom sheet. — [Android canonical layouts](https://developer.android.com/develop/ui/compose/layouts/adaptive/canonical-layouts)
- Compose provides list-detail and supporting-pane scaffolds that change pane logic by window size class; Views use `SlidingPaneLayout`. — [Android canonical layouts](https://developer.android.com/develop/ui/compose/layouts/adaptive/canonical-layouts)
- No official Google Material 3 MCP server was found. Community servers exist (for example `weppa-cloud/material3-mcp-server` with components, tokens, icons and accessibility tools) and community Claude skills (for example `copyleftdev/sk1llz` skill `google-material-design`). (secondary, directory listings) — [LobeHub weppa-cloud](https://lobehub.com/es/mcp/weppa-cloud-material3-mcp-server); [claudemarketplaces skill](https://claudemarketplaces.com/skills/copyleftdev/sk1llz/google-material-design)

### Inferences
- Canonical layouts are layout patterns, not task patterns. Their rule (window size class to pane count to layout behavior) is a lookup table, so an agent can apply it deterministically.

### Gaps
- The exact M3 page sections ("Usage", "Behavior" and so on) could not be confirmed, because the site needs JavaScript.
- No M3 contribution process for patterns was found; Material is not an open contribution system.

## Ant Design: llms.txt, MCP server, templates and ProComponents

### Takeaway
Ant Design has the strongest official agent surface of the five: an llms.txt, a DESIGN.md, and an official MCP server with 8 tools and 2 prompts. None of it has a pattern, template or ProComponents tool; page composition is only in the prompt `antd-page-generator`.

### Cited Findings
- llms.txt sections: "Navigation" (DESIGN.md, `llms-full.txt`, `llms-semantic.md`), "Docs (EN/CN)", "Component Docs (EN/CN)", "Semantic (EN/CN)". — [ant.design/llms.txt](https://ant.design/llms.txt)
- The Docs sections link design spec pages (`spec/` layout, navigation, data display, research-form, research-list). The index has no "patterns" or "templates" section and does not mention ProComponents. — [ant.design/llms.txt](https://ant.design/llms.txt)
- Official MCP server: `antd mcp` in `@ant-design/cli` v6.3.5 or later. Tools: `antd_list`, `antd_info`, `antd_doc`, `antd_demo`, `antd_token`, `antd_design_md`, `antd_semantic`, `antd_changelog`. Prompts: `antd-expert`, `antd-page-generator`. — [Ant Design MCP docs](https://ant.design/docs/react/mcp.md)
- `antd setup` has the modes `mcp`, `skill` and `both`. The page does not describe the skill content. — [Ant Design MCP docs](https://ant.design/docs/react/mcp.md)
- `--version` pins the antd version of the docs that the tools return. — [Ant Design MCP docs](https://ant.design/docs/react/mcp.md)
- Many community antd MCP servers exist with tools like `list-components`, `get-component-docs`, `get-best-practices`. (secondary) — [mcpservers.org wxzhishang/antd-mcp](https://mcpservers.org/id/servers/wxzhishang/antd-mcp); [glama zhixiaoqiang](https://glama.ai/mcp/servers/@zhixiaoqiang/antd-components-mcp/tools/list-components)

### Inferences
- In Ant Design, "find" and "choose" happen at component level (`antd_list`, `antd_info`). "Compose" is a prompt, so it is free text, not a deterministic tool. There is no fallback rule.
- The Ant Design pattern knowledge (spec and research pages) reaches agents only through `llms-full.txt` and `antd_design_md`, not through a structured pattern index.

### Gaps
- I did not read the text of `antd-page-generator` or the `antd setup` skill, so I cannot say what they tell the agent to do.
- I did not check ProComponents (`procomponents.ant.design`) or Ant Design Pro templates for an llms.txt or MCP.

## Adobe Spectrum: guidelines and agent support

### Takeaway
Spectrum has many agent artifacts (React Spectrum MCP, S2 docs MCP, design-data MCP, agent skills, llms.txt). Its draft "agent surface" spec plans a `get_guidance` operation, but that operation returns nothing until RFC-D. No artifact found covers patterns or a no-fit fallback.

### Cited Findings
- React Spectrum "Working with AI": an Agent Skill (`npx skills add https://react-spectrum.adobe.com`), an MCP server `@react-spectrum/mcp`, every docs page as `.md`, a "Copy for LLM" button, and an llms.txt index. The page lists no tool names and no pattern or selection guidance. — [React Spectrum AI](https://react-spectrum.adobe.com/ai)
- `@adobe/s2-docs-mcp` v1.3.1 (released 4 November 2026) gives assistants "access to Spectrum 2 component documentation and design guidelines". (secondary, package index) — [libraries.io s2-docs-mcp](https://libraries.io/npm/@adobe%2Fs2-docs-mcp)
- The Spectrum Design Data agent-surface spec is `1.0.0-draft`. Goals: author a non-Spectrum system, extend Spectrum, adhere to Spectrum. — [Agent surface spec](https://opensource.adobe.com/spectrum-design-data/spec/agent-surface/)
- Required operations: `primer`, `resolve_token`, `query_tokens`, `validate_usage`, `describe_component`, `diff_datasets`, `write_token`. Recommended: `suggest_token`, `get_guidance` (empty until RFC-D), `write_component` (not implemented). MCP tool names must equal the operation names. — [Agent surface spec](https://opensource.adobe.com/spectrum-design-data/spec/agent-surface/)
- Transports: a CLI `design-data`, an MCP server `@adobe/design-data-agent-mcp`, and a Claude Code skill that calls the CLI. The skill triggers on "design system", "design tokens", "drift", "spec-conformant". — [Agent surface spec](https://opensource.adobe.com/spectrum-design-data/spec/agent-surface/)
- The spec defines no fallback when nothing matches. `suggest_token` returns ranked candidates with a rationale; the worked example runs `validate_usage` on the output. — [Agent surface spec](https://opensource.adobe.com/spectrum-design-data/spec/agent-surface/)

### Inferences
- Spectrum treats agent access as a contract (operation names, required and recommended levels), which is the most formal agent model of the five. But it is token-first and component-first; patterns are not yet in the contract.
- `validate_usage` and `diff_datasets` are the only deterministic checks found in any of the five systems.

### Gaps
- I did not fetch spectrum.adobe.com guideline pages, so the Spectrum guideline page template is not confirmed.
- The tool list of `@react-spectrum/mcp` and `@adobe/s2-docs-mcp` was not retrieved.

## Cross-system: selection model and what is deterministic

### Takeaway
Only Material 3 canonical layouts have a deterministic selection rule (window size class to pane count). GOV.UK uses per-variant "when to use / when not to use" prose; USWDS, Ant Design and Spectrum use free text. No system gives an agent a written rule for "no pattern fits".

### Cited Findings
- GOV.UK: per-variant when-to-use and when-not-to-use prose. — [GOV.UK Addresses pattern](https://design-system.service.gov.uk/patterns/addresses/)
- Material 3: pane count table by window size class (secondary). — [SAP Fiori Android canonical layouts](https://www.sap.com/design-system/fiori-design-android/v26-4/foundations/adaptive-layout/canonical-layouts)
- Spectrum: validation operations, no fallback. — [Agent surface spec](https://opensource.adobe.com/spectrum-design-data/spec/agent-surface/)
- Ant Design: composition only as a prompt. — [Ant Design MCP docs](https://ant.design/docs/react/mcp.md)

### Inferences
- The closest thing to a fallback in any system is the GOV.UK contribution route: if nothing fits, start a backlog discussion with evidence of user need. An agent skill could copy this: if no pattern fits, compose from components and record a proposal.
- No system decision tree was found. Each system leaves "choose" to the reader of prose.

### Gaps
- None of the official agent artifacts that I read contains text about patterns. A full read of each llms-full.txt was outside the tool budget.
