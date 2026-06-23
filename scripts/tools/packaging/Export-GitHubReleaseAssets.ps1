param(
    [Parameter(Mandatory)] [string]$InputRoot,
    [Parameter(Mandatory)] [string]$OutputRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [string]$TrustedSignerThumbprint,
    [string]$TrustPolicyPath,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$sidecarWriter = Join-Path $PSScriptRoot 'Write-ReleaseSidecars.ps1'
if (-not (Test-Path $sidecarWriter)) {
    throw "Write-ReleaseSidecars.ps1 was not found: $sidecarWriter"
}

function Resolve-Directory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Assert-ReleaseTag {
    param([Parameter(Mandatory)] [string]$ReleaseTag)

    if ($ReleaseTag -notmatch '^v\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
        throw "Invalid release tag '$ReleaseTag'. Expected a v-prefixed SemVer tag such as v1.2.3 or v1.2.3-dev.1."
    }
}

function ConvertTo-SingleQuotedLiteral {
    param([Parameter(Mandatory)] [string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Get-ExpectedArchiveVersion {
    param([Parameter(Mandatory)] [string]$ReleaseTag)

    return $ReleaseTag.Substring(1)
}

function Test-PrereleaseTag {
    param([Parameter(Mandatory)] [string]$ReleaseTag)

    return $ReleaseTag -match '^v\d+\.\d+\.\d+-[0-9A-Za-z][0-9A-Za-z.-]*$'
}

function Get-ReleaseAssetRecord {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo]$Archive,
        [Parameter(Mandatory)] [string]$ExpectedVersion
    )

    $match = [regex]::Match(
        $Archive.Name,
        '^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$',
        'IgnoreCase, CultureInvariant')
    if (-not $match.Success) {
        throw "Unexpected release archive name '$($Archive.Name)'. Expected release_${ExpectedVersion}_win-<x64|x86|arm64>.zip."
    }

    $version = $match.Groups['version'].Value
    if ($version -ne $ExpectedVersion) {
        throw "Release archive '$($Archive.Name)' does not match release tag v$ExpectedVersion."
    }

    [pscustomobject]@{
        Archive = $Archive
        Architecture = $match.Groups['architecture'].Value.ToLowerInvariant()
    }
}

function Assert-ReleaseAssetSet {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo[]]$Assets,
        [Parameter(Mandatory)] [string]$ReleaseTag
    )

    $expectedVersion = Get-ExpectedArchiveVersion -ReleaseTag $ReleaseTag
    $records = @($Assets | ForEach-Object {
        Get-ReleaseAssetRecord -Archive $_ -ExpectedVersion $expectedVersion
    })

    foreach ($architecture in @('x64', 'x86')) {
        $matches = @($records | Where-Object { $_.Architecture -eq $architecture })
        if ($matches.Count -eq 0) {
            throw "Missing release archive for architecture $architecture."
        }

        if ($matches.Count -gt 1) {
            $names = @($matches | ForEach-Object { $_.Archive.Name }) -join ', '
            throw "Duplicate release archives for architecture ${architecture}: $names"
        }
    }

    $arm64Matches = @($records | Where-Object { $_.Architecture -eq 'arm64' })
    if ($arm64Matches.Count -gt 1) {
        $names = @($arm64Matches | ForEach-Object { $_.Archive.Name }) -join ', '
        throw "Duplicate release archives for architecture arm64: $names"
    }

    if ($arm64Matches.Count -gt 0 -and -not (Test-PrereleaseTag -ReleaseTag $ReleaseTag)) {
        $names = @($arm64Matches | ForEach-Object { $_.Archive.Name }) -join ', '
        throw "ARM64 release archive is prerelease-only and must not be staged for stable tag ${ReleaseTag}: $names"
    }
}

function Get-ReleaseAssets {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$ReleaseTag
    )

    if (-not (Test-Path $Root)) {
        throw "Input root does not exist: $Root"
    }

    $rootFullPath = (Resolve-Path $Root).Path
    $archives = @(Get-ChildItem -Path $rootFullPath -Filter 'release_*.zip' -File -Recurse | Sort-Object FullName)
    $nestedArchives = @($archives | Where-Object {
        -not $_.DirectoryName.Equals($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($nestedArchives.Count -gt 0) {
        $nestedNames = @($nestedArchives | ForEach-Object { $_.FullName }) -join ', '
        throw "Nested release archives are not allowed under input root: $nestedNames"
    }

    $rootArchives = @($archives | Where-Object {
        $_.DirectoryName.Equals($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)
    } | Sort-Object Name)
    Assert-ReleaseAssetSet -Assets ([System.IO.FileInfo[]]$rootArchives) -ReleaseTag $ReleaseTag
    return $rootArchives
}

function New-UploadScriptContent {
    param(
        [Parameter(Mandatory)] [string]$ReleaseTag,
        [Parameter(Mandatory)] [string[]]$AssetNames
    )

    $releaseTagLiteral = ConvertTo-SingleQuotedLiteral -Value $ReleaseTag
    $assetBlock = ($AssetNames | ForEach-Object {
        "    (Join-Path `$PSScriptRoot $(ConvertTo-SingleQuotedLiteral -Value $_))"
    }) -join [Environment]::NewLine
    return @"
param(
    [string]`$ReleaseTag = $releaseTagLiteral
)

`$ErrorActionPreference = 'Stop'
`$assets = @(
$assetBlock
)

`$missingAssets = @(`$assets | Where-Object { -not (Test-Path -LiteralPath `$_ -PathType Leaf) })
if (`$missingAssets.Count -gt 0) {
    throw "Generated release upload script references missing staged release asset(s): `$(`$missingAssets -join ', ')"
}

`$sha256SumsPath = Join-Path `$PSScriptRoot 'SHA256SUMS.txt'
`$releaseNotesPath = Join-Path `$PSScriptRoot 'release-notes.md'
`$sha256Sums = (Get-Content -LiteralPath `$sha256SumsPath -Raw).Trim()
`$releaseNotes = @(
    "# `$ReleaseTag release artifacts",
    "",
    "These assets are the official GitHub Release artifacts for `$ReleaseTag.",
    "",
    "## SHA256 checksums",
    "",
    '``````text',
    `$sha256Sums,
    '``````'
)

if (`$ReleaseTag -match '^v\d+\.\d+\.\d+-[0-9A-Za-z][0-9A-Za-z.-]*$') {
    `$releaseNotes += @(
        "",
        "ARM64 archives, when present, are prerelease-only preview artifacts."
    )
}

Set-Content -LiteralPath `$releaseNotesPath -Value (`$releaseNotes -join [Environment]::NewLine) -Encoding UTF8
& gh release edit `$ReleaseTag --notes-file `$releaseNotesPath

# Upload assets staged next to this script.
& gh release upload `$ReleaseTag `$assets --clobber
"@
}

Assert-ReleaseTag -ReleaseTag $Tag
$inputRootFullPath = (Resolve-Path $InputRoot).Path
$outputRootFullPath = Resolve-Directory -Path $OutputRoot
$stagingRoot = Join-Path $outputRootFullPath $Tag

if (Test-Path $stagingRoot) {
    Remove-Item -Path $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
$assets = Get-ReleaseAssets -Root $inputRootFullPath -ReleaseTag $Tag
if ($assets.Count -eq 0) {
    throw "No release_*.zip archives were found under: $inputRootFullPath"
}

foreach ($asset in $assets) {
    $destinationPath = Join-Path $stagingRoot $asset.Name
    Copy-Item -Path $asset.FullName -Destination $destinationPath -Force
}

$sidecarArguments = @{
    ArchiveRoot = $stagingRoot
    Tag = $Tag
    OutputJson = $true
}

if (-not [string]::IsNullOrWhiteSpace($TrustedSignerThumbprint)) {
    $sidecarArguments.TrustedSignerThumbprint = $TrustedSignerThumbprint
}

if (-not [string]::IsNullOrWhiteSpace($TrustPolicyPath)) {
    $sidecarArguments.TrustPolicyPath = $TrustPolicyPath
}

$manifest = & $sidecarWriter @sidecarArguments | ConvertFrom-Json

$uploadScriptPath = Join-Path $stagingRoot 'upload-gh-release.ps1'
$uploadAssetNames = @($manifest.assets | ForEach-Object { [string]$_.name }) +
    'SHA256SUMS.txt' +
    'release-assets.json' +
    'release-sbom.spdx.json' +
    'package-sbom.spdx.json'
$lateBoundUploadAssetNames = @("release-evidence.json")
$allUploadAssetNames = @($uploadAssetNames + $lateBoundUploadAssetNames)
New-UploadScriptContent -ReleaseTag $Tag -AssetNames ([string[]]$allUploadAssetNames) | Set-Content -Path $uploadScriptPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
