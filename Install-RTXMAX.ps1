# SPDX-License-Identifier: GPL-2.0-only
<#
.SYNOPSIS
Check prerequisites or stage a local firmware package into a NEW ordinary folder.
.DESCRIPTION
This preparation tool does not format, repartition, write an ESP, register a
firmware entry, change Secure Boot/BitLocker, or reboot. Boot installation steps
are documented separately. All firmware/profile inputs must be provided locally.
#>
[CmdletBinding()]
param(
    [ValidateSet('Check','Stage')][string]$Action='Check',
    [string]$BuildDirectory,
    [string]$ProfilePath,
    [string]$AssetsDirectory,
    [string]$Destination
)
$ErrorActionPreference='Stop'
if($Action -eq 'Check'){
    & (Join-Path $PSScriptRoot 'Check-RTXMAX.ps1')
    Write-Host "`nRead docs/INSTALL.md for manual USB and internal automatic options." -ForegroundColor Cyan
    return
}
foreach($taskInput in @($BuildDirectory,$ProfilePath,$AssetsDirectory,$Destination)){if([string]::IsNullOrWhiteSpace($taskInput)){throw 'Stage requires BuildDirectory, ProfilePath, AssetsDirectory and Destination.'}}
$taskDest=[IO.Path]::GetFullPath($Destination)
$taskDrive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($taskDest))
if($taskDrive.DriveType -ne [IO.DriveType]::Fixed -or $taskDest.StartsWith('\\')){throw 'Stage into a new folder on a fixed Windows data drive, not directly onto a USB or network share.'}
if(Test-Path -LiteralPath $taskDest){throw 'Destination must be a new folder. Nothing will be overwritten.'}
$taskParent=Split-Path -Parent $taskDest
if(!(Test-Path -LiteralPath $taskParent -PathType Container)){throw 'Destination parent must already exist.'}
$taskWalk=Get-Item -LiteralPath $taskParent
while($taskWalk){if($taskWalk.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse-point destinations are not supported.'};$taskWalk=$taskWalk.Parent}
$taskPart=Get-Partition -DriveLetter $taskDrive.Name.Substring(0,1)
if($taskPart.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'){throw 'Do not stage directly into an EFI system partition.'}
$taskBuild=Get-Content -LiteralPath (Join-Path $BuildDirectory 'build.json') -Raw|ConvertFrom-Json
if($taskBuild.schema_version -ne 1 -or $taskBuild.mode -notin @('Manual','Internal')){throw 'Unsupported build manifest.'}
$taskEfi=Join-Path $BuildDirectory 'BOOTX64.EFI'
if((Get-FileHash -LiteralPath $taskEfi).Hash.ToLowerInvariant() -cne $taskBuild.efi_sha256){throw 'Build EFI hash mismatch.'}
$taskProfile=[IO.File]::ReadAllBytes($ProfilePath)
if($taskProfile.Length -ne 176){throw 'Expected a locally captured, reviewed 176-byte runtime profile.'}
$taskAssetSpec=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'results\reference-artifacts.json') -Raw|ConvertFrom-Json).assets
foreach($taskAsset in $taskAssetSpec){
    $taskFile=Join-Path $AssetsDirectory $taskAsset.name
    if((Get-Item -LiteralPath $taskFile).Length -ne $taskAsset.bytes -or (Get-FileHash -LiteralPath $taskFile).Hash.ToLowerInvariant() -cne $taskAsset.sha256){throw ('Asset missing or mismatched: '+$taskAsset.name)}
}
$taskNvidiaLicense=Join-Path $AssetsDirectory 'NVIDIA-LICENSE'
if(!(Test-Path -LiteralPath $taskNvidiaLicense -PathType Leaf)){throw 'Preserve the NVIDIA-LICENSE produced by local firmware extraction.'}
$taskProfileHash=(Get-FileHash -LiteralPath $ProfilePath).Hash.ToLowerInvariant()
# Validate profile structure/target, including its existing checksum, before writes.
& python (Join-Path $PSScriptRoot 'tools\validate_profile.py') $ProfilePath
if($LASTEXITCODE -ne 0){throw 'Profile validation failed.'}
[void](New-Item -ItemType Directory -Path $taskDest)
$taskEfiDir=if($taskBuild.mode -eq 'Manual'){'EFI\BOOT'}else{'EFI\PalitUnlock'}
[void](New-Item -ItemType Directory -Path (Join-Path $taskDest $taskEfiDir) -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $taskDest 'RTX-UNLOCK\ASSETS') -Force)
$taskEfiName=if($taskBuild.mode -eq 'Manual'){'BOOTX64.EFI'}else{'AUTO.EFI'}
Copy-Item -LiteralPath $taskEfi -Destination (Join-Path $taskDest ($taskEfiDir+'\'+$taskEfiName))
Copy-Item -LiteralPath $ProfilePath -Destination (Join-Path $taskDest 'RTX-UNLOCK\PROFILE.BIN')
foreach($taskAsset in $taskAssetSpec){Copy-Item -LiteralPath (Join-Path $AssetsDirectory $taskAsset.name) -Destination (Join-Path $taskDest ('RTX-UNLOCK\ASSETS\'+$taskAsset.name))}
if((Get-FileHash -LiteralPath (Join-Path $taskDest ($taskEfiDir+'\'+$taskEfiName))).Hash.ToLowerInvariant() -cne $taskBuild.efi_sha256 -or (Get-FileHash -LiteralPath (Join-Path $taskDest 'RTX-UNLOCK\PROFILE.BIN')).Hash.ToLowerInvariant() -cne $taskProfileHash){throw 'Staged EFI/profile failed post-copy verification.'}
foreach($taskAsset in $taskAssetSpec){if((Get-FileHash -LiteralPath (Join-Path $taskDest ('RTX-UNLOCK\ASSETS\'+$taskAsset.name))).Hash.ToLowerInvariant() -cne $taskAsset.sha256){throw 'Staged asset failed post-copy verification.'}}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $taskDest 'RTX-UNLOCK\LICENSE')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UPSTREAM-NOTICES.md') -Destination (Join-Path $taskDest 'RTX-UNLOCK\UPSTREAM-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'firmware\LICENSES') -Destination (Join-Path $taskDest 'RTX-UNLOCK\LICENSES') -Recurse
Copy-Item -LiteralPath $taskNvidiaLicense -Destination (Join-Path $taskDest 'RTX-UNLOCK\ASSETS\NVIDIA-LICENSE')
$taskPackage=[ordered]@{schema_version=1;mode=$taskBuild.mode;status='staged_only_not_installed';
    efi_sha256=$taskBuild.efi_sha256;profile_sha256=$taskProfileHash;assets=$taskAssetSpec;
    hardware_tested=$false;internal_partition_guid=$taskBuild.internal_partition_guid;
    windows_partition_guid=$taskBuild.windows_partition_guid;source='locally supplied build, profile and pinned assets'}
$taskPackage|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $taskDest 'RTX-UNLOCK\installation.json') -Encoding UTF8
Write-Host ('Package staged at '+$taskDest) -ForegroundColor Green
Write-Host 'Staging validates file format/hashes, not per-PC firmware compatibility. Follow docs/INSTALL.md before any boot.' -ForegroundColor Yellow
