# Starts a live demo of the orchestrator with real claude sessions, on its own store and port.
# Usage: .\tools\demo\demo.ps1                     Build, make a new demo store, start the supervisor, the sessions and the tray.
#        .\tools\demo\demo.ps1 -Stop               Stop the demo: the tray, the supervisor and its claude turns. The files stay.
#        .\tools\demo\demo.ps1 -Status             Show the demo processes, the URL and the session tree.
# Options: -Root <folder> (default %LOCALAPPDATA%\orch\demo-store), -Port <port> (default 7791),
#          -Model <alias> (default haiku), -Effort <level> (default low), -NoTray, -NoBuild,
#          -AnyPort (allow a port outside 7790-7799).
# The script never uses the real store (%LOCALAPPDATA%\orch\store or store\) and never the port 7777.
param(
    [string] $Root = (Join-Path $env:LOCALAPPDATA 'orch\demo-store'),
    [int] $Port = 7791,
    [string] $Model = 'haiku',
    [string] $Effort = 'low',
    [switch] $NoTray,
    [switch] $NoBuild,
    [switch] $Stop,
    [switch] $Status,
    [switch] $AnyPort
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Root = [IO.Path]::GetFullPath($Root)
$store = Join-Path $Root 'store'
$app = Join-Path $Root 'app'
$work = Join-Path $Root 'work'
$logs = Join-Path $Root 'logs'
$record = Join-Path $Root 'demo.json'
$url = "http://127.0.0.1:$Port/"

function Assert-Safe {
    $real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'orch\store'))
    $old = [IO.Path]::GetFullPath((Join-Path $repo 'store'))
    foreach ($bad in @($real, $old)) {
        if ($store.TrimEnd('\') -ieq $bad.TrimEnd('\') -or $Root.TrimEnd('\') -ieq $bad.TrimEnd('\')) { throw "The demo must not use the real store $bad. Give another -Root." }
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

# 2. A new store and a new work repository. The demo policy applies no default decision, so the cards stay for the user.
foreach ($dir in @($store, $work, $logs)) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $store, $work, $logs | Out-Null
$policy = Get-Content (Join-Path $repo 'config\policy.default.json') -Raw | ConvertFrom-Json
foreach ($level in $policy.levels) { $level.timeoutSeconds = $null }
Write-Utf8 (Join-Path $store 'policy.json') ($policy | ConvertTo-Json -Depth 10)
Write-Utf8 (Join-Path $work 'AGENTS.md') "# Demo repository`n`nThis repository is a demo of the orchestrator. Keep each change to one small text file.`n"
Native git -C $work init -q -b main
Native git -C $work -c core.autocrlf=false add -A
Native git -C $work -c user.name=demo -c user.email=demo@example.invalid commit -q -m 'Demo repository'

# 3. The supervisor, with the real claude on the cheap model. ORCH_IMPORT=0: no session goes into the desktop app.
# A start from a Claude session gives the supervisor the env of that session (session id, agent, app id, plugin dirs).
# The claude turns of the demo must not inherit it, so remove each engine variable first.
Get-ChildItem env: | Where-Object { $_.Name -match '^(CLAUDE|AI_AGENT$|BAGGAGE$|ORCH_)' } | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null) }
$env:ORCH_STORE = $store
$env:ORCH_PORT = "$Port"
$env:ORCH_IMPORT = '0'
$env:ORCH_PLUGIN_DIR = Join-Path $repo 'plugin'
$env:ORCH_BIN = Join-Path $app 'cli'
$env:ANTHROPIC_MODEL = $Model
$env:CLAUDE_CODE_EFFORT_LEVEL = $Effort
$env:CLAUDE_EFFORT = $Effort
$sup = Start-Process dotnet -ArgumentList "`"$app\sup\Orchestrator.Web.dll`"" -WorkingDirectory $Root -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput "$logs\supervisor.log" -RedirectStandardError "$logs\supervisor.err.log"
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    try { Invoke-WebRequest "${url}api/sessions" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 }
}
if ((Get-Date) -ge $deadline) { throw "The supervisor does not answer on $url. Log: $logs\supervisor.log" }

# A goal goes in a file: Windows PowerShell 5.1 breaks a native argument with quotes or line breaks.
function Brief([string] $name, [string] $text) { $path = Join-Path $logs "brief-$name.md"; Write-Utf8 $path $text; $path }

# 4. The tree: one root with the planning workflow and three small children.
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
$childIds = foreach ($c in $children) { (Orch new --title $c.title --parent $rootId --cwd $work --goal-file (Brief $c.name $c.goal) | Select-Object -Last 1).Trim() }

# 5. The tray in live mode on the demo store. A second tray cannot start while another tray runs (one instance for each user).
$trayId = $null
if (-not $NoTray) {
    # A self-test tray has its own instance, so only a normal tray blocks the start.
    $other = Get-CimInstance Win32_Process -Filter "Name='Orchestrator.Tray.exe'" | Where-Object { $_.CommandLine -notlike '*--selftest*' }
    if ($other) { Write-Warning "Another tray runs (process $($other.ProcessId -join ', ')). Stop it, then run: $app\tray\Orchestrator.Tray.exe --live `"$store`" --port $Port --no-supervisor" }
    else {
        $tray = Start-Process (Join-Path $app 'tray\Orchestrator.Tray.exe') -ArgumentList '--live', "`"$store`"", '--port', $Port, '--no-supervisor', '--pin' -PassThru
        $trayId = $tray.Id
    }
}

$info = [ordered]@{ url = $url; port = $Port; root = $Root; store = $store; work = $work; supervisor = $sup.Id; tray = $trayId; rootSession = $rootId; children = @($childIds); model = $Model; effort = $Effort; started = (Get-Date).ToString('s') }
Write-Utf8 $record ($info | ConvertTo-Json)

Write-Output "Demo started. URL: $url  Store: $store"
Write-Output "Root: $rootId  Children: $($childIds -join ', ')"
Write-Output "Answer a question: `$env:ORCH_STORE='$store'; & '$app\cli\orch.exe' answer <decision> <text>"
Write-Output "Stop: .\tools\demo\demo.ps1 -Stop -Root '$Root' -Port $Port"
