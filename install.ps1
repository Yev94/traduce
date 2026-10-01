param([switch]$Launch)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'package.ps1')
$setup = Start-Process -FilePath (Join-Path $PSScriptRoot 'release\Traduce-Setup.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=startup' -Wait -PassThru -WindowStyle Hidden
if ($setup.ExitCode -ne 0) { throw "El instalador terminó con código $($setup.ExitCode)." }
$executable = Join-Path $env:LOCALAPPDATA 'Programs\Traduce\Traduce.exe'
Write-Output "Instalado: $executable"
if ($Launch) { Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden }
