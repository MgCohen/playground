<#
.SYNOPSIS
Copies plugin/ to plugins/orch in the mgcohen marketplace (MgCohen/claude-plugins) and opens a PR there.

.DESCRIPTION
The source of the orch plugin is plugin/ in this folder. The marketplace holds a copy. The account sync of
claude.ai reads the default branch of the marketplace, so a merged PR with a new version reaches each signed-in
session after a few minutes.

The script refuses a change with the same version as the marketplace copy: increase "version" in
plugin/.claude-plugin/plugin.json first.

.PARAMETER Clone
The folder of a clone of MgCohen/claude-plugins. Default: a clone in %TEMP%\orch-claude-plugins.

.PARAMETER DryRun
Copy and show the diff, but do not commit, push or open a PR.
#>
param(
    [string]$Clone = (Join-Path $env:TEMP 'orch-claude-plugins'),
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

$Repo = 'MgCohen/claude-plugins'
$Source = Join-Path (Split-Path $PSScriptRoot -Parent) 'plugin'
$Manifest = Get-Content (Join-Path $Source '.claude-plugin/plugin.json') -Raw | ConvertFrom-Json
$Version = $Manifest.version

# Git writes progress and warnings to stderr. With Stop, PowerShell 5.1 treats each such line as an error.
function Invoke-Clone {
    $ErrorActionPreference = 'Continue'
    git.exe --no-pager -C $Clone @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
}

if (-not (Test-Path (Join-Path $Clone '.git'))) {
    gh repo clone $Repo $Clone
    if ($LASTEXITCODE -ne 0) { throw "gh repo clone $Repo failed" }
}
$Main = (gh repo view $Repo --json defaultBranchRef --jq .defaultBranchRef.name).Trim()
Invoke-Clone fetch origin
Invoke-Clone checkout -q $Main
Invoke-Clone reset -q --hard "origin/$Main"

$Target = Join-Path $Clone 'plugins/orch'
$OldManifest = Join-Path $Target '.claude-plugin/plugin.json'
$OldVersion = if (Test-Path $OldManifest) { (Get-Content $OldManifest -Raw | ConvertFrom-Json).version } else { $null }

# The copy: everything except what the engine writes (.claude-plugin/types) and the tests of the hooks.
if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
robocopy $Source $Target /E /NFL /NDL /NJH /NJS /NP /XD types test node_modules /XF '*.log' | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit $LASTEXITCODE" }

# The catalog entry and the README row, once.
$Catalog = Join-Path $Clone '.claude-plugin/marketplace.json'
$Text = [IO.File]::ReadAllText($Catalog)
if ($Text -notmatch '"name":\s*"orch"') {
    $Entry = @"
    {
      "name": "orch",
      "source": "./plugins/orch",
      "description": "$($Manifest.description)"
    }
"@
    $Text = $Text -replace '\}\s*\]\s*\}\s*$', "},`n$Entry`n  ]`n}`n"
    [IO.File]::WriteAllText($Catalog, $Text, [Text.UTF8Encoding]::new($false))
}
$Readme = Join-Path $Clone 'README.md'
$ReadmeText = [IO.File]::ReadAllText($Readme)
if ($ReadmeText -notmatch '\| `orch` \|') {
    $Row = '| `orch` | The session orchestrator: the tools `mcp__orch__*`, the `orch:` skills and agents, and the hooks of the store. Needs the `orch` CLI on the PATH. Source: `session-orchestrator/plugin` in MgCohen/playground, copied by `tools/publish-plugin.ps1`. |'
    $ReadmeText = $ReadmeText -replace '(\| `triage` \|[^\n]*\n)', "`$1$Row`n"
    [IO.File]::WriteAllText($Readme, $ReadmeText, [Text.UTF8Encoding]::new($false))
}

Invoke-Clone add -A
$Changed = git --no-pager -C $Clone diff --cached --name-only
if (-not $Changed) { Write-Output "No change: plugins/orch is the same as plugin/ ($Version)."; exit 0 }
if ($OldVersion -eq $Version) { throw "plugins/orch has version $Version already. Increase the version in plugin/.claude-plugin/plugin.json." }

git --no-pager -C $Clone diff --cached --stat
if ($DryRun) { Write-Output "Dry run: nothing committed. The copy is in $Clone."; exit 0 }

$Branch = "orch-$Version"
$Commit = (git -C (Split-Path $Source -Parent) rev-parse --short HEAD).Trim()
Invoke-Clone checkout -q -B $Branch
Invoke-Clone commit -q -m "orch: publish version $Version" -m "Copy of session-orchestrator/plugin at MgCohen/playground@$Commit."
Invoke-Clone push -q -f origin $Branch
$Body = "Publishes the orch plugin $Version (previous: $(if ($OldVersion) { $OldVersion } else { 'none' })).`n`nCopy of ``session-orchestrator/plugin`` at MgCohen/playground@$Commit, made by ``tools/publish-plugin.ps1``.`n`n🤖 Generated with [Claude Code](https://claude.com/claude-code)"
$Url = gh pr create -R $Repo --base $Main --head $Branch --title "orch: publish version $Version" --body $Body
if ($LASTEXITCODE -ne 0) { $Url = gh pr view $Branch -R $Repo --json url --jq .url }
Write-Output $Url
