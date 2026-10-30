# SPDX-License-Identifier: GPL-2.0-only
$ErrorActionPreference='Stop'
if(-not ('RtxMaxBootEvidence' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'BootEvidence.cs')}
function Make-Option([switch]$Usb){
    $taskGuid=[guid]'11111111-2222-3333-4444-555555555555'
    $taskHd=New-Object byte[] 42
    $taskHd[0]=4;$taskHd[1]=1;$taskHd[2]=42
    [BitConverter]::GetBytes([uint32]1).CopyTo($taskHd,4)
    $taskGuid.ToByteArray().CopyTo($taskHd,24)
    $taskHd[40]=2;$taskHd[41]=2
    $taskName=[Text.Encoding]::Unicode.GetBytes('\EFI\PalitUnlock\AUTO.EFI'+[char]0)
    $taskFile=New-Object byte[] (4+$taskName.Length)
    $taskFile[0]=4;$taskFile[1]=4
    [BitConverter]::GetBytes([uint16]$taskFile.Length).CopyTo($taskFile,2)
    $taskName.CopyTo($taskFile,4)
    $taskPath=[byte[]]($taskHd+$taskFile+@(127,255,4,0))
    if($Usb){$taskPath=[byte[]](@(3,5,6,0,0,0)+$taskPath)}
    $taskDescription=[Text.Encoding]::Unicode.GetBytes('RTXMAX parser fixture'+[char]0)
    $taskHeader=New-Object byte[] 6
    [BitConverter]::GetBytes([uint16]$taskPath.Length).CopyTo($taskHeader,4)
    return ,([byte[]]($taskHeader+$taskDescription+$taskPath))
}
function Require([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Require-Refusal([byte[]]$Data){
    $taskRefused=$false
    try{[void][RtxMaxBootEvidence]::ParseOption($Data)}catch{$taskRefused=$true}
    Require $taskRefused 'Malformed boot option was accepted.'
}
$taskBytes=Make-Option
$taskRead=[RtxMaxBootEvidence]::ParseOption($taskBytes)
Require ($taskRead.UnlockPathMatched -and !$taskRead.UsbNodePresent -and $taskRead.PartitionGuid -eq '11111111-2222-3333-4444-555555555555') 'Valid internal path was not parsed.'
$taskRead=[RtxMaxBootEvidence]::ParseOption((Make-Option -Usb))
Require $taskRead.UsbNodePresent 'USB node was not detected.'
Require-Refusal ([byte[]]@(0,1,2))
Require-Refusal ([byte[]]$taskBytes[0..($taskBytes.Length-2)])
$taskBroken=[byte[]]$taskBytes.Clone();$taskBroken[4]=255;$taskBroken[5]=255;Require-Refusal $taskBroken
$taskBroken=[byte[]]$taskBytes.Clone();$taskBroken[-3]=1;Require-Refusal $taskBroken
$taskRead=[RtxMaxBootEvidence]::ParseOption((Make-Option))
Require ($taskRead.Description -eq 'RTXMAX parser fixture') 'Description parsing failed.'
Write-Host 'PASS: 7 boot-parser checks; synthetic bytes only, no firmware access.' -ForegroundColor Green
