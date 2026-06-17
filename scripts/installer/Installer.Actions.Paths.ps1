#>
if (-not (Get-Command Write-InstallerUtf8NoBomFile -ErrorAction SilentlyContinue)) {
    $encodingHelperPath = Join-Path $PSScriptRoot 'Installer.Encoding.ps1'
    if (Test-Path -LiteralPath $encodingHelperPath) {
        . $encodingHelperPath
    }
}

if (-not (Get-Command Resolve-AbsolutePath -ErrorAction SilentlyContinue)) {
    function Resolve-AbsolutePath {
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

        $resolvedPath = Resolve-AbsolutePath -Path $Path
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

            if ($RejectHardLinks -and -not $item.PSIsContainer -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
                $hardLinkCount = Get-InstallerHardLinkCount -Path $currentPath
                if ($hardLinkCount -gt 1) {
                    throw "Installer path '$resolvedPath' is blocked because '$currentPath' has multiple hard links."
                }
            }
        }

        return $resolvedPath
    }
}

function Test-InstallerTransientFileSystemError {
    param([System.Exception]$Exception)

    $candidate = $Exception
    while ($null -ne $candidate) {
        if ($candidate -is [System.IO.IOException] -or $candidate -is [System.UnauthorizedAccessException]) {
            return $true
        }

        $candidate = $candidate.InnerException
    }

    return $false
}

function Get-InstallerFileSystemRecoveryMessage {
    param(
        [Parameter(Mandatory)] [string]$Operation,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [System.Exception]$Exception
    )

    $message = "$Operation failed for '$Path'. $($Exception.Message)"
    if (Test-InstallerTransientFileSystemError -Exception $Exception) {
        $message += " Close any running WPF target applications, MCP server processes, and terminals or Explorer windows using this install directory, then retry with -Action full-uninstall."
    }

    return $message
}

function Move-InstallerPathWithRetry {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $pathsTrusted = $false
        $resolvedSourcePath = $null
        $resolvedDestinationPath = $null
        try {
            $resolvedSourcePath = Assert-InstallerLocalPathTrusted -Path $SourcePath
            $resolvedDestinationPath = Assert-InstallerLocalPathTrusted -Path $DestinationPath
            $pathsTrusted = $true
            $sourceExists = Test-Path -LiteralPath $resolvedSourcePath
            $destinationExists = Test-Path -LiteralPath $resolvedDestinationPath

            if (-not $sourceExists -and $destinationExists) {
                return
            }

            if ($sourceExists -and $destinationExists) {
                Assert-InstallerLocalPathTrusted -Path $resolvedDestinationPath | Out-Null
                Remove-Item -LiteralPath $resolvedDestinationPath -Recurse -Force
            }

            Assert-InstallerLocalPathTrusted -Path $resolvedSourcePath | Out-Null
            Assert-InstallerLocalPathTrusted -Path $resolvedDestinationPath | Out-Null
            Move-Item -LiteralPath $resolvedSourcePath -Destination $resolvedDestinationPath -Force
            return
        }
        catch {
            if ($pathsTrusted -and -not (Test-Path -LiteralPath $resolvedSourcePath) -and (Test-Path -LiteralPath $resolvedDestinationPath)) {
                return
            }

            if (-not (Test-InstallerTransientFileSystemError -Exception $_.Exception) -or $attempt -ge 19) {
                throw (Get-InstallerFileSystemRecoveryMessage -Operation 'Move installer path' -Path $SourcePath -Exception $_.Exception)
            }

            Start-Sleep -Milliseconds ([Math]::Min(150 * ($attempt + 1), 2000))
        }
    }
}
