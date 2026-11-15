# Pattern models in major design systems

Research date: 2026-11-14. Some official sites (atlassian.design, m3.material.io, polaris.shopify.com) render with JavaScript or redirect. The fetch tool did not read them. For those systems, the notes use secondary sources, and the notes mark them.

## GOV.UK Design System: pattern page template, split, contribution criteria, backlog

### Takeaway
GOV.UK has the most explicit pattern model. A pattern is a "best practice design solution" for a user task or a page type. The pattern uses components and tells how to adapt them. Each new pattern must pass five criteria at two gates, and user research is mandatory before publication.

### Cited Findings
- Definition: "Patterns are best practice design solutions for specific user-focused tasks and page types." Patterns "often use one or more components and explain how to adapt them to the context." — [GOV.UK Patterns](https://design-system.service.gov.uk/patterns/)
- The site has three top sections: Styles, Components, Patterns. The patterns index page does not define components or styles. — [GOV.UK Patterns](https://design-system.service.gov.uk/patterns/)
- The patterns index has three groups, named by user task:
  - "Ask users for…" (10): Addresses, Bank details, Dates, Email addresses, Equality information, Names, National Insurance numbers, Passwords, Payment card details, Phone numbers.
  - "Help users to…" (12): Check a service is suitable, Check answers, Complete multiple tasks, Confirm a phone number, Confirm an email address, Contact a department or service team, Create a username, Create accounts, Exit a page quickly, Navigate a service, Recover from validation errors, Start using a service.
  - "Pages" (8): Confirmation pages, Cookies page, Interruption pages, Page not found pages, Question pages, Service unavailable pages, Step by step navigation, There is a problem with the service pages.
  — [GOV.UK Patterns](https://design-system.service.gov.uk/patterns/)
- Exact headings of one pattern page ("Help users to Check answers"), in order: "When to use this pattern", "How it works" (sub-headings "Choose the right layout for the page", "Make the page easy to understand", "Let users go back and change their answers"), "Research on this pattern", "Help improve this pattern". This page has no "When not to use this pattern" heading. — [Check answers](https://design-system.service.gov.uk/patterns/check-answers/)
- The pattern page links to a component (Summary card in Summary list), a style (Layout, for line length), related patterns in the sidebar, a research blog post, a GitHub discussion for the pattern, and an "edit on GitHub" link. The fetched page had no link to the Service Manual and no status label. — [Check answers](https://design-system.service.gov.uk/patterns/check-answers/)
- Contribution criteria, stage 1 (proposal): "Useful": "There is evidence that this component or pattern would be useful for many teams or services." "Unique": "It does not replicate something already in the Design System." A replacement must show that it performs better. — [Contribution criteria](https://design-system.service.gov.uk/community/contribution-criteria/)
- Contribution criteria, stage 2 (development, before publication): "Usable": "It has been tested in user research and shown to work with a representative sample of users", which must include disabled people. "Consistent": "It reuses existing styles and components in the Design System where relevant", follows the GOV.UK style guide and the GOV.UK Frontend coding standards. "Versatile": usable "in a range of different services", tested with browsers, assistive technology and devices. — [Contribution criteria](https://design-system.service.gov.uk/community/contribution-criteria/)
- The "Design System team" does the reviews. The fetched page does not name a working group. — [Contribution criteria](https://design-system.service.gov.uk/community/contribution-criteria/)
- Proposal process: "1. Check the discussions on GitHub" (upvote an existing discussion and add evidence), "2. Start a new discussion" (show the idea and evidence of user need; do not spend time on a design or code yet). The backlog is the GitHub Discussions category "community-backlog". The community prioritises proposals each year. If a proposal is not prioritised, the proposer can develop it or leave it for another person. — [Propose a component or pattern](https://design-system.service.gov.uk/community/propose-a-component-or-pattern/)

### Inferences
- GOV.UK names patterns by the user task ("Ask users for…", "Help users to…"), not by the UI form. A team finds a pattern from its task, not from the widget.
- Fixed: the five criteria, the research gate, and reuse of existing components. Left to judgment: the layout choices inside "How it works".
- "No pattern fits" path: search the backlog, upvote or open a discussion with evidence, then build and research it in the service. Research is the price of admission.

### Gaps
- I did not verify a "When not to use this pattern" heading on other pattern pages. Some GOV.UK component pages have it. Check one more pattern page (for example Addresses) before you quote it as standard.
- I did not confirm where the Service Manual is linked from pattern pages. The fetched page had no link.
- I did not verify the name and role of the "Design System working group" in 2026.

## IBM Carbon: patterns versus components

### Takeaway
Carbon says a component is "designed, coded, and ready to be imported". A pattern "has multiple ways of accomplishing a user need", so Carbon does not supply code for each case.

### Cited Findings
- Patterns differ from components, "which are already designed, coded, and ready to be imported into a UI." Patterns "have multiple ways of accomplishing a user need" and combine components with more design considerations. Some patterns include example code. — [Carbon patterns overview](https://carbondesignsystem.com/patterns/overview/)
- Heading "Universal patterns" lists 14 patterns: Common actions, Dialogs, Disabled states, Disclosures, Empty states, Filtering, Forms, Global header, Loading, Login, Notifications, Read-only states, Search, Text toolbar. — [Carbon patterns overview](https://carbondesignsystem.com/patterns/overview/)
- Sample definitions: Empty states: "How to address moments in an app where there is no data to display to the user". Login: "Allows a user to gain access to an application by entering their user ID and password". Filtering: "The mechanism by which a user adds or removes data items from a displayed data set". — [Carbon patterns overview](https://carbondesignsystem.com/patterns/overview/)
- The overview page has no pattern-specific contribution text. — [Carbon patterns overview](https://carbondesignsystem.com/patterns/overview/)

### Inferences
- Carbon patterns mix three kinds: states (Disabled, Read-only, Empty, Loading), task flows (Login, Filtering, Search) and cross-component rules (Common actions). The category is "guidance that is not a single component", not one level of a hierarchy.

### Gaps
- I did not read the section headings of a single Carbon pattern page (for example Empty states).

## Shopify Polaris

### Takeaway
Polaris is now "Shopify's unified UI framework built on web components". The old Polaris React "Patterns" URL redirects to the web components API reference, and that page names no patterns.

### Cited Findings
- `polaris-react.shopify.com/patterns` gives 301 to `shopify.dev/docs/api/polaris`. — [Polaris (shopify.dev)](https://shopify.dev/docs/api/polaris)
- The page says Polaris is "Shopify's unified UI framework built on web components". Each app surface exposes a subset of components. Headings: "Polaris references", "App Home", "Extensions in other surfaces". The page does not mention patterns, compositions or templates. — [Polaris (shopify.dev)](https://shopify.dev/docs/api/polaris)
- A third-party audit says Shopify ships no llms.txt and the machine-readable surface is the Shopify Dev MCP server (plus a community `polaris-mcp-server`). — [State of AI: Shopify Polaris](https://state-of-ai-in-design-systems.netlify.app/systems/shopify-polaris)

### Inferences
- The Polaris React "Patterns" section (older material, about 2023 and before) is no longer on the main path. The 301 is the evidence.

### Gaps
- I could not read the old Polaris patterns page template or the "composition" guidance. An archive copy is necessary.

## Material Design 3: canonical layouts

### Takeaway
M3 has three canonical layouts: list-detail, supporting pane and feed. They are screen-level layouts that adapt to the window size class. Code scaffolds implement them.

### Cited Findings
- List-detail: in compact and medium windows, only one pane shows at a time. A scaffold adapts the panes to the window size class. — [Android canonical layouts](https://developer.android.com/develop/ui/compose/layouts/adaptive/canonical-layouts)
- Supporting pane: secondary content stays meaningful without the primary content. Expanded width: 70% main content, 30% supporting content. — [Android canonical layouts](https://developer.android.com/develop/adaptive-apps/guides/canonical-layouts)
- Feed: equivalent content elements in a configurable grid. On compact screens, it becomes one column. — [Android canonical layouts](https://developer.android.com/develop/adaptive-apps/guides/canonical-layouts)
- Code: Jetpack Compose `ListDetailPaneScaffold` and `SupportingPaneScaffold`. — [Compose canonical layouts](https://developer.android.com/develop/ui/compose/layouts/adaptive/canonical-layouts)

### Inferences
- M3 puts a small, closed set of screen layouts under foundations. Each one has a selection rule (relation of primary to secondary content) and a code scaffold. This is a "page type" pattern level, like the GOV.UK "Pages" group.

### Gaps
- m3.material.io returned 404 to the fetch tool. I did not confirm the M3 site structure (foundations, styles, components) or if M3 still has a "patterns" section. The findings above come from Android docs, not from m3.material.io.

## Salesforce Lightning Design System: blueprints, patterns, guidelines

### Takeaway
SLDS renamed its HTML/CSS "components" to "Component Blueprints". In SLDS 1, "design guidelines" were the pattern level. In SLDS 2 that level has the name "patterns".

### Cited Findings
- Blueprints were "originally called 'components'". They are semantic HTML, CSS, accessibility attributes and guidelines, separate from framework components. — [Salesforce UX: Beyond Components](https://medium.com/salesforce-ux/beyond-components-a-design-system-to-support-multiple-frameworks-cb1e4d511f66)
- "In SLDS 1, design guidelines are what we call patterns in SLDS 2." "A pattern is a reusable, documented solution for common user problems or tasks." "Design guidelines serve as instructions for assembling components into patterns for specific use cases." — [Trailhead: Learn about design systems](https://trailhead.salesforce.com/content/learn/modules/lightning-design-system-basics/learn-about-design-systems)
- SLDS 2 puts focus on base Lightning components instead of blueprints for custom implementations. — [Trailhead: Get started with SLDS](https://trailhead.salesforce.com/content/learn/modules/lightning-design-system-development-for-designers/get-started-with-slds)
- SLDS publishes a "Messaging" guideline that compares the message components (older SLDS 1 URL). — [SLDS messaging guidelines](https://lightningdesignsystem.com/guidelines/messaging/components/overview)

### Gaps
- I did not read an SLDS 2 pattern page template.

## Atlassian Design System

### Takeaway
I could not read atlassian.design pattern pages. The strong 2026 finding is about agent docs: Atlassian has an ADS MCP server, ADS skills and a DESIGN.md, and its own test shows that MCP and skills give more context than DESIGN.md at lower cost.

### Cited Findings
- See the agent docs section below.

### Gaps
- atlassian.design/patterns returned an empty page (JavaScript site). The list of Atlassian patterns and the page template are not in these notes.

## Brad Frost: Atomic Design

### Takeaway
Atomic Design has five stages. It is a mental model, "not a linear process". Organisms can contain other organisms, so the organism level is recursive. Templates hold content structure; pages hold real content.

### Cited Findings
- Atoms: "the foundational building blocks that comprise all our user interfaces" (labels, inputs, buttons). — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)
- Molecules: "relatively simple groups of UI elements functioning together as a unit" (a search form). — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)
- Organisms: "relatively complex UI components composed of groups of molecules and/or atoms and/or other organisms" (a header). — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)
- Templates: "page-level objects that place components into a layout and articulate the design's underlying content structure". They focus on structure (image sizes, character lengths), not final content. — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)
- Pages: "specific instances of templates that show what a UI looks like with real representative content in place". Pages test the system and show variations (for example, different cart sizes or admin rights). — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)
- "Atomic design is not a linear process"; the stages work at the same time. — [Atomic Design ch. 2](https://atomicdesign.bradfrost.com/chapter-2/)

### Inferences
- Atomic Design decomposes a screen by size and composition, not by user task. It has no "pattern" level with a task. The GOV.UK and Carbon task patterns cut across molecules, organisms and templates.
- The book is from 2016 (older material). It is still the common reference.

## Nathan Curtis (EightShapes): documenting components and patterns

### Takeaway
Curtis wrote a series of six or seven articles about component docs: overview, introductions, examples, design guidelines, code, authoring, myths. He recommends a dedicated "Use When" section, separate from the introduction.

### Cited Findings
- "While a component introduction may hint at relevant use, rely on a Use When section to illustrate the many cases when to use or not use a component." — [Component Design Guidelines](https://medium.com/eightshapes-llc/component-design-guidelines-eca706100e7c) (from search snippet)
- "If limited to images, consider depicting the component's elemental anatomy." — [Component Examples](https://medium.com/eightshapes-llc/component-examples-9c4b3bb3b308) (from search snippet)
- The series has articles: [Documenting Components](https://eightshapes.com/articles/documenting-components/), [Documenting Component Introductions](https://eightshapes.com/articles/documenting-component-introductions/), [Authoring Component Documentation](https://eightshapes.com/articles/authoring-component-documentation/), [Myths of Documenting Components](https://eightshapes.com/articles/myths-of-documenting-components), [On classification in design systems](https://eightshapes.com/articles/on-classification-in-design-systems/), [Patterns ≠ Components](https://medium.com/@nathanacurtis/patterns-components-2ce778cbe4e8).

### Gaps
- I did not read the full articles. The quotes come from search snippets. The content of "Patterns ≠ Components" and "On classification" is not in these notes. Read them before you quote Curtis on the pattern/component split.
- Most of the series is from about 2017 to 2019 (older material); I did not confirm dates.

## Decision trees for selection

### Takeaway
Several systems publish a decision tree for the notification family (modal, banner, toast, inline). Adobe React Spectrum publishes a component decision tree as an agent skill file.

### Cited Findings
- Porsche Design System has a "decision tree" page under Patterns > Notifications: walk through four cases and pick the nearest. Related components: banner, inline notification, modal. — [Porsche notifications decision tree](https://designsystem.porsche.com/v4/patterns/notifications/decision-tree)
- Adobe React Spectrum S2 publishes `component-decision-tree.md` at a `.well-known/skills/` path. — [React Spectrum component decision tree](https://react-spectrum.adobe.com/.well-known/skills/react-spectrum-s2/references/guides/component-decision-tree.md)
- Coloplast publishes an "Overlays" pattern under guidelines/patterns. — [Coloplast overlays](https://designsystem.coloplast.com/latest/guidelines/patterns/overlays-nBKXjMOX)
- Common logic across community guides: blocking and needs a decision gives a modal; persistent and non-blocking gives a banner; brief confirmation gives a toast. These are conventions, not standards. — [Tap to Dismiss: Informative & Decisional](https://medium.com/tap-to-dismiss/informative-decisional-b2ed272b4696); [Bugcrowd notifications](https://bugcrowd.design/docs/notifications)

### Gaps
- I did not read the contents of the Porsche or React Spectrum trees.

## Docs for LLMs and agents

### Takeaway
By 2026, the large systems publish MCP servers and agent skills. llms.txt is less common. Atlassian measured that MCP and skills give about 80% context, DESIGN.md about 30%.

### Cited Findings
- Primer: `@primer/mcp`, a local stdio server. 19 tools, including `list_patterns`, `get_pattern`, `get_component_usage_guidelines`, `get_component_accessibility_guidelines`, `find_tokens`, `lint_css`, `primer_coding_guidelines`, `review_alt_text`. Install: `npx @primer/mcp@latest`. — [Primer MCP server](https://primer.style/product/getting-started/foundations/mcp/)
- Atlassian (2026-06-15): published DESIGN.md (Google Stitch format: YAML tokens plus prose rationale), 80 KB, about 19,800 tokens, at `https://atlassian.design/DESIGN.md`. Test task: a log-in screen. Results: no context ~5% context, 4.20 M tokens; ADS MCP ~80%, 3.75 M tokens, 5m 1s; ADS skill ~80%, 4.43 M; DESIGN.md ~30%, 7.21 M, 6m 46s. Advice: use MCP or skills if the agent supports them. DESIGN.md suits portability, theming, prototypes and high-level direction. DESIGN.md weaknesses: all context loads at once, it loses detail, and it exposes internal component specs, so agents re-create components instead of reuse. — [Atlassian blog: DESIGN.md](https://www.atlassian.com/blog/how-we-build/atlassians-design-md-is-here-what-we-learned-testing-portable-design-context-in-practice)
- Atlassian ADS MCP server is in early access, with a remote version at `mcp.atlassian.com/v1/ads/public/mcp`; package `@atlaskit/ads-mcp`. — [pkgstats @atlaskit/ads-mcp](https://www.pkgstats.com/pkg:@atlaskit/ads-mcp) (secondary)
- Audit snapshot 2026-07-28: Atlassian, Carbon, Polaris, Primer, shadcn/ui and Salesforce SLDS all have MCP and agent skill records; llms.txt records for Atlassian, Carbon, MUI, shadcn/ui; none listed for Polaris, Primer, SLDS. A dash "is not proof of absence". — [State of AI in Design Systems](https://state-of-ai-in-design-systems.netlify.app/)
- Carbon: hand-curated llms.txt; MCP server in public preview, hosted by IBM, gated behind IBMid. — [State of AI: Carbon](https://state-of-ai-in-design-systems.netlify.app/systems/carbon-design-system) (secondary)

### Inferences
- Primer exposes patterns as a first-class MCP object (`list_patterns`, `get_pattern`). An agent can select a pattern by tool call, not only a component.

### Gaps
- Vercel Geist and GOV.UK were not in the audit. I did not check them.
- I did not verify the shadcn/ui registry MCP or llms.txt directly.
