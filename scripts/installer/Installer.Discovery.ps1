if (-not (Get-Command Resolve-InstallerDiscoveryAbsolutePath -ErrorAction SilentlyContinue)) {
    function Resolve-InstallerDiscoveryAbsolutePath {
        param([Parameter(Mandatory)] [string]$Path)

        if ([System.IO.Path]::IsPathRooted($Path)) {
            return [System.IO.Path]::GetFullPath($Path)
        }

        return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
    }
}

if (-not (Get-Command Test-InstallerUncOrDevicePath -ErrorAction SilentlyContinue)) {
    function Test-InstallerUncOrDevicePath {
        param([Parameter(Mandatory)] [string]$Path)

        return $Path.StartsWith('\\', [System.StringComparison]::Ordinal) -or
            $Path.StartsWith('\\?\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $Path.StartsWith('\\.\', [System.StringComparison]::OrdinalIgnoreCase)
    }
}

if (-not (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue)) {
    function Assert-InstallerLocalPathTrusted {
        param(
            [Parameter(Mandatory)] [string]$Path,
            [switch]$RejectHardLinks
        )

        $resolvedPath = Resolve-InstallerDiscoveryAbsolutePath -Path $Path
        if (Test-InstallerUncOrDevicePath -Path $resolvedPath) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $root = [System.IO.Path]::GetPathRoot($resolvedPath)
        if ([string]::IsNullOrWhiteSpace($root)) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require an absolute local path."
        }

        try {
            $drive = [System.IO.DriveInfo]::new($root)
            if ($drive.DriveType -eq [System.IO.DriveType]::Network) {
                throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
            }
        }
        catch [System.ArgumentException] {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $relativePath = $resolvedPath.Substring($root.Length).Trim('\', '/')
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return $resolvedPath
        }

        $currentPath = $root
        foreach ($segment in $relativePath -split '[\\/]') {
            if ([string]::IsNullOrWhiteSpace($segment)) {
                continue
            }

            $currentPath = Join-Path $currentPath $segment
            if (-not (Test-Path -LiteralPath $currentPath)) {
                break
            }

            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Installer path '$resolvedPath' is blocked because '$currentPath' is a reparse point."
            }
        }

        if ($RejectHardLinks -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
            $linkCount = Get-InstallerHardLinkCount -Path $resolvedPath
            if ($linkCount -gt 1) {
                throw "Installer path '$resolvedPath' is blocked because hardlinked installer write targets are not trusted."
            }
        }

        return $resolvedPath
    }
}

function New-DetectedInstallerRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [string]$RegistrationMode,
        [string]$RegistrationTarget,
        [string]$InstalledExecutable,
        [string]$InstallRoot,
        [string]$Architecture,
        [bool]$InstallerOwned,
        [string]$EvidenceSource,
        [string]$ResolvedVersion,
        [string]$LastVerifiedUtc
    )

    return [ordered]@{
        ClientId = $ClientId
        RegistrationMode = $RegistrationMode
        RegistrationTarget = $RegistrationTarget
        InstalledExecutable = $InstalledExecutable
        InstallRoot = $InstallRoot
        Architecture = $Architecture
        InstallerOwned = $InstallerOwned
        EvidenceSource = $EvidenceSource
        ResolvedVersion = $ResolvedVersion
        LastVerifiedUtc = $LastVerifiedUtc
    }
}

function Get-WpfDevToolsExecutableFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    foreach ($rawLine in ($Text -split "`r?`n")) {
        $line = [string]$rawLine
        if ($line -notmatch '^\s*(?:[-*]\s*)?["'']?wpf-devtools["'']?(?:\s|:|=|$)') {
            continue
        }

        $match = [regex]::Match($line, '(?<path>[A-Za-z]:\\[^`"\r\n]*wpf-devtools-(x64|x86|arm64)\.exe)', 'IgnoreCase')
        if ($match.Success) {
            return [string]$match.Groups['path'].Value
        }
    }

    return $null
}

function Normalize-InstallerPathCore {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $trimmed = [string]$PathValue.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    $normalizedSeparators = $trimmed.Replace('/', '\')
    try {
        return [System.IO.Path]::GetFullPath($normalizedSeparators)
    }
    catch {
        return $normalizedSeparators
    }
}

function Test-InstallerPathEqualsCore {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalizedLeft = Normalize-InstallerPathCore -PathValue $Left
    $normalizedRight = Normalize-InstallerPathCore -PathValue $Right
    if ([string]::IsNullOrWhiteSpace($normalizedLeft) -or [string]::IsNullOrWhiteSpace($normalizedRight)) {
        return $false
    }

    return [string]::Equals($normalizedLeft, $normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
}

function Resolve-InstallerOwnershipFromExecutable {
    param([string]$InstalledExecutable)

    $result = [ordered]@{
        InstallerOwned = $false
        InstalledExecutable = $InstalledExecutable
        InstallBase = $null
        InstallRoot = $null
        Architecture = $null
        ResolvedVersion = $null
    }

    if ([string]::IsNullOrWhiteSpace($InstalledExecutable)) {
        return $result
    }

    try {
        $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $InstalledExecutable
    }
    catch {
        return $result
    }

    $architectureMatch = [regex]::Match($trustedInstalledExecutable, 'wpf-devtools-(x64|x86|arm64)\.exe', 'IgnoreCase')
    if ($architectureMatch.Success) {
        $result.Architecture = [string]$architectureMatch.Groups[1].Value.ToLowerInvariant()
    }

    $binDirectory = Split-Path -Parent $trustedInstalledExecutable
    if ([string]::IsNullOrWhiteSpace($binDirectory)) {
        return $result
    }

    $currentDirectory = Split-Path -Parent $binDirectory
    if ([string]::IsNullOrWhiteSpace($currentDirectory)) {
        return $result
    }

    $installBase = Split-Path -Parent $currentDirectory
    if ([string]::IsNullOrWhiteSpace($installBase)) {
        return $result
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    }
    catch {
        return $result
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $result
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifestExecutable = [string]$manifest.executable
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and (Test-InstallerPathEqualsCore -Left $manifestExecutable -Right $trustedInstalledExecutable)) {
            $result.InstallerOwned = $true
            $result.InstallBase = $installBase
            $result.InstallRoot = [string]$manifest.installRoot
            if ([string]::IsNullOrWhiteSpace($result.InstallRoot)) {
                $result.InstallRoot = Split-Path -Parent $installBase
            }

            if ([string]::IsNullOrWhiteSpace([string]$result.Architecture)) {
                $result.Architecture = [string]$manifest.architecture
            }

            $result.ResolvedVersion = [string]$manifest.version
        }
    }
    catch {
    }

    return $result
}

function Merge-DetectedInstallerRegistration {
    param(
        [Parameter(Mandatory)] $Primary,
        [Parameter(Mandatory)] $Secondary
    )

    $merged = [ordered]@{}
    $preferSecondaryFields = @(
        'RegistrationMode',
        'RegistrationTarget',
        'InstalledExecutable',
        'InstallRoot',
        'Architecture',
        'EvidenceSource',
        'ResolvedVersion'
    )
    foreach ($field in @(
            'ClientId',
            'RegistrationMode',
            'RegistrationTarget',
            'InstalledExecutable',
            'InstallRoot',
            'Architecture',
            'EvidenceSource',
            'ResolvedVersion',
            'LastVerifiedUtc')) {
        $primaryValue = $Primary[$field]
        $secondaryValue = $Secondary[$field]
        $preferSecondary = $preferSecondaryFields -contains $field
        $merged[$field] = if ($preferSecondary) {
            if (-not [string]::IsNullOrWhiteSpace([string]$secondaryValue)) { $secondaryValue } else { $primaryValue }
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace([string]$primaryValue)) { $primaryValue } else { $secondaryValue }
        }
    }

    $merged['InstallerOwned'] = ([bool]$Primary.InstallerOwned -or [bool]$Secondary.InstallerOwned)

    $primaryEvidenceSource = [string]$Primary.EvidenceSource
    $secondaryEvidenceSource = [string]$Secondary.EvidenceSource
    $primaryTarget = [string]$Primary.RegistrationTarget
    $secondaryTarget = [string]$Secondary.RegistrationTarget
    $primaryClientBaseId = Resolve-ClientBaseId -ClientId ([string]$Primary.ClientId)
    $collectionName = switch ($primaryClientBaseId) {
        'vscode' { 'servers' }
        'visual-studio' { 'servers' }
        'claude-desktop' { 'mcpServers' }
        'cursor' { 'mcpServers' }
        default { $null }
    }

    if ([string]::Equals($primaryEvidenceSource, 'state', [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($secondaryEvidenceSource, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace($collectionName) -and
        -not [string]::IsNullOrWhiteSpace($primaryTarget) -and
        -not [string]::IsNullOrWhiteSpace($secondaryTarget)) {
        $primaryHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $primaryTarget
        $secondaryHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $secondaryTarget
        if ($primaryHasRegistration -and $secondaryHasRegistration -and -not (Test-InstallerPathEqualsCore -Left $primaryTarget -Right $secondaryTarget)) {
            $merged['RegistrationTarget'] = $primaryTarget
            $merged['RegistrationMode'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.RegistrationMode)) { [string]$Primary.RegistrationMode } else { [string]$merged['RegistrationMode'] }
            $merged['InstalledExecutable'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.InstalledExecutable)) { [string]$Primary.InstalledExecutable } else { [string]$merged['InstalledExecutable'] }
            $merged['InstallRoot'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.InstallRoot)) { [string]$Primary.InstallRoot } else { [string]$merged['InstallRoot'] }
            $merged['Architecture'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.Architecture)) { [string]$Primary.Architecture } else { [string]$merged['Architecture'] }
            $merged['ResolvedVersion'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.ResolvedVersion)) { [string]$Primary.ResolvedVersion } else { [string]$merged['ResolvedVersion'] }
        }
    }

    return $merged
}

function Get-StateRegistrationEvidence {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if (-not $State.registrations.Contains($ClientId)) {
        return $null
    }

    $registration = $State.registrations[$ClientId]
    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable ([string]$registration.installedExecutable)
    return (New-DetectedInstallerRegistration `
            -ClientId (Resolve-ClientStateKey -ClientId $ClientId -RegistrationMode ([string]$registration.mode)) `
            -RegistrationMode ([string]$registration.mode) `
            -RegistrationTarget ([string]$registration.target) `
            -InstalledExecutable ([string]$registration.installedExecutable) `
            -InstallRoot ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.installRoot)) { [string]$registration.installRoot } else { [string]$ownership.InstallRoot })) `
            -Architecture ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.architecture)) { [string]$registration.architecture } else { [string]$ownership.Architecture })) `
            -InstallerOwned ([bool]$ownership.InstallerOwned) `
            -EvidenceSource 'state' `
            -ResolvedVersion ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.resolvedVersion)) { [string]$registration.resolvedVersion } else { [string]$ownership.ResolvedVersion })) `
            -LastVerifiedUtc ([string]$registration.lastVerifiedUtc))
}

function Get-StateRegistrationEvidencesForClient {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if ($ClientId -ne 'cursor') {
        $stateEvidence = Get-StateRegistrationEvidence -State $State -ClientId $ClientId
        return @($(if ($null -ne $stateEvidence) { $stateEvidence }))
    }

    return @($State.registrations.Keys |
            Where-Object { $_ -eq 'cursor' -or $_ -like 'cursor-*' } |
            Sort-Object |
            ForEach-Object {
                Get-StateRegistrationEvidence -State $State -ClientId ([string]$_)
            } |
            Where-Object { $null -ne $_ })
}


if ($null -eq (Get-Command Get-DetectedInstallerRegistrations -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Installer.Discovery.Detection.ps1')
}
