param([string]$Which)
$brief = Get-Content -Raw (Join-Path $PSScriptRoot "brief-$Which.txt")
Set-Location 'C:\Users\matheus.cohen\Documents\Projects\playground'
Write-Host ("start " + $Which + " " + (Get-Date).ToUniversalTime().ToString('o'))
claude --cloud $brief
Write-Host ("end " + $Which + " " + (Get-Date).ToUniversalTime().ToString('o'))
