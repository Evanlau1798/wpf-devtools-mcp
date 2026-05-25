[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$InstallRoot,
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture
)

$ErrorActionPreference = 'Stop'
$script:Failures = New-Object System.Collections.Generic.List[string]

function Add-ResidueFailure {
    param([Parameter(Mandatory = $true)] [string]$Message)

    [void]$script:Failures.Add($Message)
}

function Get-DisplayPath {
    param([Parameter(Mandatory = $true)] [string]$Path)

    try {
        $root = [System.IO.Path]::GetFullPath($InstallRoot)
        $target = [System.IO.Path]::GetFullPath($Path)
        if (-not $root.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $root += [System.IO.Path]::DirectorySeparatorChar
        }

        $rootUri = [System.Uri]::new($root)
        $targetUri = [System.Uri]::new($target)
        return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
    }
    catch {
        return $Path
    }
}

function Test-PathExists {
    param([Parameter(Mandatory = $true)] [string]$Path)

    return -not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)
}

function Test-RollbackOrTemporaryArtifact {
    param([Parameter(Mandatory = $true)] [System.IO.FileSystemInfo]$Item)

    $name = $Item.Name
    return $name -like '*.rollback-*' -or
        $name -like '*.tmp' -or
        $name -like '*.partial' -or
        $name -like '*.download' -or
        ($name -like 'WpfDevTools_Bootstrap_*.json')
}

$resolvedInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$installBase = Join-Path $resolvedInstallRoot $Architecture

if (Test-PathExists -Path $installBase) {
    $installerOwnedPaths = @(
        (Join-Path $installBase 'install-manifest.json'),
        (Join-Path $installBase 'current'),
        (Join-Path $installBase 'client-registration'),
        (Join-Path $installBase "current\bin\wpf-devtools-$Architecture.exe"),
        (Join-Path $installBase 'current\bin\install.ps1'),
        (Join-Path $installBase 'current\bin\installer')
    )

    foreach ($path in $installerOwnedPaths) {
        if (Test-PathExists -Path $path) {
            Add-ResidueFailure "Residual installer-owned path: $(Get-DisplayPath -Path $path)"
        }
    }

    $registrationRoot = Join-Path $installBase 'client-registration'
    if (Test-PathExists -Path $registrationRoot) {
        foreach ($artifact in Get-ChildItem -LiteralPath $registrationRoot -Recurse -Force -ErrorAction SilentlyContinue) {
            Add-ResidueFailure "Residual generated registration artifact: $(Get-DisplayPath -Path $artifact.FullName)"
        }
    }

    foreach ($item in Get-ChildItem -LiteralPath $installBase -Recurse -Force -ErrorAction SilentlyContinue) {
        if (Test-RollbackOrTemporaryArtifact -Item $item) {
            Add-ResidueFailure "Residual rollback or temporary artifact: $(Get-DisplayPath -Path $item.FullName)"
            continue
        }

        if (-not $item.PSIsContainer) {
            Add-ResidueFailure "Residual installer-owned path: $(Get-DisplayPath -Path $item.FullName)"
        }
    }
}

if ($script:Failures.Count -gt 0) {
    foreach ($failure in $script:Failures) {
        Write-Host "ERROR: $failure"
    }

    exit 1
}

Write-Host 'Install residue validation passed.'
