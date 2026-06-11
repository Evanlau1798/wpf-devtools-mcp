function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $null
    }

    $backupPath = Assert-InstallerLocalPathTrusted -Path "$resolvedPath.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
    Copy-Item -LiteralPath $resolvedPath -Destination $backupPath -Force
    return $backupPath
}

function Get-ConfigJsonParseFailureMessage {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ErrorMessage
    )

    return "Failed to parse JSON config file '$Path'. Fix the malformed JSON and retry. The installer did not modify the file or update registration state. Parser error: $ErrorMessage"
}

function Get-ExistingConfigMap {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    $map = [ordered]@{}
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $map
    }

    $raw = Get-Content -LiteralPath $resolvedPath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    try {
        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw (Get-ConfigJsonParseFailureMessage -Path $resolvedPath -ErrorMessage $_.Exception.Message)
    }

    foreach ($property in $parsed.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }

    return $map
}

function Get-ConfigCollectionMap {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$CollectionName
    )

    $servers = [ordered]@{}
    if ($Root.Contains($CollectionName) -and $null -ne $Root[$CollectionName]) {
        foreach ($property in $Root[$CollectionName].PSObject.Properties) {
            $servers[$property.Name] = $property.Value
        }
    }

    return $servers
}

function Set-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    $directory = Split-Path -Parent $resolvedConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        Assert-InstallerLocalPathTrusted -Path $directory | Out-Null
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    $servers['wpf-devtools'] = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $root[$CollectionName] = $servers
    $backupPath = Backup-ConfigFile -Path $resolvedConfigPath
    Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
    Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content ($root | ConvertTo-Json -Depth 10)

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $resolvedConfigPath
        backupPath = $backupPath
        applied = $true
    }
}

function Remove-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $resolvedConfigPath
            backupPath = $null
            applied = $false
        }
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $resolvedConfigPath
            backupPath = $null
            applied = $false
        }
    }

    [void]$servers.Remove('wpf-devtools')
    $backupPath = Backup-ConfigFile -Path $resolvedConfigPath

    if ($servers.Count -gt 0) {
        $root[$CollectionName] = $servers
    }
    else {
        [void]$root.Remove($CollectionName)
    }

    if ($root.Count -eq 0) {
        Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content '{}'
    }
    else {
        Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content ($root | ConvertTo-Json -Depth 10)
    }

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $resolvedConfigPath
        backupPath = $backupPath
        applied = $true
    }
}

function Test-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $false
    }

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    return $servers.Contains('wpf-devtools')
}

function Resolve-VsCodeConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VsCodeConfigPath)) { return $VsCodeConfigPath }
    return (Join-Path $env:APPDATA 'Code\User\mcp.json')
}

function Resolve-VisualStudioConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VisualStudioConfigPath)) { return $VisualStudioConfigPath }
    return (Join-Path $env:USERPROFILE '.mcp.json')
}

function Resolve-ClaudeDesktopConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($ClaudeDesktopConfigPath)) { return $ClaudeDesktopConfigPath }
    return (Join-Path $env:APPDATA 'Claude\claude_desktop_config.json')
}
