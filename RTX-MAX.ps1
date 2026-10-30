# SPDX-License-Identifier: GPL-2.0-only
[CmdletBinding()]
param([ValidateSet('Menu','Health','InstallGuide','Results')][string]$Action='Menu')
$ErrorActionPreference='Stop'
function Show-Guide([string]$Name){Get-Content -LiteralPath (Join-Path $PSScriptRoot $Name)}
function Run-Action([string]$Selected){
    switch($Selected){
        'Health' { & (Join-Path $PSScriptRoot 'Check-RTXMAX.ps1') }
        'InstallGuide' { Show-Guide 'docs\INSTALL.md' }
        'Results' { Show-Guide 'RESULTS.md' }
    }
}
if($Action -ne 'Menu'){Run-Action $Action;return}
while($true){
    Write-Host "`nRTX-MAX Windows GPU" -ForegroundColor Cyan
    Write-Host '  1  Health + installation check (read-only)' -ForegroundColor Green
    Write-Host '  2  Installation options and requirements' -ForegroundColor White
    Write-Host '  3  Verified before / after results' -ForegroundColor White
    Write-Host '  0  Exit' -ForegroundColor DarkGray
    $taskChoice=Read-Host 'Select'
    switch($taskChoice){'1'{Run-Action 'Health'}'2'{Run-Action 'InstallGuide'}'3'{Run-Action 'Results'}'0'{return}default{Write-Host 'Choose 0, 1, 2 or 3.' -ForegroundColor Yellow}}
}
