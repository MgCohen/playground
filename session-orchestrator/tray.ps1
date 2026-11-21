# Starts the tray app from the source, for development. The installed app comes from install.ps1.
# Usage: .\tray.ps1                               Mock mode: mock data, no claude process. Exit with the tray menu.
#        .\tray.ps1 -SelfTest                     Runs the flow of the tray (open, sheet, page move, dock, notice, hide) with no mouse, writes selftest\selftest.json and pictures, and exits.
#        .\tray.ps1 -Live <store> [-Port <port>]  Live mode: shows a real store on the port (default ORCH_PORT or 7777). If no supervisor answers, the tray starts one (orch supervisor start rules).
#        .\tray.ps1 -LiveDefault [-Port <port>]   Live mode on the default store root: ORCH_STORE, else %LOCALAPPDATA%\orch\store.
param([switch] $SelfTest, [string] $Live, [switch] $LiveDefault, [int] $Port)
$ErrorActionPreference = 'Stop'
# The Umpire packages come from GitHub Packages (nuget.config): the restore reads the token from GITHUB_TOKEN.
if (-not $env:GITHUB_TOKEN) { $env:GITHUB_TOKEN = (gh auth token) }
$project = Join-Path $PSScriptRoot 'src\Orchestrator.Tray'
dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The build of $project failed. Run: dotnet build $project" }
$appArgs = @()
if ($SelfTest) { $appArgs += '--selftest', (Join-Path $PSScriptRoot 'selftest') }
if ($Live) { $appArgs += '--live', (Resolve-Path $Live).Path }
elseif ($LiveDefault) { $appArgs += '--live' }
if ($Port) { $appArgs += '--port', $Port }
dotnet run --no-build --project $project -- @appArgs
exit $LASTEXITCODE
