# Installs or updates the orchestrator for this user: the tray and the supervisor as published copies, the orch tool and the shortcuts.
# Run it again after a merge to update. Do not run it on the real profile without the approval of the user.
#
# Usage: .\install.ps1                      Live tray on the store of this repository, port 7777, desktop and Startup shortcuts.
#        .\install.ps1 -Mock                The tray shows mock data. No supervisor.
#        .\install.ps1 -Root <dir> -Store <dir> -Port <port> -Shortcuts <dir> -NoTool    A test install (scratchpad).
#        -NoStartup: no Startup shortcut. -NoStart: do not start the tray at the end.
param(
    [string] $Root = (Join-Path $env:USERPROFILE 'Apps\Orchestrator'),
    [string] $Store = (Join-Path $PSScriptRoot 'store'),
    [int] $Port = 7777,
    [string] $Shortcuts,
    [switch] $Mock,
    [switch] $NoStartup,
    [switch] $NoTool,
    [switch] $NoStart
)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
$Store = [IO.Path]::GetFullPath($Store)
$staging = "$Root.new"
$commit = (git -C $PSScriptRoot rev-parse HEAD).Trim()
if (git -C $PSScriptRoot status --porcelain -- src) { Write-Warning 'src/ has changes that are not committed. The install record names the last commit only.' }

# 1. Publish to a staging folder. A failed build leaves the installed copy as it is.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $PSScriptRoot 'src\Orchestrator.Tray') -c Release -o "$staging\tray" --nologo -v q -r win-x64 --self-contained false -p:PublishSingleFile=true -p:ValidateExecutableReferencesMatchSelfContained=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish of the tray failed. The installed app did not change.' }
dotnet publish (Join-Path $PSScriptRoot 'src\Orchestrator.Web') -c Release -o "$staging\supervisor" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish of the supervisor failed. The installed app did not change.' }

# 2. Stop the tray and the supervisor that run from the install root. Crash recovery continues a stopped turn at the next start.
$running = Get-CimInstance Win32_Process | Where-Object {
    ($_.ExecutablePath -and $_.ExecutablePath.StartsWith("$Root\", 'OrdinalIgnoreCase')) -or
    ($_.CommandLine -and $_.CommandLine.IndexOf("$Root\supervisor\", [StringComparison]::OrdinalIgnoreCase) -ge 0)
}
foreach ($process in $running) {
    Write-Host "Stop $($process.Name) (pid $($process.ProcessId))"
    & taskkill.exe /T /F /PID $process.ProcessId | Out-Null
}
if ($running) { Start-Sleep -Seconds 1 }

# 3. Swap the folders and write the install record.
if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
Move-Item $staging $Root
[ordered]@{ commit = $commit; store = $Store; port = $Port; installed = (Get-Date).ToUniversalTime().ToString('o') } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $Root 'install.json')

# 4. Shortcuts: one on the desktop, one in the Startup folder (the tray starts hidden at login).
$exe = Join-Path $Root 'tray\Orchestrator.Tray.exe'
$trayArgs = if ($Mock) { '' } else { "--live `"$Store`" --port $Port" }
function New-Shortcut([string] $path, [string] $arguments, [string] $description) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($path)
    $link.TargetPath = $exe
    $link.Arguments = $arguments
    $link.WorkingDirectory = Split-Path $exe
    $link.Description = $description
    $link.Save()
    Write-Host "Shortcut: $path"
}
$desktop = if ($Shortcuts) { $Shortcuts } else { [Environment]::GetFolderPath('Desktop') }
$startup = if ($Shortcuts) { $Shortcuts } else { [Environment]::GetFolderPath('Startup') }
$label = if ($Mock) { 'mock data' } else { "store $Store, port $Port" }
New-Item -ItemType Directory -Force $desktop, $startup | Out-Null
New-Shortcut (Join-Path $desktop 'Orchestrator.lnk') $trayArgs "Orchestrator tray: $label"
$startupLink = Join-Path $startup 'Orchestrator (login).lnk'
if ($NoStartup) { if (Test-Path $startupLink) { Remove-Item $startupLink } }
else { New-Shortcut $startupLink "$trayArgs --hidden".Trim() "Orchestrator tray at login: $label" }

# 5. The orch tool, from the same commit.
if (-not $NoTool) {
    $pkg = Join-Path ([IO.Path]::GetTempPath()) "orch-pkg-$PID"
    dotnet pack (Join-Path $PSScriptRoot 'src\Orchestrator.Cli') -c Release -o $pkg --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet pack of orch failed. The tray and the supervisor are installed.' }
    dotnet tool update -g Orchestrator.Cli --add-source $pkg
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool update of orch failed. The tray and the supervisor are installed.' }
    Remove-Item $pkg -Recurse -Force
}

# 6. Start the tray. In live mode it starts the supervisor if no supervisor answers on the port.
if (-not $NoStart) {
    if ($Mock) { Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) }
    else { Start-Process -FilePath $exe -ArgumentList $trayArgs -WorkingDirectory (Split-Path $exe) }
}
Write-Host "Installed commit $($commit.Substring(0, 8)) in $Root ($label)."
