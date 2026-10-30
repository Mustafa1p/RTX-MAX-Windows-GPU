# SPDX-License-Identifier: GPL-2.0-only
[CmdletBinding()]
param([string]$Compiler,[string]$OutputDirectory,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $taskRoot 'build\desktop'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new build directory; existing builds are preserved.'}
if(!$Compiler){$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe';if(Test-Path -LiteralPath $taskVsWhere){$Compiler=& $taskVsWhere -latest -products * -find 'MSBuild\Current\Bin\Roslyn\csc.exe'|Select-Object -First 1}}
if(!$Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Install Visual Studio C++/.NET Build Tools with Roslyn, or pass -Compiler.'}
$taskReferences=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
if(!(Test-Path -LiteralPath (Join-Path $taskReferences 'PresentationFramework.dll'))){throw '.NET Framework 4.7.2 developer reference assemblies are required to build.'}
[void](New-Item -ItemType Directory -Path $OutputDirectory)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive=Join-Path $OutputDirectory 'source.zip'
$taskNames=@(& git -C $taskRoot ls-files --cached --others --exclude-standard|Sort-Object -Unique)
if($LASTEXITCODE -ne 0){throw 'Source inventory failed.'}
$taskStream=[IO.File]::Open($taskArchive,[IO.FileMode]::CreateNew)
$taskZip=[IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($taskName in $taskNames){
  if($taskName -match '(^|/)(build|bin|obj|node_modules|__pycache__|\.git)(/|$)' -or $taskName -match '\.(exe|dll|efi|bin|rmp|key|pfx|pem|pyc|zip)$' -or ($taskName.StartsWith('reports/') -and $taskName -ne 'reports/.gitkeep')){continue}
  $taskSource=Join-Path $taskRoot $taskName
  if(!(Test-Path -LiteralPath $taskSource -PathType Leaf)){continue}
  [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$taskSource,$taskName,[IO.Compression.CompressionLevel]::Optimal)
 }
}finally{$taskZip.Dispose();$taskStream.Dispose()}
$taskEngine=Join-Path $OutputDirectory 'engine-source.txt'
[IO.File]::WriteAllText($taskEngine,([IO.File]::ReadAllText((Join-Path $taskRoot 'desktop\Benchmark.cs'))+[IO.File]::ReadAllText((Join-Path $taskRoot 'desktop\Core.cs'))),[Text.UTF8Encoding]::new($false))
$taskExe=Join-Path $OutputDirectory 'RTX-MAX.exe'
$taskArgs=@('/nologo','/target:winexe','/platform:x64','/langversion:latest','/optimize+','/deterministic+',('/out:'+$taskExe),('/win32manifest:'+(Join-Path $taskRoot 'desktop\app.manifest')),('/win32icon:'+(Join-Path $taskRoot 'assets\rtxmax.ico')))
foreach($taskAssembly in @('mscorlib','System','System.Core','System.Xaml','WindowsBase','PresentationCore','PresentationFramework','System.Management','System.Web.Extensions','System.Windows.Forms','System.Drawing','System.IO.Compression','System.IO.Compression.FileSystem','System.Net.Http','System.Xml')){$taskArgs+=('/reference:'+(Join-Path $taskReferences ($taskAssembly+'.dll')))}
$taskArgs+=@('/nostdlib+',('/resource:'+(Join-Path $taskRoot 'desktop\MainWindow.xaml')+',MainWindow.xaml'),('/resource:'+(Join-Path $taskRoot 'assets\rtxmax.ico')+',app.ico'),('/resource:'+(Join-Path $taskRoot 'desktop\benchmark-packages.json')+',benchmark-packages.json'),('/resource:'+(Join-Path $taskRoot 'results\reference-artifacts.json')+',reference-artifacts.json'),('/resource:'+$taskArchive+',source.zip'),('/resource:'+$taskEngine+',engine-source'))
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'desktop') -Filter '*.cs' -File|ForEach-Object FullName)+@(Join-Path $taskRoot 'tools\BootEvidence.cs')
& $Compiler @taskArgs @taskSources
if($LASTEXITCODE -ne 0){throw 'Desktop build failed.'}
if(!$SkipTests){
 $taskTest=Start-Process -FilePath $taskExe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $OutputDirectory 'self-tests.log') -RedirectStandardError (Join-Path $OutputDirectory 'self-tests-errors.log')
 if($taskTest.ExitCode -ne 0){throw 'Native desktop self-tests failed. See build logs.'}
 Get-Content -LiteralPath (Join-Path $OutputDirectory 'self-tests.log')
}
[ordered]@{version='0.2.0';target='Windows x64 / .NET Framework 4.7.2+';exe_sha256=(Get-FileHash -LiteralPath $taskExe).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $taskExe).Length;source_included=$true;nvidia_binaries_included=$false;firmware_payloads_included=$false;native_self_tests_passed=(!$SkipTests)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'desktop-build.json') -Encoding UTF8
Write-Host ('Built: '+$taskExe) -ForegroundColor Green
