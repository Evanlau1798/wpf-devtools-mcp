[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$allowedLicenses = @('MIT')

function Read-RequiredJson {
    param([Parameter(Mandatory = $true)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required Composer built-in pack file is missing: $Path"
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Composer built-in pack JSON is invalid: $Path $($_.Exception.Message)"
    }
}

function Get-NormalizedTextSha256 {
    param(
        [Parameter(Mandatory = $true)] [System.Security.Cryptography.HashAlgorithm]$Algorithm,
        [Parameter(Mandatory = $true)] [System.IO.Stream]$Stream
    )

    $reader = [System.IO.StreamReader]::new($Stream, [System.Text.Encoding]::UTF8, $true)
    try {
        $text = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $normalizedText = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($normalizedText)
    return [System.Convert]::ToBase64String($Algorithm.ComputeHash($bytes))
}

function Get-PackRelativePaths {
    param([Parameter(Mandatory = $true)] [string]$PackRoot)

    $rootPrefix = $PackRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    return @(Get-ChildItem -LiteralPath $PackRoot -Recurse -File | ForEach-Object {
        if (-not $_.FullName.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Composer built-in pack file is outside its expected root: $($_.FullName)"
        }

        $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
    } | Sort-Object)
}

function Test-SourcePolicy {
    param(
        [Parameter(Mandatory = $true)] [string]$PackKey,
        [Parameter(Mandatory = $true)] [string]$SourceLockPath
    )

    $sourceLock = Read-RequiredJson -Path $SourceLockPath
    $sources = @($sourceLock.sources)
    if ($sources.Count -eq 0) {
        throw "Composer built-in pack source provenance is empty: $PackKey"
    }

    foreach ($source in $sources) {
        if ($allowedLicenses -notcontains [string]$source.license) {
            throw "Composer built-in pack $PackKey uses an unapproved license: $($source.license)"
        }

        $sourceUri = $null
        if (-not [System.Uri]::TryCreate([string]$source.url, [System.UriKind]::Absolute, [ref]$sourceUri) -or
            $sourceUri.Scheme -ne 'https' -or
            [string]::IsNullOrWhiteSpace([string]$source.version)) {
            throw "Composer built-in pack source provenance is incomplete or non-HTTPS: $PackKey"
        }

        $localPath = if ($source.PSObject.Properties.Name -contains 'localPath') {
            [string]$source.localPath
        }
        else {
            ''
        }
        if ($localPath -match '^[A-Za-z]:[\\/]' -or $localPath.StartsWith('\\') -or $localPath.StartsWith('/')) {
            throw "Composer built-in pack $PackKey source uses a local absolute path: $localPath"
        }
    }
}

function Test-ReviewedBaseline {
    param(
        [Parameter(Mandatory = $true)] [string]$RepoRootPath,
        [Parameter(Mandatory = $true)] [string]$PackRoot,
        [Parameter(Mandatory = $true)] [string]$PackId,
        [Parameter(Mandatory = $true)] [string]$Version
    )

    $packKey = "$PackId/$Version"
    $baselineRoot = Join-Path $RepoRootPath "packs\baselines\$PackId\$Version"
    $baselineArchivePath = Join-Path $baselineRoot "archives\$PackId-$Version.zip"
    $readinessPath = Join-Path $baselineRoot "reports\$PackId-$Version.readiness.json"
    $readiness = Read-RequiredJson -Path $readinessPath
    if ([string]$readiness.schemaVersion -ne 'wpfdevtools.pack-readiness-report.v1' -or
        -not [bool]$readiness.valid -or
        [string]$readiness.requestedLevel -ne 'release') {
        throw "Composer built-in pack readiness is not release-valid: $packKey"
    }

    if (-not (Test-Path -LiteralPath $baselineArchivePath -PathType Leaf)) {
        throw "Composer built-in pack baseline archive is missing: $packKey"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivePrefix = "$PackId/$Version/"
    $relativePackPaths = @(Get-PackRelativePaths -PackRoot $PackRoot)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($baselineArchivePath)
    try {
        $archiveEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $relativeArchivePaths = @($archiveEntries | ForEach-Object {
            if (-not $_.FullName.StartsWith($archivePrefix, [System.StringComparison]::Ordinal)) {
                throw "Composer built-in baseline archive entry is outside $packKey`: $($_.FullName)"
            }

            $_.FullName.Substring($archivePrefix.Length)
        } | Sort-Object)
        if (@($relativeArchivePaths | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
            throw "Composer built-in baseline archive contains duplicate paths: $packKey"
        }

        if (@(Compare-Object -ReferenceObject $relativeArchivePaths -DifferenceObject $relativePackPaths).Count -ne 0) {
            throw 'Composer built-in pack file set does not match its reviewed baseline archive.'
        }

        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            foreach ($entry in $archiveEntries) {
                $relativePath = $entry.FullName.Substring($archivePrefix.Length)
                $packPath = Join-Path $PackRoot $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                $entryStream = $entry.Open()
                try {
                    $archiveHash = Get-NormalizedTextSha256 -Algorithm $sha256 -Stream $entryStream
                }
                finally {
                    $entryStream.Dispose()
                }

                $packStream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $packPath).Path)
                try {
                    $packHash = Get-NormalizedTextSha256 -Algorithm $sha256 -Stream $packStream
                }
                finally {
                    $packStream.Dispose()
                }

                if ($archiveHash -ne $packHash) {
                    throw "Composer built-in pack file hash mismatch: $packKey/$relativePath"
                }
            }
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

$repoRootPath = (Resolve-Path -LiteralPath $RepoRoot).Path
$builtinRoot = Join-Path $repoRootPath 'packs\builtin'
$baselinePolicyPath = Join-Path $repoRootPath 'packs\baselines\builtin-pack-policy.json'
$baselinePolicy = Read-RequiredJson -Path $baselinePolicyPath
if ([string]$baselinePolicy.schemaVersion -ne 'wpfdevtools.builtin-pack-baseline-policy.v1') {
    throw "Unsupported Composer built-in baseline policy: $baselinePolicyPath"
}

$baselineExemptions = @{}
foreach ($exemption in @($baselinePolicy.baselineExemptions)) {
    $key = "$([string]$exemption.id)/$([string]$exemption.version)"
    if ([string]::IsNullOrWhiteSpace([string]$exemption.id) -or
        [string]::IsNullOrWhiteSpace([string]$exemption.version) -or
        [string]::IsNullOrWhiteSpace([string]$exemption.reason) -or
        $baselineExemptions.ContainsKey($key)) {
        throw "Invalid or duplicate Composer built-in baseline exemption: $key"
    }

    $baselineExemptions[$key] = [string]$exemption.reason
}

$packRoots = @(Get-ChildItem -LiteralPath $builtinRoot -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'pack.json') -PathType Leaf
    }
} | Sort-Object FullName)
if ($packRoots.Count -eq 0) {
    throw "No Composer built-in packs were found under: $builtinRoot"
}

$packKeys = @()
foreach ($packRootDirectory in $packRoots) {
    $packId = $packRootDirectory.Parent.Name
    $version = $packRootDirectory.Name
    $packKey = "$packId/$version"
    $packKeys += $packKey
    $pack = Read-RequiredJson -Path (Join-Path $packRootDirectory.FullName 'pack.json')
    $installManifest = Read-RequiredJson -Path (Join-Path $packRootDirectory.FullName 'install.manifest.json')
    if ([string]$pack.id -ne $packId -or [string]$pack.version -ne $version -or
        [string]$installManifest.id -ne $packId -or [string]$installManifest.version -ne $version -or
        [string]$installManifest.scope -ne 'composer-builtin') {
        throw "Composer built-in pack identity does not match its directory: $packKey"
    }

    $lockFile = [string]$pack.source.lockFile
    if ([string]::IsNullOrWhiteSpace($lockFile) -or [System.IO.Path]::IsPathRooted($lockFile)) {
        throw "Composer built-in pack source lock path is invalid: $packKey"
    }

    $packRootPrefix = $packRootDirectory.FullName.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $sourceLockPath = [System.IO.Path]::GetFullPath((Join-Path $packRootDirectory.FullName $lockFile))
    if (-not $sourceLockPath.StartsWith($packRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Composer built-in pack source lock escapes its pack root: $packKey"
    }

    Test-SourcePolicy -PackKey $packKey -SourceLockPath $sourceLockPath
    if (-not $baselineExemptions.ContainsKey($packKey)) {
        Test-ReviewedBaseline -RepoRootPath $repoRootPath -PackRoot $packRootDirectory.FullName -PackId $packId -Version $version
        Write-Output "Validated Composer built-in pack: $packKey (reviewed baseline)"
    }
    else {
        Write-Output "Validated Composer built-in pack: $packKey (baseline exempt: $($baselineExemptions[$packKey]))"
    }
}

$staleExemptions = @($baselineExemptions.Keys | Where-Object { $packKeys -notcontains $_ })
if ($staleExemptions.Count -ne 0) {
    throw "Composer built-in baseline policy contains stale exemption(s): $($staleExemptions -join ', ')"
}

Write-Output "Validated $($packRoots.Count) Composer built-in pack(s)."
