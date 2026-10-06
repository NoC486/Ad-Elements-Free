param([Parameter(Mandatory)][string]$ProjectAssets, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $ProjectAssets -Raw | ConvertFrom-Json
$copied = 0
$dependencies = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
foreach ($library in $dependencies) {
    if ($library.name -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$') { continue }
    $version = ($library.version.Trim('[', ']') -split ',')[0].Trim()
    $relativePath = $library.name.ToLowerInvariant() + '/' + $version
    $package = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $relativePath
        if (Test-Path -LiteralPath $candidate) { $package = $candidate; break }
    }
    if (-not $package) { throw "Runtime package not found: $($library.Name)" }
    $target = Join-Path $Destination ('ThirdParty\' + $library.name + '-' + $version)
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $notices = Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match 'LICENSE|THIRD.PARTY.NOTICES' }
    if (-not $notices) { throw "Runtime license missing: $($library.Name)" }
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $target; $copied++ }
}
if ($copied -lt 2) { throw 'Both runtime packages must have license notices.' }
