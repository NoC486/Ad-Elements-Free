param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnetPath = Join-Path $root '..\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = 'dotnet' }
if (-not $Compiler) { $Compiler = Join-Path $root '..\.tools\InnoSetup\ISCC.exe' }
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Specify -Compiler with the path to Inno Setup 6.7+ ISCC.exe.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src\AdElementsFree\AdElementsFree.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$publish = Join-Path $root 'artifacts\publish\win-x64'
$release = Join-Path $root 'artifacts\release'
if (Test-Path -LiteralPath $publish) { throw 'Use a clean artifacts/publish/win-x64 directory to avoid shipping stale files.' }
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $dotnetPath publish (Join-Path $root 'src\AdElementsFree') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
& (Join-Path $PSScriptRoot 'Copy-RuntimeNotices.ps1') -ProjectAssets (Join-Path $root 'src\AdElementsFree\obj\project.assets.json') -Destination $publish
& $Compiler "/DAppVersion=$version" (Join-Path $PSScriptRoot 'AdElementsFree.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $release "Ad-Elements-Free-$version-win-x64-portable.zip") -Force
Write-Output "Release files: $release"
