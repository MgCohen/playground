# Golden paths, paved roads and agent instruction formats

Research date: 2026-11-14. Each source date is in the text where the source gives one.

## Q1. Platform engineering: golden paths, paved roads, and the policy for teams that leave the path

### Takeaway
A golden path is an opinionated, supported default. It is optional. A team that leaves it loses central support and takes the responsibility. No source gives a formal off-path process. The deterministic parts are templates (Backstage Scaffolder) and scorecards (Soundcheck). The tutorial text is judgment and guidance.

### Cited Findings

Spotify golden paths (Gary Niemen, Spotify Engineering, 17 August 2020):
- Definition: the Golden Path is the "opinionated and supported" path to "build something", for example a backend service, a website or a data pipeline. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- Unit template: a step-by-step tutorial that "walks you through this opinionated and supported path". There is one tutorial for each discipline: backend, client, data engineering, data science, machine learning, web, audio. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- The tutorials include each step, also small UI actions, because a skipped step causes confusion. The article calls them Spotify's most read technical documentation. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- Problem that the path solves: "Rumour-driven development". Before the paths, the only way to learn a procedure was to ask a colleague. This "simply wasn't scalable". — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- Off the path: engineers can leave the path, but they do not get the same support. The article proposes "Golden State", a list of checks that show if a system follows the path. It gives no enforcement policy. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- Discovery: the Backstage "Explore" section shows the "blessed" tools, with a filter by discipline. The tutorials are written in TechDocs (docs-like-code) and shown in Backstage. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)
- Ownership: the Platform organization made the paths. One technical writer for each tutorial did not scale. Content ownership is now spread across many teams, which makes coordination gaps. — [Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem/)

Backstage Software Templates (the scaffolder, deterministic part of a path):

```yaml
apiVersion: scaffolder.backstage.io/v1beta3
kind: Template
metadata: { name, title, description }
spec:
  owner: team-a
  type: service
  parameters:   # JSON Schema form steps + ui:* keys (ui:field, ui:options, ui:autofocus)
  steps:        # each: id, name, action, input; optional: if, each
    - id: fetchBase
      action: fetch:template   # also fetch:plain, publish:github, catalog:register
  output:       # links (title + url or entityRef, icon), text (title, content)
```

- Fields and actions above are from the Backstage docs. Optional `spec` fields are `secrets` and `presentation`. The docs tell authors to use camelCase step `id` values, because a dash breaks expressions such as `${{ steps.my-action.output.value }}`. — [Backstage docs](https://backstage.io/docs/features/software-templates/writing-templates)

Soundcheck (Spotify scorecard plugin for Backstage, the verifier part):
- A track has one or more levels. A level has one or more checks. Level one is usually the hardest level. — [Soundcheck docs](https://backstage.spotify.com/docs/plugins/soundcheck/core-concepts/tracks)
- A check result is pass or fail for one entity. The Entity Tab shows the technical health of one entity against its tracks and checks. Tech Health views show pass rates by check, track, level, entity and team. — [Soundcheck docs](https://backstage.spotify.com/docs/plugins/soundcheck/core-concepts); [Soundcheck Entity Tab](https://backstage.spotify.com/docs/plugins/soundcheck/soundcheck-elements/soundcheck-entity-tab)
- Authors make tracks from templates, a no-code UI or YAML. — [Soundcheck docs](https://backstage.spotify.com/docs/plugins/soundcheck/core-concepts/tracks)

Netflix paved road:
- Dianne Marsh (Netflix director of engineering) describes the Paved Road as "formalizing a set of expectations and commitments between the centralized teams and our engineering customers". The deck says the Paved Road is "NOT mandatory" and teams "may choose not to use the Paved Road at any time". — [The Paved Road at Netflix, SlideShare](https://www.slideshare.net/diannemarsh/the-paved-road-at-netflix)
- A talk example on freedom and responsibility: an engineer who wants a different build tool hears "with that freedom, you take that responsibility", so the central team gives no support for that tool. This is a speaker's example, not a written policy. — [search summary of Netflix talk material](https://www.wearedevelopers.com/videos/270-devops-at-netflix.md)
- The Netflix culture page frames the culture as freedom with responsibility for the outcome. — [Netflix culture](https://jobs.netflix.com/culture)

CNCF Platforms White Paper (TAG App Delivery; page shows © 2025, no publication date):
- Definition: a platform is "an integrated collection of capabilities defined and presented according to the needs of the platform's users". — [CNCF Platforms White Paper](https://tag-app-delivery.cncf.io/whitepapers/platforms/)
- Golden path templates: "Templated compositions of well-integrated code and capabilities for rapid project development". The paper uses "golden path", not "paved road". — [CNCF](https://tag-app-delivery.cncf.io/whitepapers/platforms/)
- Seven attributes: "Platform as a product", "User experience", "Documentation and onboarding", "Self-service", "Reduced cognitive load for users", "Optional and composable", "Secure by default". — [CNCF](https://tag-app-delivery.cncf.io/whitepapers/platforms/)
- Off-path policy: under "Optional and composable", platforms should "enable product teams to provide and manage their own capabilities outside of the platform's offerings when necessary". — [CNCF](https://tag-app-delivery.cncf.io/whitepapers/platforms/)
- Thin platform: the paper recommends "the thinnest viable platform layer over implementations from managed providers". — [CNCF](https://tag-app-delivery.cncf.io/whitepapers/platforms/)
- Thirteen capability domains. One of them is "Golden path" templates and docs. Others include web portals, APIs and CLIs, build and test automation, observability. — [CNCF](https://tag-app-delivery.cncf.io/whitepapers/platforms/)

### Inferences
- One golden path has three layers: a tutorial (judgment, for people), a template (deterministic start), and a scorecard (deterministic check after the start). Spotify's "Golden State" idea became the Soundcheck track model.
- All sources agree on the same off-path rule: leaving is allowed, central support stops, the team owns the result. No source gives a request, exception or return process.
- The scorecard is the only place where the off-path state is visible. It measures distance from the path. It does not block.

### Gaps
- The Netflix Tech Blog article on the paved road returned HTTP 403. I did not read a primary Netflix blog text. The quotes come from the Marsh slide deck through a search summary.
- I did not verify a Team Topologies primary text for "thinnest viable platform". The CNCF quote is the only cited source.
- I did not find a Google primary source on golden paths in this session.
- I did not read the Backstage docs on how users find templates in the UI (filters, tags). The page I read does not cover it.

## Q2. Anthropic Agent Skills: SKILL.md format, selection, and authoring guidance

### Takeaway
One skill is a folder with `SKILL.md` (YAML frontmatter `name` and `description`, then a body) plus optional files and scripts. Claude selects a skill only from its `description`, which is always in context. The body and the files load only when needed. The guidance matches freedom to fragility and puts deterministic work in scripts.

### Cited Findings

Unit template:

```markdown
---
name: pdf-processing          # max 64 chars; lowercase, digits, hyphens; no XML; not "anthropic"/"claude"
description: Extract text and tables from PDF files, fill forms, merge documents. Use when working with PDF files or when the user mentions PDFs, forms, or document extraction.   # non-empty, max 1,024 chars, no XML
---
# PDF Processing
## Quick start
...
**Form filling**: See [FORMS.md](FORMS.md)
```

- Field rules above are from the docs. The `description` "must include both what the Skill does and when Claude should use it". — [Agent Skills overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)

Progressive disclosure (three levels):

| Level | When loaded | Token cost | Content |
|---|---|---|---|
| Level 1: Metadata | Always (at startup) | about 100 tokens per skill | `name` and `description` |
| Level 2: Instructions | When the skill is triggered | Under 5k tokens | `SKILL.md` body |
| Level 3+: Resources | As needed | None until accessed | Reference files load when read. Scripts run through bash, only their output enters context. |

— [Agent Skills overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)

Selection:
- "The `description` is what Claude matches your request against when determining whether to trigger the Skill." — [overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)
- "Claude uses it to choose the right Skill from potentially 100+ available Skills." — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- "Always write in third person. The description is injected into the system prompt, and inconsistent point-of-view can cause discovery problems." Good: "Processes Excel files and generates reports". Bad: "I can help you…", "You can use this…". — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Vague descriptions to avoid: "Helps with documents", "Processes data". — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)

Structure rules:
- "Keep the `SKILL.md` body under 500 lines for optimal performance." — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- "Keep references one level deep from `SKILL.md`." Reason: with nested references Claude "might use commands such as `head -100`" and get incomplete data. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- A reference file longer than 100 lines needs a table of contents at the top. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Three disclosure patterns: high-level guide with references; domain-specific organization (`reference/finance.md`, `reference/sales.md`); conditional details. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Naming: gerund form recommended (`processing-pdfs`). Avoid `helper`, `utils`, `tools`. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)

Degrees of freedom:

| Freedom | Form | Use when |
|---|---|---|
| High | Text instructions | Multiple approaches are valid; decisions depend on context; heuristics guide |
| Medium | Pseudocode or scripts with parameters | A preferred pattern exists; some variation is acceptable |
| Low | Specific scripts, few or no parameters | Operations are fragile; consistency is critical; a specific sequence must be followed |

- Analogy: "Narrow bridge with cliffs on both sides" means low freedom with exact steps. "Open field with no hazards" means high freedom. Low-freedom example: "Run exactly this script… Do not modify the command or add additional flags." — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)

Feedback loops, checklists and scripts:
- Checklist pattern: "provide a checklist that Claude can copy into its response and check off as it progresses". — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Feedback loop: "Run validator → fix errors → repeat". The validator can be a script or a reference document such as `STYLE_GUIDE.md`. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- "plan-validate-execute": analyze → create plan file → validate plan → execute → verify. Use for batch, destructive, complex or high-stakes operations. Validation errors must be specific, for example a list of available field names. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- "Prefer scripts for deterministic operations." "Solve, don't defer": scripts handle errors themselves. No "voodoo constants". — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Templates: strict ("ALWAYS use this exact template structure") or flexible ("use your best judgment"). "Avoid offering too many options": give a default plus one escape hatch. — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Evaluations first: at least three evaluation scenarios, a baseline without the skill, then minimal instructions. Test with Haiku, Sonnet and Opus. "There is not currently a built-in way to run these evaluations." — [best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- Security: use skills only from trusted sources. A malicious skill can direct tool calls or code. — [overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)

Claude Code extensions (code.claude.com, read 2026-11-14):
- Extra frontmatter fields: `when_to_use`, `disable-model-invocation`, `user-invocable`, `allowed-tools`, `disallowed-tools`, `paths`, `model`, `effort`, `context: fork`, `agent`, `hooks`, `argument-hint`, `arguments`, `shell`. — [Claude Code skills](https://code.claude.com/docs/en/skills)
- "the combined `description` and `when_to_use` text is truncated at 1,536 characters in the skill listing". — [Claude Code skills](https://code.claude.com/docs/en/skills)
- `paths`: glob patterns. "Claude loads the skill automatically only when working with files matching the patterns." — [Claude Code skills](https://code.claude.com/docs/en/skills)
- `disable-model-invocation: true`: the description is not in context; only the user can start the skill. `user-invocable: false`: only Claude can start it. — [Claude Code skills](https://code.claude.com/docs/en/skills)
- Locations and precedence: enterprise over personal (`~/.claude/skills/`) over project (`.claude/skills/`). Plugin skills are namespaced (`/plugin-name:skill-name`). Nested `<subdir>/.claude/skills/` loads when Claude first reads a file in that folder. — [Claude Code skills](https://code.claude.com/docs/en/skills)
- Relation to CLAUDE.md: "Unlike CLAUDE.md content, a skill's body loads only when it's used". Make a skill "when a section of CLAUDE.md has grown into a procedure rather than a fact." Custom commands "have been merged into skills". — [Claude Code skills](https://code.claude.com/docs/en/skills)
- "Claude Code skills follow the Agent Skills open standard". Outside Claude Code (claude.ai, API) only `name`, `description`, `license`, `compatibility`, `metadata`, `allowed-tools` are allowed; other keys give a hard error. — [Claude Code skills](https://code.claude.com/docs/en/skills)

### Inferences
- The skill model is a two-stage router. Stage 1 is a menu of descriptions. Stage 2 is the body, which links to files one level down. The description is the only discovery surface, so it is a trigger specification, not a summary.
- "Facts in CLAUDE.md, procedures in skills" is an explicit Anthropic rule for the split between always-on context and on-demand units.
- Degrees of freedom map directly to the platform split: low freedom = template or script (deterministic), high freedom = tutorial text (judgment).

### Gaps
- Anthropic docs do not describe the matching algorithm beyond "the description is matched". I found no published accuracy data for skill selection.
- I did not read the agentskills.io specification page directly.

## Q3. AGENTS.md, CLAUDE.md, Cursor rules, GitHub Copilot instructions: scope and trigger

### Takeaway
All four formats are Markdown. They differ in trigger: always on, nearest file in the directory tree, glob match, description match, or manual mention. Nearest or most specific file wins in AGENTS.md, Cursor and Copilot. The user's chat prompt overrides all.

### Cited Findings

| Format | Location | Trigger and scope | Precedence |
|---|---|---|---|
| AGENTS.md | Any folder; no frontmatter, "Use any headings you like" | "Agents automatically read the nearest file in the directory tree." | "The closest AGENTS.md to the edited file wins; explicit user chat prompts override everything." |
| Cursor project rules | `.cursor/rules/*.mdc` ("A plain `.md` file in `.cursor/rules` is ignored") | Always Apply; Apply Intelligently (by `description`); Apply to Specific Files (`globs`); Apply Manually (`@my-rule`) | Team Rules → Project Rules → User Rules; "earlier sources take precedence" |
| Copilot repo-wide | `.github/copilot-instructions.md` | All requests in the repo | Personal > repository > organization; all sets are still sent |
| Copilot path-specific | `.github/instructions/NAME.instructions.md` | Frontmatter `applyTo: "**/*.ts,**/*.tsx"`; optional `excludeAgent: "code-review"` or `"cloud-agent"` | Combined with repo-wide |
| Copilot agent files | `AGENTS.md` anywhere, or one `CLAUDE.md` / `GEMINI.md` in root | Nearest AGENTS.md | "the nearest `AGENTS.md` file in the directory tree will take precedence" |

- AGENTS.md: "A simple, open format for guiding coding agents", "a README for agents". Suggested sections: project overview, build and test commands, code style, testing instructions, security. — [agents.md](https://agents.md/)
- AGENTS.md stewardship: "stewarded by the Agentic AI Foundation under the Linux Foundation". Adoption: "used by over 60k open-source projects". Supported by Codex, Jules, Cursor, VS Code, Copilot coding agent, Gemini CLI, Devin, Zed, Warp, Aider, Windsurf and others. — [agents.md](https://agents.md/)
- Cursor: `alwaysApply: true` means "Always included. Globs and description are ignored." `globs` "auto-attach the rule when matching files are in context". "Keep rules under 500 lines." Cursor also reads AGENTS.md in root and subfolders; nested files combine with "more specific instructions taking precedence". Enforced Team Rules "cannot be disabled". — [Cursor rules docs](https://cursor.com/docs/context/rules)
- Copilot: path-specific instructions on GitHub.com apply only to Copilot cloud agent and Copilot code review. Code review reads instructions from the PR head branch. — [GitHub docs](https://docs.github.com/en/copilot/how-tos/configure-custom-instructions/add-repository-instructions)
- Claude Code: skills can carry `paths` globs, like Cursor `globs`. Nested `CLAUDE.md` files load on demand (the `/doctor` command can move always-loaded text into "skills and nested `CLAUDE.md` files that load on demand"). — [Claude Code skills](https://code.claude.com/docs/en/skills)

### Inferences
- Four trigger kinds exist across tools: (1) always, (2) location (nearest file), (3) file glob, (4) description match by the model. A fifth is manual mention (`@rule`, `/skill`). Only (4) depends on model judgment. The others are deterministic.
- The Cursor 500-line limit and the Anthropic 500-line limit are the same number.
- No format has a formal "off-path" state. The user chat prompt is the override.

### Gaps
- I did not read the Claude Code memory page on CLAUDE.md import syntax and `.claude/rules/` with `paths` frontmatter. The skills page was truncated before that section.
- I found no published comparison of trigger accuracy across these formats.

## Q4. llms.txt and design systems that serve agents

### Takeaway
llms.txt is a Markdown index at the site root: an H1, a blockquote summary, and H2 lists of links, with an "Optional" section that agents can skip. Design systems now ship MCP servers and some ship llms.txt. Carbon has an official MCP server; most others are community projects.

### Cited Findings
- Author Jeremy Howard. Published 3 September 2024. Modified 10 August 2026. — [llmstxt.org](https://llmstxt.org/)
- Format order: H1 with the project name ("This is the only required section"); blockquote summary; optional detail text with no headings; zero or more H2 "file lists" with items `[name](url)` and optional `: notes`. — [llmstxt.org](https://llmstxt.org/)
- "The 'Optional' section is used, by convention, for secondary information." Agents can skip it for a shorter context. — [llmstxt.org](https://llmstxt.org/)
- Pages should offer a Markdown version at the same URL plus `.md` (`page.html.md`, `index.html.md`). Discovery: `rel="alternate" type="text/markdown"` and `rel="describedby"`. — [llmstxt.org](https://llmstxt.org/)
- The llmstxt.org page does not mention `llms-full.txt`. — [llmstxt.org](https://llmstxt.org/)
- IBM Carbon has an official Carbon MCP that gives an AI client "components, tokens, icons, guidance, and code". Carbon Patterns and v10-to-v11 migration are planned. — [Carbon MCP](https://carbondesignsystem.com/developing/carbon-mcp/overview/)
- Community MCP servers cover Atlassian, Material Design 3 and Carbon knowledge (southleft design-systems-mcp). ds-mcp loads a "dspack" JSON file that describes a design system and exposes it as tools. — [southleft design-systems-mcp](https://mcpservers.org/en/servers/southleft/design-systems-mcp); [ds-mcp](https://glama.ai/mcp/servers/aestheticfunction/ds-mcp)
- WunderUI publishes a DESIGN.md, an llms.txt and a JSON index; its MCP server gives tokens, component props and blocks. — [WunderUI MCP](https://mcpservers.org/servers/kl-webmedia/wunderui-mcp)
- Codrops (9 December 2025) describes design systems with LLMs and Storybook MCP. — [Codrops](https://tympanus.net/codrops/2025/12/09/supercharge-your-design-system-with-llms-and-storybook-mcp/)

### Inferences
- llms.txt has the same shape as SKILL.md progressive disclosure: a short index with one-line link notes, then full pages on demand. The "Optional" section is a priority signal.
- The DESIGN.md + llms.txt + MCP pattern of WunderUI is close to the VSA setup (`vsa/DESIGN.md`, skills, API manifests).

### Gaps
- I did not verify official MCP servers or llms.txt files for Material UI, shadcn/ui or Atlassian. The search found only community servers for them.
- I did not read the Storybook MCP primary documentation.

## Q5. Evidence on how agents choose between procedures, and on instruction length

### Takeaway
Anthropic recommends the simplest system and names "routing" as the pattern for distinct input categories. Research shows accuracy falls as context or instruction count grows: the U-shaped "lost in the middle" curve, and about 68% accuracy at 500 simultaneous instructions for the best models. This supports short always-on files and on-demand units.

### Cited Findings
- "Building effective agents", Anthropic, 19 December 2024. "Workflows are systems where LLMs and tools are orchestrated through predefined code paths." Agents are systems where "LLMs dynamically direct their own processes and tool usage". — [Anthropic](https://www.anthropic.com/engineering/building-effective-agents)
- Routing: "Routing classifies an input and directs it to a specialized followup task." It "works well for complex tasks where there are distinct categories that are better handled separately." An LLM or a traditional classifier can do the classification. — [Anthropic](https://www.anthropic.com/engineering/building-effective-agents)
- Other patterns: prompt chaining (with programmatic checks between steps), parallelization (sectioning, voting), orchestrator-workers, evaluator-optimizer, autonomous agents. — [Anthropic](https://www.anthropic.com/engineering/building-effective-agents)
- Simplicity: "find the simplest solution possible, and only increasing complexity when needed". — [Anthropic](https://www.anthropic.com/engineering/building-effective-agents)
- Tool documentation (ACI): "Think of this as writing a great docstring for a junior developer on your team." Poka-yoke: "Change the arguments so that it is harder to make mistakes." — [Anthropic](https://www.anthropic.com/engineering/building-effective-agents)
- Lost in the Middle (Liu et al., 2023; TACL 2024, 12:157–173): accuracy is highest when relevant data is at the start or the end of the input and drops in the middle (U-shape). — [ACL Anthology](https://aclanthology.org/2024.tacl-1.9.pdf)
- IFScale, "How Many Instructions Can LLMs Follow at Once?" (Distyl AI, arXiv 2507.11538, July 2025; NeurIPS 2025): 500 keyword-inclusion instructions, 20 models. Best frontier models reach about 68% accuracy at 500 instructions. Three decay patterns: threshold, linear, exponential. Bias toward earlier instructions. — [arXiv](https://arxiv.org/abs/2507.11538); [NeurIPS listing](https://neurips.cc/virtual/2025/122417)
- Contrary data: "Boosting Instruction Following at Scale" (arXiv 2510.14842) found "no consistent relationship between IF rates and instruction position". So position may not explain the drop with more instructions. — [arXiv 2510.14842](https://arxiv.org/pdf/2510.14842)

### Inferences
- Skill selection from descriptions is a routing step done by the model. The Anthropic routing guidance applies: categories must be distinct, so skill descriptions must not overlap.
- Count of always-on instructions is a measurable risk. Long AGENTS.md or CLAUDE.md files compete with each other, which supports the 500-line limits and the "facts always, procedures on demand" split.
- IFScale and 2510.14842 disagree on the position effect. The count effect is the stronger finding.

### Gaps
- I did not read the OpenAI "A practical guide to building agents" in this session.
- I found no study that measures skill or rule selection accuracy as the number of descriptions grows.
