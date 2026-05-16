if (-not (Get-Command Write-InstallerUtf8NoBomFile -ErrorAction SilentlyContinue)) {
    $encodingHelperPath = Join-Path $PSScriptRoot 'Installer.Encoding.ps1'
    if (Test-Path -LiteralPath $encodingHelperPath) {
        . $encodingHelperPath
    }
}

function Resolve-InstallerStateAbsolutePath {
    param([Parameter(Mandatory)] [string]$Path)

    $resolver = Get-Command Resolve-AbsolutePath -ErrorAction SilentlyContinue
    if ($null -ne $resolver) {
        return (Resolve-AbsolutePath -Path $Path)
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
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

        $resolvedPath = Resolve-InstallerStateAbsolutePath -Path $Path
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

function Resolve-InstallerStatePath {
    param([switch]$CreateRoot)

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'installer-state.json')
}

function Get-EmptyInstallerState {
    return [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }
}

function Move-CorruptInstallerStateFile {
    param([Parameter(Mandatory)] [string]$StatePath)

    $resolvedStatePath = Assert-InstallerLocalPathTrusted -Path $StatePath
    if (-not (Test-Path -LiteralPath $resolvedStatePath)) {
        return $null
    }

    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
    $corruptPath = Assert-InstallerLocalPathTrusted -Path "$resolvedStatePath.corrupt-$timestamp"
    try {
        Assert-InstallerLocalPathTrusted -Path $resolvedStatePath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $corruptPath | Out-Null
        Move-Item -LiteralPath $resolvedStatePath -Destination $corruptPath -Force
        return $corruptPath
    }
    catch {
        return $null
    }
}

function Get-InstallerState {
    $statePath = Resolve-InstallerStatePath
    $state = Get-EmptyInstallerState

    if (-not (Test-Path -LiteralPath $statePath)) {
        return $state
    }

    $raw = Get-Content -LiteralPath $statePath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $state
    }

    try {
        $parsed = $raw | ConvertFrom-Json
    }
    catch {
        Move-CorruptInstallerStateFile -StatePath $statePath | Out-Null
        return $state
    }

    $state.lastInstallRoot = [string]$parsed.lastInstallRoot

    if ($null -ne $parsed.architectures) {
        foreach ($property in $parsed.architectures.PSObject.Properties) {
            $state.architectures[$property.Name] = [ordered]@{
                version = [string]$property.Value.version
                executable = [string]$property.Value.executable
                installRoot = [string]$property.Value.installRoot
            }
        }
    }

    if ($null -ne $parsed.registrations) {
        foreach ($property in $parsed.registrations.PSObject.Properties) {
            $state.registrations[$property.Name] = [ordered]@{
                architecture = [string]$property.Value.architecture
                installRoot = [string]$property.Value.installRoot
                mode = [string]$property.Value.mode
                target = [string]$property.Value.target
                resolvedVersion = [string]$property.Value.resolvedVersion
                installedExecutable = [string]$property.Value.installedExecutable
                lastVerifiedUtc = [string]$property.Value.lastVerifiedUtc
            }
        }
    }

    return $state
}

function Save-InstallerState {
    param([Parameter(Mandatory)] $State)

    $statePath = Resolve-InstallerStatePath -CreateRoot
    $tempStatePath = "$statePath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $tempStatePath -Content ($State | ConvertTo-Json -Depth 10)
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $statePath -RejectHardLinks | Out-Null
        Move-Item -LiteralPath $tempStatePath -Destination $statePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempStatePath) {
            Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
            Remove-Item -LiteralPath $tempStatePath -Force
        }
    }
    return $statePath
}

function Test-InstallerStateHasData {
    param([Parameter(Mandatory)] $State)

    return (
        -not [string]::IsNullOrWhiteSpace([string]$State.lastInstallRoot) -or
        $State.architectures.Count -gt 0 -or
        $State.registrations.Count -gt 0
    )
}


if ($null -eq (Get-Command Resolve-PreferredInstallRoot -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Installer.State.Installation.ps1')
}
