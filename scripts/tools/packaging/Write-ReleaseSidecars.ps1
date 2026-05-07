param(
    [Parameter(Mandatory)] [string]$ArchiveRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [string]$TrustedSignerThumbprint,
    [string]$TrustPolicyPath,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null

function Normalize-SignerThumbprint {
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        return $null
    }

    return $Thumbprint.Replace(' ', '').ToUpperInvariant()
}

function Test-InstallerTestModeEnabled {
    return [string]::Equals([string]$env:WPFDEVTOOLS_INSTALLER_TEST_MODE, '1', [System.StringComparison]::Ordinal)
}

function Test-LocalArchiveMetadataTrustOverrideEnabled {
    return (Test-InstallerTestModeEnabled) -and
        [string]::Equals([string]$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA, '1', [System.StringComparison]::Ordinal)
}

function Assert-ValidSignerThumbprint {
    param(
        [Parameter(Mandatory)] [string]$Thumbprint,
        [Parameter(Mandatory)] [string]$Source
    )

    if ($Thumbprint -notmatch '^[A-F0-9]{40}$') {
        throw "Release trust policy '$Source' must provide a 40-character hexadecimal signer thumbprint."
    }
}

function Resolve-ReleaseTrustPolicy {
    param(
        [string]$TrustedThumbprint,
        [string]$PolicyPath
    )

    if (-not [string]::IsNullOrWhiteSpace($PolicyPath)) {
        if (-not (Test-Path $PolicyPath)) {
            throw "Release trust policy file was not found: $PolicyPath"
        }

        $resolvedPolicyPath = (Resolve-Path $PolicyPath).Path
        $policy = Get-Content -Path $resolvedPolicyPath -Raw | ConvertFrom-Json
        $policyThumbprints = @(
            $policy.trustedSignerThumbprints |
                ForEach-Object { Normalize-SignerThumbprint -Thumbprint ([string]$_) } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )

        if ($policyThumbprints.Count -eq 0) {
            throw "Release trust policy file must declare trustedSignerThumbprints: $resolvedPolicyPath"
        }

        foreach ($thumbprint in $policyThumbprints) {
            Assert-ValidSignerThumbprint -Thumbprint $thumbprint -Source $resolvedPolicyPath
        }

        return [ordered]@{
            Source = 'policyFile'
            PolicyPath = $resolvedPolicyPath
            TrustedThumbprints = @($policyThumbprints | Sort-Object -Unique)
        }
    }

    $explicitThumbprint = Normalize-SignerThumbprint -Thumbprint $TrustedThumbprint
    $source = 'parameter'
    if ([string]::IsNullOrWhiteSpace($explicitThumbprint)) {
        $explicitThumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT)
        $source = 'WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT'
    }

    if ([string]::IsNullOrWhiteSpace($explicitThumbprint)) {
        return [ordered]@{
            Source = $null
            PolicyPath = $null
            TrustedThumbprints = @()
        }
    }

    Assert-ValidSignerThumbprint -Thumbprint $explicitThumbprint -Source $source
    return [ordered]@{
        Source = $source
        PolicyPath = $null
        TrustedThumbprints = @($explicitThumbprint)
    }
}

function Assert-ArchiveSignerTrustPolicy {
    param(
        [object]$SignerMetadata,
        [Parameter(Mandatory)] [object]$TrustPolicy,
        [Parameter(Mandatory)] [string]$ArchiveName
    )

    if ($null -eq $SignerMetadata) {
        return $null
    }

    $signerThumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$SignerMetadata.signerThumbprint)
    if ([string]::IsNullOrWhiteSpace($signerThumbprint)) {
        throw "Release signer metadata for '$ArchiveName' is self-declared but does not include a trusted signer thumbprint."
    }

    if (Test-LocalArchiveMetadataTrustOverrideEnabled) {
        return [ordered]@{
            source = 'installerTestMode'
            trustedSignerThumbprint = $signerThumbprint
        }
    }

    $trustedThumbprints = @($TrustPolicy.TrustedThumbprints)
    if ($trustedThumbprints.Count -eq 0) {
        throw "Release signer metadata for '$ArchiveName' is self-declared; provide a trusted signer thumbprint or policy file before generating release sidecars."
    }

    if ($trustedThumbprints -notcontains $signerThumbprint) {
        throw "Release signer metadata for '$ArchiveName' reported signer '$signerThumbprint', which is not allowed by the trusted signer policy."
    }

    $policy = [ordered]@{
        source = [string]$TrustPolicy.Source
        trustedSignerThumbprint = $signerThumbprint
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$TrustPolicy.PolicyPath)) {
        $policy.policyPath = [string]$TrustPolicy.PolicyPath
    }

    return $policy
}

function Get-ArchiveSignerMetadata {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
        try {
            $manifestEntry = $archive.GetEntry('bin/manifest.json')
            if ($null -eq $manifestEntry) {
                $manifestEntry = $archive.GetEntry('manifest.json')
            }

            if ($null -eq $manifestEntry) {
                return $null
            }

            $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
            try {
                $manifest = ($reader.ReadToEnd() | ConvertFrom-Json)
            }
            finally {
                $reader.Dispose()
            }

            $thumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$manifest.signerThumbprint)
            $subject = if ([string]::IsNullOrWhiteSpace([string]$manifest.signerSubject)) { $null } else { [string]$manifest.signerSubject }
            if ([string]::IsNullOrWhiteSpace($thumbprint) -and [string]::IsNullOrWhiteSpace($subject)) {
                return $null
            }

            return [ordered]@{
                signerThumbprint = $thumbprint
                signerSubject = $subject
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch {
        return $null
    }
}

function Get-ReleaseArchives {
    param([Parameter(Mandatory)] [string]$Root)

    if (-not (Test-Path $Root)) {
        throw "Archive root does not exist: $Root"
    }

    return @(Get-ChildItem -Path $Root -Filter 'release_*.zip' -File | Sort-Object Name)
}

function Get-Sha256FileHashHex {
    param([Parameter(Mandatory)] [string]$Path)

    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }

    return (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

$archiveRootFullPath = (Resolve-Path $ArchiveRoot).Path
$trustPolicy = Resolve-ReleaseTrustPolicy -TrustedThumbprint $TrustedSignerThumbprint -PolicyPath $TrustPolicyPath
$archives = Get-ReleaseArchives -Root $archiveRootFullPath
if ($archives.Count -eq 0) {
    throw "No release_*.zip archives were found under: $archiveRootFullPath"
}

$assets = foreach ($archive in $archives) {
    $hash = Get-Sha256FileHashHex -Path $archive.FullName
    $signerMetadata = Get-ArchiveSignerMetadata -ArchivePath $archive.FullName
    $signerTrustPolicy = Assert-ArchiveSignerTrustPolicy -SignerMetadata $signerMetadata -TrustPolicy $trustPolicy -ArchiveName $archive.Name
    [pscustomobject]@{
        name = $archive.Name
        sizeBytes = $archive.Length
        sha256 = $hash
        signerThumbprint = if ($null -ne $signerMetadata) { [string]$signerMetadata.signerThumbprint } else { $null }
        signerSubject = if ($null -ne $signerMetadata) { [string]$signerMetadata.signerSubject } else { $null }
        signerTrustPolicy = $signerTrustPolicy
    }
}

$checksumPath = Join-Path $archiveRootFullPath 'SHA256SUMS.txt'
$checksumLines = $assets | ForEach-Object { "$($_.sha256)  $($_.name)" }
$checksumLines | Set-Content -Path $checksumPath -Encoding UTF8

$manifest = [pscustomobject]@{
    tag = $Tag
    assetCount = @($assets).Count
    assets = @($assets)
}

$manifestPath = Join-Path $archiveRootFullPath 'release-assets.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
