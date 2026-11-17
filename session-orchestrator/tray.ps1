# Starts the tray app from the source, with mock data. No supervisor ticks and no claude process.
# Usage: .\tray.ps1            Opens the window. Exit with the tray menu.
#        .\tray.ps1 -SelfTest  Opens each view, examines the DOM, writes screenshots to selftest\ and exits.
param([switch] $SelfTest)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Orchestrator.Tray'
dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The build of $project failed. Run: dotnet build $project" }
$appArgs = @()
if ($SelfTest) { $appArgs += '--selftest', (Join-Path $PSScriptRoot 'selftest') }
dotnet run --no-build --project $project -- @appArgs
exit $LASTEXITCODE
