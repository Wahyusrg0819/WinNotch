param([string]$SigningCertificateThumbprint)
$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $PSScriptRoot 'artifacts\msix-stage'
$published = Join-Path $PSScriptRoot 'dist\WinNotch\WinNotch.exe'
if (-not (Test-Path -LiteralPath $published)) { throw 'Run .\build.ps1 -Publish first.' }
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem -LiteralPath $sdkBin -Directory | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $makeappx) { throw 'Install the Windows SDK to obtain MakeAppx.exe.' }
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'Assets') | Out-Null
Copy-Item -LiteralPath $published -Destination (Join-Path $packageRoot 'WinNotch.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\AppxManifest.xml') -Destination (Join-Path $packageRoot 'AppxManifest.xml') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\images\winnotch-icon.png') -Destination (Join-Path $packageRoot 'Assets\Logo.png') -Force
$output = Join-Path $PSScriptRoot 'dist\WinNotch-0.2.9-x64.msix'
& $makeappx pack /d $packageRoot /p $output /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
if ($SigningCertificateThumbprint) {
    $signtool = Join-Path (Split-Path $makeappx) 'signtool.exe'
    & $signtool sign /fd SHA256 /sha1 $SigningCertificateThumbprint $output
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed. Certificate subject must match CN=WinNotch Preview.' }
    Write-Output "Signed package: $output"
} else {
    Write-Output "Unsigned package: $output. Sign with a trusted certificate before installing. See packaging/README.md."
}
