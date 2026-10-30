# SPDX-License-Identifier: GPL-2.0-only
[CmdletBinding()]
param(
    [ValidateSet('Manual','Internal')][string]$Mode='Manual',
    [string]$LlvmBin,
    [string]$VcVars,
    [guid]$InternalPartitionGuid=[guid]::Empty,
    [guid]$WindowsPartitionGuid=[guid]::Empty,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$LlvmBin){$LlvmBin=$env:LLVM_BIN}
$taskClang=if($LlvmBin){Join-Path $LlvmBin 'clang.exe'}else{(Get-Command clang.exe -ErrorAction Stop).Source}
$taskLink=if($LlvmBin){Join-Path $LlvmBin 'lld-link.exe'}else{(Get-Command lld-link.exe -ErrorAction Stop).Source}
if(!(Test-Path -LiteralPath $taskClang) -or !(Test-Path -LiteralPath $taskLink)){throw 'LLVM clang and lld-link are required.'}
if(!$env:INCLUDE -or $VcVars){
    if(!$VcVars){
        $taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if(Test-Path -LiteralPath $taskVsWhere){$VcVars=& $taskVsWhere -latest -products * -find 'VC\Auxiliary\Build\vcvars64.bat'|Select-Object -First 1}
        if(!$VcVars){$taskConventional=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat';if(Test-Path -LiteralPath $taskConventional){$VcVars=$taskConventional}}
    }
    if(!$VcVars -or !(Test-Path -LiteralPath $VcVars) -or $VcVars -match '["&|<>^%!\r\n]'){throw 'Install Visual Studio C++ Build Tools/Windows SDK, or pass a valid -VcVars path.'}
    $taskEnvironment=& cmd.exe /d /c "call `"$VcVars`" >nul && set"
    if($LASTEXITCODE -ne 0){throw 'MSVC/Windows SDK environment initialization failed.'}
    foreach($taskLine in $taskEnvironment){if($taskLine -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($Matches[1],$Matches[2],'Process')}}
}
if($Mode -eq 'Internal' -and ($InternalPartitionGuid -eq [guid]::Empty -or $WindowsPartitionGuid -eq [guid]::Empty -or $InternalPartitionGuid -eq $WindowsPartitionGuid)){throw 'Internal mode requires distinct, nonzero, locally verified unlock and Windows ESP GUIDs.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $taskRoot ('build\'+$Mode.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output already exists; refusing to overwrite.'}
if($OutputDirectory.StartsWith((Join-Path $taskRoot 'firmware'),[StringComparison]::OrdinalIgnoreCase)){throw 'Build outside the source snapshot.'}
[void](New-Item -ItemType Directory -Path $OutputDirectory)
$taskStage=Join-Path $OutputDirectory 'source'
[void](New-Item -ItemType Directory -Path $taskStage)
Copy-Item -LiteralPath (Join-Path $taskRoot 'firmware\src') -Destination $taskStage -Recurse
function Format-CGuid([guid]$Value){
    $taskHex=$Value.ToString('N')
    $taskTail=@();for($taskI=16;$taskI -lt 32;$taskI+=2){$taskTail+=('0x'+$taskHex.Substring($taskI,2))}
    '{0x'+$taskHex.Substring(0,8)+',0x'+$taskHex.Substring(8,4)+',0x'+$taskHex.Substring(12,4)+',{'+($taskTail -join ',')+'}}'
}
if($Mode -eq 'Internal'){
    $taskHeader=@('#pragma once','/* Generated locally; do not publish private partition identities. */','#define PALIT_INTERNAL_CONFIG_VALIDATED 1',
        ('static const EFI_GUID PalitUnlockPartitionGuid = '+(Format-CGuid $InternalPartitionGuid)+';'),
        ('static const EFI_GUID PalitWindowsPartitionGuid = '+(Format-CGuid $WindowsPartitionGuid)+';'))
    $taskHeader|Set-Content -LiteralPath (Join-Path $taskStage 'src\gop\palit_internal_config.h') -Encoding ASCII
}
$taskDefines=@('-DGC_FULL_FLR','-DGC_FULL_UNLOCK','-DGU_GOP_EXPERIMENT','-D__KERNEL__','-DGU_UEFI_PORT','-DGU_RUNTIME_PROFILE_BUILD','-DGC_WINDOWS_HANDOFF','-DGU_SSD_EXPERIMENT','-DGC_SHARED_TEXT_GOP')
if($Mode -eq 'Internal'){$taskDefines+=@('-DGC_PRODUCTION','-DGC_PALIT_INTERNAL_AUTO')}
$taskFlags=@('-target','x86_64-pc-windows-msvc','-std=c11','-ffreestanding','-fno-builtin','-fno-stack-protector','-mno-red-zone','-fshort-wchar','-Os','-Wall','-Wextra','-Werror','-Wno-unused-parameter','-Wno-unused-function')+$taskDefines+@('-I',(Join-Path $taskStage 'src\unlock\compat'),'-I',(Join-Path $taskRoot 'firmware\deps\edk2\MdePkg\Include'),'-I',(Join-Path $taskRoot 'firmware\deps\edk2\MdePkg\Include\X64'))
$taskSources=@('gop/app.c','gop/full.c','gop/leaf.c','gop/reset.c','gop/visual.c','gop/windows.c','unlock/runtime.c','unlock/format.c','unlock/app.c','unlock/app_io.c','unlock/ownership.c','unlock/sha256.c','unlock/checks.c','unlock/port.c','unlock/compat.c','unlock/runtime_profile.c','unlock/vendor/unlock_610.c','unlock/vendor/prep_610.c','unlock/vendor/layout_610.c','unlock/vendor/vbios_610.c','unlock/vendor/gfw_eval.c')
$taskObjects=@()
foreach($taskSource in $taskSources){
    $taskObj=Join-Path $OutputDirectory ($taskSource.Replace('/','_').Replace('.c','.obj'))
    & $taskClang @taskFlags -c (Join-Path $taskStage ('src\'+$taskSource)) -o $taskObj
    if($LASTEXITCODE -ne 0){throw ('Compile failed: '+$taskSource)}
    $taskObjects+=$taskObj
}
$taskEfi=Join-Path $OutputDirectory 'BOOTX64.EFI';$taskMap=Join-Path $OutputDirectory 'BOOTX64.map'
& $taskLink /nologo /nodefaultlib /subsystem:efi_application /entry:efi_main /machine:x64 /base:0 /dynamicbase /nxcompat /timestamp:0 /opt:ref /opt:icf ('/map:'+$taskMap) ('/out:'+$taskEfi) @taskObjects
if($LASTEXITCODE -ne 0){throw 'EFI link failed.'}
$taskMapText=Get-Content -LiteralPath $taskMap -Raw
foreach($taskRequired in @('GfRun','GcLeafSingleOwner','GwStart','GuProfileLoad')){if($taskMapText -notmatch ('\b'+$taskRequired+'\b')){throw ('Runtime path missing: '+$taskRequired)}}
foreach($taskForbidden in @('GuProfileCreate','GuProfileRefresh','GuProfileCommit')){if($taskMapText -match ('\b'+$taskForbidden+'\b')){throw ('Unexpected profile mutation: '+$taskForbidden)}}
$taskBytes=[IO.File]::ReadAllBytes($taskEfi)
$taskPe=[BitConverter]::ToInt32($taskBytes,0x3c)
if([BitConverter]::ToUInt32($taskBytes,$taskPe) -ne 0x4550 -or [BitConverter]::ToUInt16($taskBytes,$taskPe+4) -ne 0x8664){throw 'Not an AMD64 PE image.'}
$taskOptional=$taskPe+24
if([BitConverter]::ToUInt16($taskBytes,$taskOptional) -ne 0x20b -or [BitConverter]::ToUInt16($taskBytes,$taskOptional+68) -ne 10){throw 'Not a PE32+ EFI application.'}
foreach($taskIndex in @(1,12,13)){
    if([BitConverter]::ToUInt64($taskBytes,$taskOptional+112+8*$taskIndex) -ne 0){throw 'EFI imports, IAT or delayed imports detected.'}
}
$taskManifest=[ordered]@{schema_version=1;mode=$Mode;hardware_tested=$false;
    note='Build success is not hardware qualification. Use a locally captured and reviewed runtime profile.';
    efi_sha256=(Get-FileHash -LiteralPath $taskEfi).Hash.ToLowerInvariant();
    automatic_start=($Mode -eq 'Internal');profile_automatic_creation=$false;
    internal_partition_guid=if($Mode -eq 'Internal'){$InternalPartitionGuid.ToString()}else{$null};
    windows_partition_guid=if($Mode -eq 'Internal'){$WindowsPartitionGuid.ToString()}else{$null};
    defines=$taskDefines;source_base_commit='5c076dc4ee7fa369639669fa591ac74d8f6dfcd8';
    imports=0;iat=0;delay_imports=0}
$taskManifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'build.json') -Encoding UTF8
Write-Host ('Built '+$Mode+' EFI: '+$taskEfi) -ForegroundColor Green
Write-Host 'No boot, disk, encryption or firmware configuration was changed.' -ForegroundColor Cyan
