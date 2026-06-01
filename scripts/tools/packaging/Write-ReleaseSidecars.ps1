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

function ConvertTo-SpdxIdSuffix {
    param([Parameter(Mandatory)] [string]$Value)

    $suffix = $Value -replace '[^A-Za-z0-9\.\-]', '-'
    return $suffix.Trim('-')
}

function New-ReleaseSbom {
    param(
        [Parameter(Mandatory)] [string]$ReleaseTag,
        [Parameter(Mandatory)] [object[]]$ReleaseAssets
    )

    $packages = @($ReleaseAssets | ForEach-Object {
        $packageId = "SPDXRef-Package-$(ConvertTo-SpdxIdSuffix -Value ([string]$_.name))"
        [pscustomobject]@{
            name = [string]$_.name
            SPDXID = $packageId
            downloadLocation = 'NOASSERTION'
            filesAnalyzed = $false
            packageFileName = [string]$_.name
            checksums = @(
                [pscustomobject]@{
                    algorithm = 'SHA256'
                    checksumValue = [string]$_.sha256
                }
            )
        }
    })

    $relationships = @($packages | ForEach-Object {
        [pscustomobject]@{
            spdxElementId = 'SPDXRef-DOCUMENT'
            relationshipType = 'DESCRIBES'
            relatedSpdxElement = [string]$_.SPDXID
        }
    })

    [pscustomobject]@{
        spdxVersion = 'SPDX-2.3'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = "wpf-devtools-mcp-$ReleaseTag-release-assets"
        documentComment = 'This is a release asset SPDX inventory for published archive files only; it is not a full package/dependency SBOM and does not enumerate managed assemblies, NuGet dependencies, native binaries, or scripts inside each archive.'
        documentNamespace = "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/$ReleaseTag/release-sbom.spdx.json"
        creationInfo = [pscustomobject]@{
            creators = @('Tool: WPF DevTools MCP release sidecar writer')
            created = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture)
        }
        packages = $packages
        relationships = $relationships
    }
}

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function New-SpdxChecksum {
    param([Parameter(Mandatory)] [string]$Hash)

    [pscustomobject]@{
        algorithm = 'SHA256'
        checksumValue = $Hash
    }
}

function Get-Sha256HexForStream {
    param([Parameter(Mandatory)] [System.IO.Stream]$Stream)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($sha256.ComputeHash($Stream) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
}

function New-SpdxFile {
    param(
        [Parameter(Mandatory)] [string]$FileName,
        [Parameter(Mandatory)] [string]$Sha256,
        [Parameter(Mandatory)] [string[]]$FileTypes
    )

    [pscustomobject]@{
        fileName = $FileName.Replace('\', '/')
        SPDXID = "SPDXRef-File-$(ConvertTo-SpdxIdSuffix -Value $FileName)"
        checksums = @(New-SpdxChecksum -Hash $Sha256)
        fileTypes = $FileTypes
    }
}

function Get-SpdxFileTypes {
    param([Parameter(Mandatory)] [string]$Path)

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    switch ($extension) {
        '.dll' { return @('BINARY', 'APPLICATION') }
        '.exe' { return @('BINARY', 'APPLICATION') }
        '.ps1' { return @('SOURCE') }
        '.psm1' { return @('SOURCE') }
        '.bat' { return @('SOURCE') }
        '.cmd' { return @('SOURCE') }
        '.json' { return @('TEXT') }
        default { return @('OTHER') }
    }
}

function Get-ReleaseArchiveContentFiles {
    param([Parameter(Mandatory)] [System.IO.FileInfo[]]$Archives)

    foreach ($archiveFile in $Archives) {
        try {
            $archive = [System.IO.Compression.ZipFile]::OpenRead($archiveFile.FullName)
        }
        catch {
            if (Test-InstallerTestModeEnabled) {
                New-SpdxFile -FileName $archiveFile.Name -Sha256 (Get-Sha256FileHashHex -Path $archiveFile.FullName) -FileTypes @('BINARY', 'ARCHIVE')
                continue
            }

            throw
        }

        try {
            foreach ($entry in $archive.Entries | Sort-Object FullName) {
                if ([string]::IsNullOrWhiteSpace($entry.Name)) {
                    continue
                }

                $stream = $entry.Open()
                try {
                    $fileName = "$($archiveFile.Name)!/$($entry.FullName.Replace('\', '/'))"
                    New-SpdxFile -FileName $fileName -Sha256 (Get-Sha256HexForStream -Stream $stream) -FileTypes (Get-SpdxFileTypes -Path $entry.FullName)
                }
                finally {
                    $stream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
}

function Get-RepositoryScriptFiles {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $scriptRoot = Join-Path $RepositoryRoot 'scripts'
    if (-not (Test-Path -LiteralPath $scriptRoot -PathType Container)) {
        return @()
    }

    Get-ChildItem -LiteralPath $scriptRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.ps1', '.psm1', '.bat', '.cmd', '.json') } |
        Sort-Object FullName |
        ForEach-Object {
            $relativeName = $_.FullName.Substring($RepositoryRoot.TrimEnd('\').Length + 1).Replace('\', '/')
            New-SpdxFile -FileName $relativeName -Sha256 (Get-Sha256FileHashHex -Path $_.FullName) -FileTypes (Get-SpdxFileTypes -Path $_.FullName)
        }
}

function Get-NuGetDependencyPackages {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $dependencies = [ordered]@{}
    Get-ChildItem -LiteralPath $RepositoryRoot -Filter 'packages.lock.json' -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|tmp)\\' } |
        Sort-Object FullName |
        ForEach-Object {
            $lock = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            $dependencyGroups = $lock.PSObject.Properties['dependencies'].Value
            if ($null -eq $dependencyGroups) {
                return
            }

            foreach ($group in $dependencyGroups.PSObject.Properties) {
                foreach ($dependency in $group.Value.PSObject.Properties) {
                    $value = $dependency.Value
                    $resolved = [string]$value.PSObject.Properties['resolved'].Value
                    if ([string]::IsNullOrWhiteSpace($resolved)) {
                        continue
                    }

                    $name = [string]$dependency.Name
                    $key = "$name@$resolved"
                    if ($dependencies.Contains($key)) {
                        continue
                    }

                    $dependencies[$key] = [pscustomobject]@{
                        name = $name
                        SPDXID = "SPDXRef-NuGet-$(ConvertTo-SpdxIdSuffix -Value $key)"
                        versionInfo = $resolved
                        downloadLocation = 'NOASSERTION'
                        filesAnalyzed = $false
                        supplier = 'NOASSERTION'
                    }
                }
            }
        }

    @($dependencies.Values)
}

function New-PackageSbom {
    param(
        [Parameter(Mandatory)] [string]$ReleaseTag,
        [Parameter(Mandatory)] [System.IO.FileInfo[]]$Archives
    )

    $repositoryRoot = Get-RepositoryRoot
    $packages = @(Get-NuGetDependencyPackages -RepositoryRoot $repositoryRoot)
    $files = @(
        Get-ReleaseArchiveContentFiles -Archives $Archives
        Get-RepositoryScriptFiles -RepositoryRoot $repositoryRoot
    )
    $relationships = @(
        $packages | ForEach-Object {
            [pscustomobject]@{
                spdxElementId = 'SPDXRef-DOCUMENT'
                relationshipType = 'DESCRIBES'
                relatedSpdxElement = [string]$_.SPDXID
            }
        }
        $files | ForEach-Object {
            [pscustomobject]@{
                spdxElementId = 'SPDXRef-DOCUMENT'
                relationshipType = 'CONTAINS'
                relatedSpdxElement = [string]$_.SPDXID
            }
        }
    )

    [pscustomobject]@{
        spdxVersion = 'SPDX-2.3'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = "wpf-devtools-mcp-$ReleaseTag-package-dependencies"
        documentComment = 'This is a full package/dependency SBOM for release package review. It enumerates NuGet lock-file dependencies, release ZIP contents, managed assemblies, native bootstrapper binaries, PowerShell scripts, installer payload files, and SHA-256 checksums for executable/script payloads.'
        documentNamespace = "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/$ReleaseTag/package-sbom.spdx.json"
        creationInfo = [pscustomobject]@{
            creators = @('Tool: WPF DevTools MCP package SBOM writer')
            created = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture)
        }
        packages = $packages
        files = $files
        relationships = $relationships
    }
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

$sbomPath = Join-Path $archiveRootFullPath 'release-sbom.spdx.json'
New-ReleaseSbom -ReleaseTag $Tag -ReleaseAssets @($assets) |
    ConvertTo-Json -Depth 8 |
    Set-Content -Path $sbomPath -Encoding UTF8

$packageSbomPath = Join-Path $archiveRootFullPath 'package-sbom.spdx.json'
New-PackageSbom -ReleaseTag $Tag -Archives ([System.IO.FileInfo[]]$archives) |
    ConvertTo-Json -Depth 12 |
    Set-Content -Path $packageSbomPath -Encoding UTF8

$sbomFile = Get-Item -LiteralPath $sbomPath
$packageSbomFile = Get-Item -LiteralPath $packageSbomPath
$sidecars = @(
    [pscustomobject]@{
        name = $sbomFile.Name
        role = 'release-asset-spdx-sbom'
        sizeBytes = $sbomFile.Length
        sha256 = Get-Sha256FileHashHex -Path $sbomFile.FullName
    },
    [pscustomobject]@{
        name = $packageSbomFile.Name
        role = 'package-dependency-spdx-sbom'
        sizeBytes = $packageSbomFile.Length
        sha256 = Get-Sha256FileHashHex -Path $packageSbomFile.FullName
    }
)

$manifest = [pscustomobject]@{
    tag = $Tag
    assetCount = @($assets).Count
    assets = @($assets)
    sidecarCount = @($sidecars).Count
    sidecars = @($sidecars)
}

$manifestPath = Join-Path $archiveRootFullPath 'release-assets.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
