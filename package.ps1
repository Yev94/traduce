param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$innoCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($innoCommand) { $candidates = @($innoCommand.Source) + $candidates }
$innoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) { throw 'Instala Inno Setup 6 para empaquetar: winget install --id JRSoftware.InnoSetup --exact --scope user' }
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
& $innoCompiler /Qp (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el instalador.' }
$files = @('dist\Traduce.exe', 'dist\Traduce.exe.config', 'README.md', 'README.es.md', 'LICENSE', 'assets', 'docs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $release 'Traduce-Windows-x64.zip') -Force
$hashes = @('Traduce-Setup.exe', 'Traduce-Windows-x64.zip') | ForEach-Object {
    $hash = Get-FileHash -LiteralPath (Join-Path $release $_) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $hashes, [Text.Encoding]::ASCII)
Get-ChildItem -LiteralPath $release -File | Select-Object Name, Length
