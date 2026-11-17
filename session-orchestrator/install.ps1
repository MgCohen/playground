# Installs the tray app: one-file publish to %USERPROFILE%\Apps\Orchestrator and a desktop shortcut.
# Do not run it without the approval of the user.
$ErrorActionPreference = 'Stop'
$app = Join-Path $env:USERPROFILE 'Apps\Orchestrator'
$staging = "$app.new"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $PSScriptRoot 'src\Orchestrator.Tray') -c Release -o $staging --nologo -v q -r win-x64 --self-contained false -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. The installed app did not change.' }
Get-Process -Name 'Orchestrator.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Move-Item $staging $app
$exe = Join-Path $app 'Orchestrator.Tray.exe'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Orchestrator.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $app
$link.Description = 'Orchestrator: the session orchestrator in the tray (mock data)'
$link.Save()
Write-Host "Installed in $app. Start it with the Orchestrator shortcut on the desktop."
