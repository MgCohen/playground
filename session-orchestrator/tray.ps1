# Starts the tray app from the source.
# Usage: .\tray.ps1                               Mock mode: mock data, no claude process. Exit with the tray menu.
#        .\tray.ps1 -SelfTest                     Opens each view, examines the DOM, writes screenshots to selftest\ and exits.
#        .\tray.ps1 -Live <store> [-Port <port>]  Live mode: shows a real store. Its supervisor must run on the port (default ORCH_PORT or 7777).
param([switch] $SelfTest, [string] $Live, [int] $Port)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Orchestrator.Tray'
dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The build of $project failed. Run: dotnet build $project" }
$appArgs = @()
if ($SelfTest) { $appArgs += '--selftest', (Join-Path $PSScriptRoot 'selftest') }
if ($Live) { $appArgs += '--live', (Resolve-Path $Live).Path }
if ($Port) { $appArgs += '--port', $Port }
dotnet run --no-build --project $project -- @appArgs
exit $LASTEXITCODE
