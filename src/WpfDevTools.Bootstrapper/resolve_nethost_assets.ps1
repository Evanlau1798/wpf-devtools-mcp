param(
    [Parameter(Mandatory = $true)]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$rid = switch ($Platform) {
    'Win32' { 'win-x86' }
    'x64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
    default { throw "Unsupported platform: $Platform" }
}

$dotnetRoot = if ($env:DOTNET_ROOT) {
    $env:DOTNET_ROOT
}
else {
    Split-Path (Get-Command dotnet).Source -Parent
}

$hostPackRoot = Join-Path $dotnetRoot "packs\Microsoft.NETCore.App.Host.$rid"
if (-not (Test-Path $hostPackRoot)) {
    throw "Host pack root not found: $hostPackRoot"
}

$packVersionDir = Get-ChildItem $hostPackRoot -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1

if (-not $packVersionDir) {
    throw "No host pack versions found under: $hostPackRoot"
}

$nativeDir = Join-Path $packVersionDir.FullName "runtimes\$rid\native"
$requiredFiles = @('nethost.h', 'hostfxr.h', 'coreclr_delegates.h', 'libnethost.lib')

foreach ($file in $requiredFiles) {
    $source = Join-Path $nativeDir $file
    if (-not (Test-Path $source)) {
        throw "Required net host asset missing: $source"
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item (Join-Path $nativeDir $file) (Join-Path $OutputDir $file) -Force
}
