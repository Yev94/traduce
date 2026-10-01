param([switch]$Test, [switch]$LiveTest, [switch]$DesktopTest, [switch]$ContractTest)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Se requiere .NET Framework 4.8 (incluido en Windows 10/11).' }
$destination = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sources = @('AssemblyInfo.cs', 'Core.cs', 'Settings.cs', 'Providers.cs', 'CliConnections.cs', 'ConnectionForm.cs', 'Clipboard.cs', 'Region.cs', 'Ocr.cs', 'CaptureLog.cs', 'PopupPlacement.cs', 'Program.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$references = @('/r:System.dll', '/r:System.Security.dll', '/r:System.Net.Http.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll')
$framework = Split-Path $compiler
$metadata = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata\*\Windows.winmd" -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -match '^10\.' } | Sort-Object { [version]$_.Directory.Name } | Select-Object -Last 1
if (-not $metadata) { throw 'Para compilar se necesita el Windows 10/11 SDK (Windows.winmd). Para ejecutar basta Windows 10/11.' }
$references += "/r:$($metadata.FullName)"
$references += @('System.Runtime.WindowsRuntime.dll', 'System.Runtime.dll', 'System.Threading.Tasks.dll', 'System.ObjectModel.dll') | ForEach-Object { "/r:$framework\$_" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/win32manifest:$PSScriptRoot\app.manifest" "/out:$destination\Traduce.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido compilar Traduce.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $destination 'Traduce.exe.config') -Force
if ($Test -or $LiveTest -or $DesktopTest -or $ContractTest) {
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output "/win32manifest:$PSScriptRoot\app.manifest" /main:Traduce.Tests "/out:$destination\Traduce.Tests.exe" @references @sources (Join-Path $PSScriptRoot 'Tests.cs') (Join-Path $PSScriptRoot 'ProviderTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'No se han podido compilar las pruebas.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $destination 'Traduce.Tests.exe.config') -Force
    $testArgs = @()
    if ($LiveTest) { $testArgs += '--live' }
    if ($DesktopTest) { $testArgs += '--desktop' }
    if ($ContractTest) { $testArgs += '--contracts' }
    & (Join-Path $destination 'Traduce.Tests.exe') @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas han fallado.' }
}
Write-Output "Compilado: $destination\Traduce.exe"
