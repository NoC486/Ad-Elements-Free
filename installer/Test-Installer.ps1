param([Parameter(Mandatory)][string]$Setup, [switch]$AllUsers)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src\AdElementsFree\AdElementsFree.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$mode = if ($AllUsers) { 'machine' } else { 'user' }
$testRoot = Join-Path $root "artifacts\installer-smoke\$version\$mode"
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$installDir = Join-Path $testRoot 'custom install directory'
if ($AllUsers) { $installDir = 'D:\Program Files\Ad Elements Free Installer Test' }
$hive = if ($AllUsers) { 'HKLM:' } else { 'HKCU:' }
$registryPath = "$hive\Software\Microsoft\Windows\CurrentVersion\Uninstall\AdElementsFree.NoC486.InstallerTest_is1"
if (Test-Path -LiteralPath $registryPath) { throw 'A prior isolated test installation exists. Inspect it before retrying.' }
if (Test-Path -LiteralPath $installDir) { throw 'Use a new, empty isolated install location.' }
$modeArgument = if ($AllUsers) { '/ALLUSERS' } else { '/CURRENTUSER' }
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Setup).Path -ArgumentList @($modeArgument,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $installDir + '"'),('/LOG="' + (Join-Path $testRoot 'install.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath $registryPath)) { throw 'Expected isolated uninstall registration missing.' }
$installedPath = (Get-ItemProperty -LiteralPath $registryPath).InstallLocation.TrimEnd('\')
if ($installedPath -ne $installDir.TrimEnd('\')) { throw 'Unexpected installation directory. Do not uninstall automatically.' }
Write-Output "PASS Custom installation directory with spaces and $mode registration"
$publish = Join-Path $root "artifacts\publish\$version\win-x64"
foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    $installed = Join-Path $installDir $relative
    if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
        throw "Installed payload mismatch: $relative"
    }
}
Write-Output 'PASS Every installed runtime, application and license file matches the release payload'
$exe = Join-Path $installDir 'AdElementsFree.exe'
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($exe))
try {
    $reader.BaseStream.Position = 0x3c
    $offset = $reader.ReadInt32()
    $reader.BaseStream.Position = $offset + 4
    if ($reader.ReadUInt16() -ne 0x8664) { throw 'Application is not x64.' }
} finally { $reader.Dispose() }
if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "$version.0") { throw 'Wrong application version.' }
Write-Output "PASS Installed application is Windows x64 version $version"
$uninstaller = Join-Path $installDir 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $registryPath)) { throw 'Uninstall did not remove the isolated application.' }
Write-Output 'PASS Uninstall removes the isolated payload and its registration'
