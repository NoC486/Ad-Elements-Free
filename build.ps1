param([switch]$Test, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$dotnetPath = Join-Path $PSScriptRoot '..\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetPath build (Join-Path $PSScriptRoot 'AdElementsFree.sln') -c Release -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Test) {
    foreach ($project in @('AdElementsFree.Tests', 'AdElementsFree.WindowsTests')) {
        & $dotnetPath (Join-Path $PSScriptRoot "tests\$project\bin\Release\net8.0-windows\$project.dll")
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
if ($Publish) {
    & $dotnetPath publish (Join-Path $PSScriptRoot 'src\AdElementsFree') -c Release -r win-x64 --self-contained true -o (Join-Path $PSScriptRoot 'artifacts\win-x64')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
