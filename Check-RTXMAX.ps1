# SPDX-License-Identifier: GPL-2.0-only
<#
.SYNOPSIS
Read-only NVIDIA GPU health and RTX-MAX installation check.
.DESCRIPTION
Writes only its requested local report. Does not unlock, reboot, install drivers,
change startup, change clocks, or collect BitLocker recovery keys. A healthy
driver or matching installation is not proof that Tensor throughput is unlocked.
#>
[CmdletBinding()]
param(
    [string]$InstallRoot,
    [string]$ManifestPath,
    [string]$OutputDirectory,
    [switch]$NoReport,
    [switch]$PassThru
)
$ErrorActionPreference='Stop'
$taskChecks=New-Object 'System.Collections.Generic.List[object]'
function Add-Check([string]$Name,[string]$Status,[string]$Detail) {
    $taskChecks.Add([pscustomobject]@{name=$Name;status=$Status;detail=$Detail})
    $taskColor=@{PASS='Green';WARN='Yellow';FAIL='Red';INFO='Cyan';UNKNOWN='DarkGray'}[$Status]
    Write-Host ('[{0,-7}] {1}: {2}' -f $Status,$Name,$Detail) -ForegroundColor $taskColor
}
function Get-Number($Value) {
    $taskNumber=0.0
    if([double]::TryParse([string]$Value,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$taskNumber)){return $taskNumber}
    return $null
}
function Get-Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Same-Hash([string]$Path,[string]$Expected) {
    if($Expected -notmatch '^[0-9a-fA-F]{64}$'){return $false}
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and (Get-Digest $Path) -eq $Expected.ToLowerInvariant()
}
Write-Host "`nRTX-MAX / WINDOWS GPU CHECK" -ForegroundColor Cyan
Write-Host 'Read-only diagnostics | no performance test is run' -ForegroundColor DarkGray
$taskAdmin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$taskReport=[ordered]@{schema_version=1;captured_utc=[DateTime]::UtcNow.ToString('o');read_only=$true;
    administrator=$taskAdmin;recovery_keys_collected=$false;benchmark_run=$false;
    unlock_performance_verified=$false;operating_system=$null;gpus=@();telemetry=@();
    secure_boot_enabled=$null;fast_startup_enabled=$null;bitlocker=@();boot_evidence=$null;
    installation=@();relevant_error_events=@();checks=@();summary=$null}
if(!$taskAdmin){Add-Check 'Permissions' 'INFO' 'Standard user: some firmware, event-log and encryption checks may be unavailable.'}
try {
    $taskOs=Get-CimInstance Win32_OperatingSystem
    $taskReport.operating_system=[ordered]@{name=$taskOs.Caption;version=$taskOs.Version;build=$taskOs.BuildNumber;
        boot_utc=$taskOs.LastBootUpTime.ToUniversalTime().ToString('o');
        usable_ram_gib=[math]::Round($taskOs.TotalVisibleMemorySize/1MB,2);
        available_ram_gib=[math]::Round($taskOs.FreePhysicalMemory/1MB,2)}
    Add-Check 'Windows' 'INFO' ($taskOs.Caption+' / build '+$taskOs.BuildNumber)
    $taskReport.gpus=@(Get-CimInstance Win32_VideoController | ForEach-Object {
        $taskPci=[regex]::Match([string]$_.PNPDeviceID,'(?i)VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})')
        [pscustomobject]@{name=$_.Name;status=$_.Status;device_error_code=$_.ConfigManagerErrorCode;
            pci_id=if($taskPci.Success){($taskPci.Groups[1].Value+':'+$taskPci.Groups[2].Value).ToLowerInvariant()}else{$null}}
    })
    foreach($taskGpu in $taskReport.gpus){
        if($taskGpu.device_error_code -eq 0){Add-Check $taskGpu.name 'PASS' 'Windows reports device error code 0.'}
        else{Add-Check $taskGpu.name 'FAIL' ('Windows device error code '+$taskGpu.device_error_code)}
    }
} catch {Add-Check 'Windows/device inventory' 'UNKNOWN' 'Inventory could not be read.'}
try {
    $taskSmi=(Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue).Source
    if(!$taskSmi){$taskFallback=Join-Path $env:SystemRoot 'System32\nvidia-smi.exe';if(Test-Path -LiteralPath $taskFallback){$taskSmi=$taskFallback}}
    if(!$taskSmi){throw 'unavailable'}
    $taskFields='index,name,pci.device_id,pci.sub_device_id,driver_version,vbios_version,memory.total,memory.used,utilization.gpu,temperature.gpu,power.limit,clocks.sm,clocks.mem'
    $taskCsv=@(& $taskSmi ('--query-gpu='+$taskFields) '--format=csv,noheader,nounits' 2>$null)
    if($LASTEXITCODE -ne 0 -or !$taskCsv.Count){throw 'query failed'}
    $taskRows=@($taskCsv|ConvertFrom-Csv -Header ($taskFields.Split(',')))
    $taskReport.telemetry=@(foreach($taskRow in $taskRows){
        $taskTemperature=Get-Number $taskRow.'temperature.gpu'
        $taskExact=$taskRow.name.Trim() -eq 'NVIDIA GeForce RTX 3060 Ti' -and $taskRow.'pci.device_id'.Trim() -ieq '0x248910DE' -and $taskRow.'pci.sub_device_id'.Trim() -ieq '0x248910DE' -and $taskRow.vbios_version.Trim() -ieq '94.04.6b.00.f4' -and $taskRow.driver_version.Trim() -eq '610.88' -and (Get-Number $taskRow.'memory.total') -eq 8192 -and (Get-Number $taskRow.'power.limit') -eq 200
        if($taskExact){Add-Check 'Compatibility' 'INFO' 'Reference GPU signature matches; motherboard topology and per-PC profile still require verification.'}
        else{Add-Check 'Compatibility' 'WARN' 'This GPU/driver signature is outside the published reference test. Installation is not qualified.'}
        if($null -eq $taskTemperature){Add-Check 'Temperature' 'UNKNOWN' 'The driver did not expose a numeric reading.'}
        elseif($taskTemperature -ge 85){Add-Check 'Temperature' 'WARN' ($taskTemperature.ToString()+' C; investigate cooling/load. 85 C is this project''s advisory threshold.')}
        else{Add-Check 'Temperature' 'PASS' ($taskTemperature.ToString()+' C at this instant; this is not a load/stability test.')}
        [pscustomobject]@{name=$taskRow.name.Trim();device_id=$taskRow.'pci.device_id'.Trim();subsystem_id=$taskRow.'pci.sub_device_id'.Trim();
            driver=$taskRow.driver_version.Trim();vbios=$taskRow.vbios_version.Trim();reference_gpu_signature_match=$taskExact;
            vram_total_mib=(Get-Number $taskRow.'memory.total');vram_used_mib=(Get-Number $taskRow.'memory.used');
            utilization_percent=(Get-Number $taskRow.'utilization.gpu');temperature_c=$taskTemperature;
            power_limit_w=(Get-Number $taskRow.'power.limit');sm_clock_mhz=(Get-Number $taskRow.'clocks.sm');memory_clock_mhz=(Get-Number $taskRow.'clocks.mem')}
    })
} catch {Add-Check 'NVIDIA telemetry' 'UNKNOWN' 'nvidia-smi is missing or this query is unsupported; no setting was changed.'}
try {
    $taskReport.secure_boot_enabled=[bool](Confirm-SecureBootUEFI)
    if($taskReport.secure_boot_enabled){Add-Check 'Secure Boot' 'INFO' 'Enabled. The unsigned experimental EFI cannot run in this configuration.'}
    else{Add-Check 'Secure Boot' 'INFO' 'Disabled. This permits the unsigned EFI; disabling it is not a general health recommendation.'}
} catch {Add-Check 'Secure Boot' 'UNKNOWN' 'Requires an elevated UEFI-capable Windows session.'}
try {
    $taskReport.fast_startup_enabled=[bool](Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled)
    if($taskReport.fast_startup_enabled){Add-Check 'Fast Startup' 'WARN' 'Enabled; hybrid shutdown/resume is not qualified for the reference unlock.'}
    else{Add-Check 'Fast Startup' 'PASS' 'Disabled; a full startup is available.'}
} catch {Add-Check 'Fast Startup' 'UNKNOWN' 'The saved preference could not be read.'}
if($taskAdmin){
    try {
        if(-not ('RtxMaxBootEvidence' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'tools\BootEvidence.cs')}
        $taskBoot=[RtxMaxBootEvidence]::ReadCurrent()
        $taskReport.boot_evidence=$taskBoot
        if($taskBoot.UnlockPathMatched){Add-Check 'Actual boot route' 'PASS' 'UEFI BootCurrent points to the automatic unlock path; file/partition checks follow.'}
        else{Add-Check 'Actual boot route' 'INFO' 'BootCurrent is another loader. This does not establish an unlocked session.'}
    } catch {Add-Check 'Actual boot route' 'UNKNOWN' 'Firmware boot evidence could not be read.'}
    try {
        $taskReport.bitlocker=@(Get-BitLockerVolume | ForEach-Object {
            [pscustomobject]@{mount_point=$_.MountPoint;volume_status=[string]$_.VolumeStatus;protection=[string]$_.ProtectionStatus}
        })
        foreach($taskVolume in $taskReport.bitlocker){
            if($taskVolume.volume_status -eq 'FullyEncrypted' -and $taskVolume.protection -ne 'On'){
                Add-Check ('BitLocker '+$taskVolume.mount_point) 'WARN' 'Encrypted, but protection is suspended/off.'
            }else{Add-Check ('BitLocker '+$taskVolume.mount_point) 'INFO' ($taskVolume.volume_status+' / protection '+$taskVolume.protection)}
        }
    } catch {Add-Check 'BitLocker' 'UNKNOWN' 'Encryption status could not be read; no recovery protector data was exported.'}
}
try {
    if($taskOs){
        $taskEventErrors=@()
        $taskEvents=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$taskOs.LastBootUpTime;Level=@(1,2,3)} -ErrorAction SilentlyContinue -ErrorVariable taskEventErrors |
            Where-Object {$_.TimeCreated.ToUniversalTime() -ge $taskOs.LastBootUpTime.ToUniversalTime() -and $_.ProviderName -match '^(Display|nvlddmkm|Microsoft-Windows-WHEA-Logger|Microsoft-Windows-Kernel-WHEA|Microsoft-Windows-WER-SystemErrorReporting|BugCheck)$'})
        if(@($taskEventErrors|Where-Object {$_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'}).Count){throw 'Event query failed.'}
        $taskReport.relevant_error_events=@($taskEvents|ForEach-Object{[pscustomobject]@{utc=$_.TimeCreated.ToUniversalTime().ToString('o');provider=$_.ProviderName;id=$_.Id;level=$_.Level}})
        if($taskEvents.Count){Add-Check 'Current-boot event log' 'WARN' ($taskEvents.Count.ToString()+' relevant warning/error events; investigate before calling the system stable.')}
        else{Add-Check 'Current-boot event log' 'PASS' 'No matching warning/error events returned; this is one diagnostic signal.'}
    }
} catch {Add-Check 'Current-boot event log' 'UNKNOWN' 'The event log could not be queried.'}
$taskRoots=@()
if($InstallRoot){$taskRoots=@([IO.Path]::GetFullPath($InstallRoot))}
else{$taskRoots=@(Get-PSDrive -PSProvider FileSystem|Where-Object {$_.Root -match '^[A-Z]:\\$'}|ForEach-Object {$_.Root})}
if(!$ManifestPath){$ManifestPath=Join-Path $PSScriptRoot 'results\reference-artifacts.json'}
foreach($taskRoot in $taskRoots){
    $taskEfi=Join-Path $taskRoot 'EFI\PalitUnlock\AUTO.EFI'
    if(!(Test-Path -LiteralPath $taskEfi -ErrorAction SilentlyContinue)){continue}
    try {
        $taskManifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
        $taskOk=(Same-Hash $taskEfi $taskManifest.efi_sha256) -and (Same-Hash (Join-Path $taskRoot 'RTX-UNLOCK\PROFILE.BIN') $taskManifest.profile_sha256)
        if(@($taskManifest.assets).Count -ne 5){throw 'Expected five assets'}
        foreach($taskAsset in $taskManifest.assets){
            if($taskAsset.name -notmatch '^[a-zA-Z0-9_.-]+$'){throw 'Unsafe asset name'}
            $taskOk=$taskOk -and (Same-Hash (Join-Path $taskRoot ('RTX-UNLOCK\ASSETS\'+$taskAsset.name)) $taskAsset.sha256)
        }
        $taskPartition=$null;$taskInternal=$false;$taskCurrentMatches=$false
        if($taskRoot -match '^([A-Za-z]):\\?$'){
            $taskPartition=Get-Partition -DriveLetter $Matches[1] -ErrorAction SilentlyContinue
            if($taskPartition){
                $taskInternal=($taskPartition|Get-Disk).BusType -ne 'USB'
                if($taskReport.boot_evidence -and $taskReport.boot_evidence.PartitionGuid){
                    $taskCurrentMatches=([guid]$taskReport.boot_evidence.PartitionGuid -eq [guid]$taskPartition.Guid) -and $taskReport.boot_evidence.UnlockPathMatched -and $taskInternal
                }
            }
        }
        $taskReport.installation+= [pscustomobject]@{root=$taskRoot;artifact_hashes_match=$taskOk;non_usb_disk=$taskInternal;actual_current_boot_matches=$taskCurrentMatches}
        if($taskOk){Add-Check 'Installed files' 'PASS' ('EFI, profile and five assets match the selected manifest at '+$taskRoot)}
        else{Add-Check 'Installed files' 'WARN' 'Files differ from the selected reference/build manifest. A different local build needs its own manifest.'}
        if($taskCurrentMatches){Add-Check 'Internal startup' 'PASS' 'Actual boot partition and internal file location agree. Benchmarking is still needed for performance proof.'}
    } catch {Add-Check 'Installation inspection' 'UNKNOWN' 'Files were found, but one or more hashes/partition checks could not be read.'}
}
if(!$taskReport.installation.Count){Add-Check 'Installation' 'UNKNOWN' 'No readable automatic installation found. A hidden/unmounted ESP or manual USB boot may be in use.'}
Add-Check 'Unlock performance' 'UNKNOWN' 'Not measured by this health check. Compare matched, correctness-checked before/after GEMM results.'
$taskReport.checks=@($taskChecks.ToArray())
$taskFailures=@($taskChecks|Where-Object {$_.status -eq 'FAIL'}).Count
$taskWarnings=@($taskChecks|Where-Object {$_.status -eq 'WARN'}).Count
$taskReport.summary=[ordered]@{failures=$taskFailures;warnings=$taskWarnings;
    unknown_checks=@($taskChecks|Where-Object {$_.status -eq 'UNKNOWN'}).Count;
    assessment=if($taskFailures){'issues_observed'}elseif($taskWarnings){'review_warnings'}else{'no_issues_observed_in_checked_items'};
    lifetime_stability_certified=$false}
Write-Host "`nAssessment: $($taskReport.summary.assessment)" -ForegroundColor Cyan
if(!$NoReport){
    if(!$OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot ('reports\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))}
    if(Test-Path -LiteralPath $OutputDirectory){throw 'Report directory already exists; refusing to overwrite.'}
    [void](New-Item -ItemType Directory -Path $OutputDirectory)
    $taskReport|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $OutputDirectory 'health.json') -Encoding UTF8
    $taskLines=@('# RTX-MAX health check','',('Captured UTC: '+$taskReport.captured_utc),'','| Check | Status | Detail |','| --- | --- | --- |')
    foreach($taskCheck in $taskChecks){$taskLines+=('| '+$taskCheck.name.Replace('|','/')+' | '+$taskCheck.status+' | '+$taskCheck.detail.Replace('|','/')+' |')}
    $taskLines+=@('','Performance unlock was not measured. Reports may contain partition GUIDs and boot descriptions; review before sharing.')
    $taskLines|Set-Content -LiteralPath (Join-Path $OutputDirectory 'health.md') -Encoding UTF8
    Write-Host ('Report: '+$OutputDirectory) -ForegroundColor DarkGray
}
if($PassThru){[pscustomobject]$taskReport}
