# Release SBOM document helpers for Write-ReleaseSidecars.ps1.

function Test-InstallerTestModeEnabled {
    return [string]::Equals([string]$env:WPFDEVTOOLS_INSTALLER_TEST_MODE, '1', [System.StringComparison]::Ordinal)
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
        Where-Object { $_.Extension -in @('.ps1', '.psm1', '.bat', '.cmd', '.json') -and $_.Name -notmatch '\.test-[0-9a-f]{32}\.ps1$' } |
        Sort-Object FullName |
        ForEach-Object {
            $relativeName = $_.FullName.Substring($RepositoryRoot.TrimEnd('\').Length + 1).Replace('\', '/')
            New-SpdxFile -FileName $relativeName -Sha256 (Get-Sha256FileHashHex -Path $_.FullName) -FileTypes (Get-SpdxFileTypes -Path $_.FullName)
        }
}

function Get-NuGetDependencyPackages {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $dependencies = [ordered]@{}
    $repositoryRootPrefix = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $RepositoryRoot -Filter 'packages.lock.json' -Recurse -File |
        Where-Object {
            $fullName = [System.IO.Path]::GetFullPath($_.FullName)
            if (-not $fullName.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $false
            }

            $relativeSegments = $fullName.Substring($repositoryRootPrefix.Length).Split([char[]]@('\', '/'), [System.StringSplitOptions]::RemoveEmptyEntries)
            return -not @($relativeSegments | Where-Object { $_ -in @('bin', 'obj', 'tmp') }).Count
        } |
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

