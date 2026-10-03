param([switch]$Publish, [switch]$Check)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
if ($Check) {
    & $dotnet run --project 'tests\WinNotch.Checks' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'State checks failed.' }
}
if ($Publish) {
    & $dotnet publish 'src\WinNotch\WinNotch.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o 'dist\WinNotch'
} else {
    & $dotnet build 'src\WinNotch\WinNotch.csproj' -c Release
}
if ($LASTEXITCODE -ne 0) { throw 'WinNotch build failed.' }
