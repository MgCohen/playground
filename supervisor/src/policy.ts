import { existsSync, readFileSync } from 'node:fs'

export type Level = { name: string; description: string; timeoutSeconds: number | null; checkDocs: boolean }
export type OnDefault = 'stacked-pr' | 'commit' | 'none'
export type Policy = { levels: Level[]; docs: string[]; onDefault: OnDefault }

export function loadPolicy(path: string, fallbackPath: string): Policy {
  const file = existsSync(path) ? path : fallbackPath
  const policy = JSON.parse(readFileSync(file, 'utf8')) as Policy
  if (!Array.isArray(policy.levels) || policy.levels.length === 0) throw new Error(`policy ${file}: levels is empty`)
  for (const level of policy.levels) {
    if (!level.name) throw new Error(`policy ${file}: a level has no name`)
    if (level.timeoutSeconds !== null && !(level.timeoutSeconds >= 0)) throw new Error(`policy ${file}: level ${level.name} has a bad timeoutSeconds`)
  }
  return { levels: policy.levels, docs: policy.docs ?? [], onDefault: policy.onDefault ?? 'stacked-pr' }
}

export function levelOf(policy: Policy, name: string | undefined): Level {
  const strictest = policy.levels[policy.levels.length - 1] as Level
  return policy.levels.find(level => level.name === name) ?? strictest
}

export function defaultNote(policy: Policy, decision: string, reason: string): string {
  const head = `This is a default decision (${reason}). The user did not answer yet and will review it later.`
  if (policy.onDefault === 'none') return head
  if (policy.onDefault === 'commit')
    return `${head} Commit your current work first. Then make the work of this decision in its own commit, with "decision ${decision}" in the commit message.`
  return [
    head,
    'Before you act on it, keep the work of this decision apart so that the user can approve or refuse it alone:',
    '1. Commit your current work on the current branch.',
    `2. Create the branch orch/decision-${decision} from the current branch.`,
    '3. Make the work of this decision on that branch, and commit it.',
    '4. If the repository has a GitHub remote, push the branch and open a draft PR with the base set to the previous branch. Put the question, the default answer and the decision id in the PR description.',
    '5. Call mcp__orch__report with state working, and give the branch and the PR link in details.',
    'Continue your later work on top of this branch.',
  ].join('\n')
}

export type Verdict = 'approve' | 'change' | 'refuse'

export function reviewText(decision: string, question: string, verdict: Verdict, text: string): string {
  const said = text ? ` The user said: ${text}` : ''
  if (verdict === 'approve')
    return `Review of decision ${decision} (${question}): approved.${said} If it has a PR, mark it ready for review. Do not merge it unless the user asks.`
  if (verdict === 'change')
    return `Review of decision ${decision} (${question}): change requested.${said} Make the change on the branch orch/decision-${decision} and commit it. Then rebase each branch that is stacked above it, and push the branches that have a remote.`
  return `Review of decision ${decision} (${question}): refused.${said} Close the PR of orch/decision-${decision} and each PR stacked above it, with a comment that gives the reason. Keep the branches. Go back to the base branch of orch/decision-${decision} and continue from there in the direction that the user gave. Reuse work from the closed branches where it still fits.`
}
