# Starts a demo of the orchestrator on its own store and port.
# Usage: .\tools\demo\demo.ps1                     Build, make a new demo store, start the supervisor, a small tree and the tray (real claude).
#        .\tools\demo\demo.ps1 -Fake               The same tree with the fake provider: no model call. The script posts the cards.
#        .\tools\demo\demo.ps1 -FromLive           A copy of the live store, upgraded to version 3, with the fake provider (port 7792).
#        .\tools\demo\demo.ps1 -Stop               Stop the demo: the tray, the supervisor and its turns. The files stay.
#        .\tools\demo\demo.ps1 -Status             Show the demo processes, the URL and the session tree.
# Options: -Root <folder> (default %LOCALAPPDATA%\orch\demo-store, or demo-live with -FromLive), -Port <port> (default 7791, or 7792),
#          -LiveStore <folder> (default: store\ of the main checkout), -Model <alias> (default haiku), -Effort <level> (default low),
#          -NoTray, -NoBuild, -AnyPort (allow a port outside 7790-7799).
# The script never writes to the real store (%LOCALAPPDATA%\orch\store or store\) and never uses the port 7777.
param(
    [string] $Root,
    [int] $Port,
    [string] $Model = 'haiku',
    [string] $Effort = 'low',
    [string] $LiveStore,
    [switch] $Fake,
    [switch] $FromLive,
    [switch] $NoTray,
    [switch] $NoBuild,
    [switch] $Stop,
    [switch] $Status,
    [switch] $AnyPort
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
# A copy of real data runs with no real model and no import (root pick, docs/decisions.md).
if ($FromLive) { $Fake = $true }
if (-not $Root) { $Root = Join-Path $env:LOCALAPPDATA $(if ($FromLive) { 'orch\demo-live' } else { 'orch\demo-store' }) }
if (-not $Port) { $Port = $(if ($FromLive) { 7792 } else { 7791 }) }
$Root = [IO.Path]::GetFullPath($Root)
$store = Join-Path $Root 'store'
$app = Join-Path $Root 'app'
$work = Join-Path $Root 'work'
$logs = Join-Path $Root 'logs'
$record = Join-Path $Root 'demo.json'
$url = "http://127.0.0.1:$Port/"

# The main checkout is the first entry of the worktree list. Its store\ is the live store of today.
function Main-Checkout {
    $line = git -C $repo worktree list --porcelain | Select-Object -First 1
    ($line -replace '^worktree ', '') -replace '/', '\'
}

function Assert-Safe {
    $real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'orch\store'))
    $bad = @($real, [IO.Path]::GetFullPath((Join-Path $repo 'store')))
    if ($LiveStore) { $bad += [IO.Path]::GetFullPath($LiveStore) }
    foreach ($b in $bad) {
        foreach ($mine in @($store, $Root)) {
            if ($mine.TrimEnd('\') -ieq $b.TrimEnd('\')) { throw "The demo must not use the real store $b. Give another -Root." }
        }
    }
    if ($Port -eq 7777) { throw 'Port 7777 is the port of the real supervisor. Give another -Port.' }
    # Temporary rule while the sessions L, V and D run in parallel: L uses 7790-7799 (issue #47).
    if (-not $AnyPort -and ($Port -lt 7790 -or $Port -gt 7799)) { throw "Port $Port is outside the demo range 7790-7799. Give a port in the range, or add -AnyPort." }
}

# UTF-8 with no BOM: the store readers expect plain JSON.
function Write-Utf8([string] $path, [string] $text) { [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false)) }

# Windows PowerShell 5.1 makes a native stderr line an error. Run natives with Continue and check the exit code.
function Native {
    $ErrorActionPreference = 'Continue'
    $exe = $args[0]
    $rest = @($args | Select-Object -Skip 1)
    & $exe @rest 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE." }
}

function Stop-Tree([int] $id) {
    if ($id -and (Get-Process -Id $id -ErrorAction SilentlyContinue)) { taskkill /T /F /PID $id 2>&1 | Out-Null }
}

function Stop-Demo {
    if (Test-Path $record) {
        $r = Get-Content $record -Raw | ConvertFrom-Json
        Stop-Tree $r.tray
        Stop-Tree $r.supervisor
        Remove-Item $record -Force
    }
    # A supervisor that an older run started can still hold the port.
    Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | ForEach-Object { Stop-Tree $_.OwningProcess }
    # Stop each process that runs from the demo app folder (tray, supervisor, orch).
    Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($app) } | ForEach-Object { Stop-Tree $_.ProcessId }
}

function Orch {
    $env:ORCH_STORE = $store
    Native (Join-Path $app 'cli\orch.exe') @args
}

# A goal or a message body goes in a file: Windows PowerShell 5.1 breaks a native argument with quotes or line breaks.
function Brief([string] $name, [string] $text) { $path = Join-Path $logs "brief-$name.md"; Write-Utf8 $path $text; $path }
function Body([string] $name, $value) { $path = Join-Path $logs "body-$name.json"; Write-Utf8 $path ($value | ConvertTo-Json -Depth 5); $path }

Assert-Safe

if ($Stop) { Stop-Demo; Write-Output "The demo on port $Port stopped. The files stay in $Root."; exit 0 }

if ($Status) {
    if (Test-Path $record) { Get-Content $record -Raw } else { Write-Output 'No demo record.' }
    $up = [bool](Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    Write-Output "Supervisor on port ${Port}: $(if ($up) { 'answers' } else { 'not running' }). URL: $url"
    if (Test-Path (Join-Path $app 'cli\orch.exe')) { Orch tree }
    exit 0
}

# 1. Stop an old demo. Then build the stack into the demo app folder, out of bin\, so that a later build does not fail on locked DLLs.
Stop-Demo
if (-not $NoBuild) {
    if (Test-Path $app) { Remove-Item $app -Recurse -Force }
    Write-Output 'Build: supervisor, orch, tray.'
    Native dotnet publish (Join-Path $repo 'src\Orchestrator.Web') -c Release -o "$app\sup" --nologo -v q
    Native dotnet publish (Join-Path $repo 'src\Orchestrator.Cli') -c Release -o "$app\cli" --nologo -v q
    if (-not $NoTray) {
        Native dotnet publish (Join-Path $repo 'src\Orchestrator.Tray') -c Release -o "$app\tray" --nologo -v q -r win-x64 --self-contained false -p:PublishSingleFile=true -p:ValidateExecutableReferencesMatchSelfContained=false
    }
}

# 2. A new store. The demo policy applies no default decision, so the cards stay for the user.
foreach ($dir in @($store, $work, $logs)) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $work, $logs | Out-Null
$policy = Get-Content (Join-Path $repo 'config\policy.default.json') -Raw | ConvertFrom-Json
foreach ($level in $policy.levels) { $level.timeoutSeconds = $null }
$copied = 0
if ($FromLive) {
    if (-not $LiveStore) { $LiveStore = Join-Path (Main-Checkout) 'session-orchestrator\store' }
    $LiveStore = [IO.Path]::GetFullPath($LiveStore)
    if (-not (Test-Path (Join-Path $LiveStore 'sessions'))) { throw "No live store in $LiveStore. Give -LiveStore." }
    # A snapshot copy that only reads the live store. orch store migrate refuses it: the live sessions write new
    # files during the copy, so its check finds differences (finding L-F8). robocopy exit codes below 8 are a success.
    Write-Output "Copy: $LiveStore"
    if (Test-Path "$store.migrating") { Remove-Item "$store.migrating" -Recurse -Force }
    robocopy $LiveStore $store /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy of $LiveStore failed with exit code $LASTEXITCODE." }
    # The copy has the leases and heartbeats of the live sessions, so the upgrade needs --force.
    Orch store upgrade --force | Select-Object -Last 1
    # Each copied session names a real folder. An archive in the demo runs git worktree remove on it,
    # so point each session to a scratch folder of its own (finding L-F6).
    foreach ($file in Get-ChildItem (Join-Path $store 'sessions') -Filter 'session.json' -Recurse) {
        $entry = Get-Content $file.FullName -Raw | ConvertFrom-Json
        $scratch = Join-Path $work ($file.Directory.Name.Substring(0, 8))
        New-Item -ItemType Directory -Force $scratch | Out-Null
        $path = $scratch.Replace('\', '/')
        if ($entry.PSObject.Properties['cwd']) { $entry.cwd = $path }
        if ($entry.PSObject.Properties['work'] -and $entry.work) {
            foreach ($key in @('repo', 'cwd')) { if ($entry.work.PSObject.Properties[$key]) { $entry.work.$key = $path } }
        }
        Write-Utf8 $file.FullName ($entry | ConvertTo-Json -Depth 20)
        $copied++
    }
} else {
    New-Item -ItemType Directory -Force $store | Out-Null
    Write-Utf8 (Join-Path $work 'AGENTS.md') "# Demo repository`n`nThis repository is a demo of the orchestrator. Keep each change to one small text file.`n"
    Native git -C $work init -q -b main
    Native git -C $work -c core.autocrlf=false add -A
    Native git -C $work -c user.name=demo -c user.email=demo@example.invalid commit -q -m 'Demo repository'
}
Write-Utf8 (Join-Path $store 'policy.json') ($policy | ConvertTo-Json -Depth 10)

# 3. The supervisor. ORCH_IMPORT=0: no session goes into the desktop app.
# A start from a Claude session gives the supervisor the env of that session (session id, agent, app id, plugin dirs).
# The turns of the demo must not inherit it, so remove each engine variable first (finding L-F1).
Get-ChildItem env: | Where-Object { $_.Name -match '^(CLAUDE|AI_AGENT$|BAGGAGE$|ORCH_)' } | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null) }
$env:ORCH_STORE = $store
$env:ORCH_PORT = "$Port"
$env:ORCH_IMPORT = '0'
$env:ORCH_PLUGIN_DIR = Join-Path $repo 'plugin'
$env:ORCH_BIN = Join-Path $app 'cli'
if ($Fake) {
    # The fake provider of D: each turn is good, and each model call gives one JSON text that the docs check
    # reads as "not answered" and the state check reads as "idle".
    $env:ORCH_PROVIDER = 'fake'
    $env:ORCH_FAKE = Join-Path $Root 'fake.json'
    $behavior = [ordered]@{ turn = @{ ok = $true; text = 'Fake turn: done.'; delayMs = 500 }; ask = @{ ok = $true; text = '{"answered": false, "state": "idle", "reason": "fake provider"}' } }
    Write-Utf8 $env:ORCH_FAKE ($behavior | ConvertTo-Json -Depth 5)
} else {
    $env:ANTHROPIC_MODEL = $Model
    $env:CLAUDE_CODE_EFFORT_LEVEL = $Effort
    $env:CLAUDE_EFFORT = $Effort
}
$sup = Start-Process dotnet -ArgumentList "`"$app\sup\Orchestrator.Web.dll`"" -WorkingDirectory $Root -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput "$logs\supervisor.log" -RedirectStandardError "$logs\supervisor.err.log"
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    try { Invoke-WebRequest "${url}api/sessions" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 }
}
if ((Get-Date) -ge $deadline) { throw "The supervisor does not answer on $url. Log: $logs\supervisor.log" }

# 4. The tree: one root with the planning workflow and three small children. A copy of the live store has its own sessions.
$rootId = $null
$childIds = @()
if (-not $FromLive) {
    $rootGoal = @'
Plan the content of one demo file, greeting.txt, in the repository of your working folder. Keep each turn short.
Do these steps in order. Do not spawn children: your three children exist already.
1. Run: orch run start planning --goal "Plan the content of greeting.txt"
2. Run: orch run show <run>. The run is at step read.
3. Do the read step: the open question is "Which language do we use in greeting.txt?". Run: orch run step <run> --holds "questions are open"
4. Do the ask step: call mcp__orch__ask_user with that question, context "The demo file needs one language.", options English and Portuguese, recommendation "English: the docs use English.", risk low, waitSeconds 0. Then run: orch run step <run> --holds "the question is asked". Then end your turn.
5. When the answer arrives, run orch run show <run> and do the step that it shows. Follow the step text.
If a message from a child arrives, read it and end your turn. Do not answer for the user.
'@
    $rootId = (Orch new --title 'Demo root: plan greeting.txt' --type orchestrator --stream DEMO --workflow planning --cwd $work --goal-file (Brief 'root' $rootGoal) | Select-Object -Last 1).Trim()

    $ask = @'
Call mcp__orch__ask_user one time: question "Which greeting word do we put in hello.txt?", context "The demo needs one word for a small file.", options HELLO and WELCOME, recommendation "HELLO: it is the shortest word.", risk low, waitSeconds 0. Then end your turn. Do nothing else.
When the answer arrives: write the word into hello.txt and call mcp__orch__report with state done and a one-line summary.
'@
    $report = @'
Write the line "demo" into report.txt in your working folder. Do not commit.
Then call mcp__orch__report with state done, summary "report.txt is written.", and done ["report.txt"]. Then end your turn. Do nothing else.
'@
    $update = @'
Call mcp__orch__update one time: summary "Half of the check list is done.", step "check list", progress 50. Then end your turn. Do nothing else.
If a message arrives later, reply in one line and end your turn.
'@
    $children = @(
        @{ name = 'ask'; title = 'Demo child: ask a question'; goal = $ask },
        @{ name = 'report'; title = 'Demo child: report done'; goal = $report },
        @{ name = 'update'; title = 'Demo child: send an update'; goal = $update }
    )
    $childIds = @(foreach ($c in $children) { (Orch new --title $c.title --parent $rootId --cwd $work --goal-file (Brief $c.name $c.goal) | Select-Object -Last 1).Trim() })

    if ($Fake) {
        # With no model, no session calls the plugin tools. The script posts the same messages that the tools post,
        # and moves the planning run as the root would.
        $q = @{ question = 'Which greeting word do we put in hello.txt?'; context = 'The demo needs one word for a small file.'; options = @('HELLO', 'WELCOME'); recommendation = 'HELLO: it is the shortest word.'; risk = 'low'; waitSeconds = 0 }
        Orch post question --to user --from $childIds[0] --needs answer --json (Body 'ask' $q) | Out-Null
        Orch post report --to user --from $childIds[1] --needs ack --json (Body 'report' @{ state = 'done'; summary = 'report.txt is written.'; done = @('report.txt') }) | Out-Null
        Orch post update --to user --from $childIds[2] --json (Body 'update' @{ summary = 'Half of the check list is done.'; step = 'check list'; progress = 50 }) | Out-Null
        Orch run start planning --owner $rootId --goal 'Plan the content of greeting.txt' | Out-Null
        $run = ((Orch run list | Where-Object { $_ -match '^planning-' } | Select-Object -First 1) -split '\s+')[0]
        $env:CLAUDE_CODE_SESSION_ID = $rootId
        Orch run step $run --holds 'questions are open' | Out-Null
        $rq = @{ question = 'Which language do we use in greeting.txt?'; context = 'The demo file needs one language.'; options = @('English', 'Portuguese'); recommendation = 'English: the docs use English.'; risk = 'low'; waitSeconds = 0 }
        Orch post question --to user --from $rootId --needs answer --json (Body 'root-ask' $rq) | Out-Null
        Orch run step $run --holds 'the question is asked' | Out-Null
        $env:CLAUDE_CODE_SESSION_ID = $null
    }
}

# 5. The tray in live mode on the demo store. One normal tray runs for each user (L-F4).
$trayId = $null
if (-not $NoTray) {
    # A self-test tray has its own instance, so only a normal tray blocks the start.
    $other = Get-CimInstance Win32_Process -Filter "Name='Orchestrator.Tray.exe'" | Where-Object { $_.CommandLine -notlike '*--selftest*' }
    if ($other) { Write-Warning "Another tray runs (process $($other.ProcessId -join ', ')). Use the web UI, or stop that tray and run: $app\tray\Orchestrator.Tray.exe --live `"$store`" --port $Port --no-supervisor" }
    else {
        $tray = Start-Process (Join-Path $app 'tray\Orchestrator.Tray.exe') -ArgumentList '--live', "`"$store`"", '--port', $Port, '--no-supervisor', '--pin' -PassThru
        $trayId = $tray.Id
    }
}

$mode = if ($FromLive) { 'live copy, fake provider' } elseif ($Fake) { 'fake provider' } else { "claude $Model, effort $Effort" }
$info = [ordered]@{ url = $url; port = $Port; root = $Root; store = $store; work = $work; mode = $mode; liveStore = $LiveStore; copiedSessions = $copied; supervisor = $sup.Id; tray = $trayId; rootSession = $rootId; children = $childIds; started = (Get-Date).ToString('s') }
Write-Utf8 $record ($info | ConvertTo-Json)

Write-Output "Demo started ($mode). URL: $url  Store: $store"
if ($FromLive) { Write-Output "Copied sessions: $copied (from $LiveStore)" } else { Write-Output "Root: $rootId  Children: $($childIds -join ', ')" }
Write-Output "Answer a question: `$env:ORCH_STORE='$store'; & '$app\cli\orch.exe' answer <decision> <text>"
Write-Output "Stop: .\tools\demo\demo.ps1 -Stop -Root '$Root' -Port $Port"
