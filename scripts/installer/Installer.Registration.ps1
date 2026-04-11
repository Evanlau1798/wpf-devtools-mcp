function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    $backupPath = "$Path.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Copy-Item -Path $Path -Destination $backupPath -Force
    return $backupPath
}

function Get-ExistingConfigMap {
    param([Parameter(Mandatory)] [string]$Path)

    $map = [ordered]@{}
    if (-not (Test-Path $Path)) {
        return $map
    }

    $raw = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    $parsed = $raw | ConvertFrom-Json
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

    $directory = Split-Path -Parent $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    $servers['wpf-devtools'] = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $root[$CollectionName] = $servers
    $backupPath = Backup-ConfigFile -Path $ConfigPath
    $root | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $ConfigPath
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

    if (-not (Test-Path $ConfigPath)) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $ConfigPath
            backupPath = $null
            applied = $false
        }
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $ConfigPath
            backupPath = $null
            applied = $false
        }
    }

    [void]$servers.Remove('wpf-devtools')
    $backupPath = Backup-ConfigFile -Path $ConfigPath

    if ($servers.Count -gt 0) {
        $root[$CollectionName] = $servers
    }
    else {
        [void]$root.Remove($CollectionName)
    }

    if ($root.Count -eq 0) {
        '{}' | Set-Content -Path $ConfigPath -Encoding UTF8
    }
    else {
        $root | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8
    }

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $ConfigPath
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

    if (-not (Test-Path $ConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
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

function Resolve-CursorProjectRoot {
    if (-not [string]::IsNullOrWhiteSpace($CursorProjectRoot)) {
        return (Resolve-AbsoluteDirectory -Path $CursorProjectRoot)
    }

    return (Resolve-AbsoluteDirectory -Path (Get-Location).Path)
}

function Resolve-CursorGlobalConfigPath {
    if ($CursorMode -eq 'project') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path $env:USERPROFILE '.cursor\mcp.json')
}

function Resolve-CursorProjectConfigPath {
    if ($CursorMode -eq 'global') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path (Resolve-CursorProjectRoot) '.cursor\mcp.json')
}

function Resolve-CursorRegistrationProfile {
    param(
        [string]$SelectedClient,
        [switch]$PromptIfNeeded,
        $RegistrationRecord
    )

    $selectedMode = switch ($SelectedClient) {
        'cursor-project' { 'project' }
        'cursor-global' { 'global' }
        default { $null }
    }

    $recordedMode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'RegistrationMode')
    if ($recordedMode -like 'cursor-*') {
        $recordedMode = $recordedMode.Substring(7)
    }

    $recordedTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')

    $resolvedMode = if (-not [string]::IsNullOrWhiteSpace($CursorMode)) {
        [string]$CursorMode
    }
    elseif (-not [string]::IsNullOrWhiteSpace($selectedMode)) {
        [string]$selectedMode
    }
    elseif (-not [string]::IsNullOrWhiteSpace($recordedMode)) {
        [string]$recordedMode
    }
    elseif ($PromptIfNeeded -and -not $NonInteractive -and -not $OutputJson) {
        Read-ValidatedChoice -Prompt 'Cursor mode (global/project)' -DefaultValue 'global' -AllowedValues @('global', 'project')
    }
    else {
        'global'
    }

    if ($resolvedMode -eq 'project') {
        $projectRoot = Resolve-CursorProjectRoot
        return [ordered]@{
            Mode = 'project'
            ConfigPath = if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) { $CursorConfigPath } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Join-Path $projectRoot '.cursor\mcp.json' }
            Target = $projectRoot
        }
    }

    $globalConfigPath = if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) { $CursorConfigPath } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Join-Path $env:USERPROFILE '.cursor\mcp.json' }
    return [ordered]@{
        Mode = 'global'
        ConfigPath = $globalConfigPath
        Target = $globalConfigPath
    }
}

function Get-CursorVerificationConfigPaths {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    $paths = New-Object System.Collections.Generic.List[string]
    $recordTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    if (-not [string]::IsNullOrWhiteSpace($recordTarget)) {
        $paths.Add($recordTarget)
        return @($paths)
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorMode) -or -not [string]::IsNullOrWhiteSpace($CursorConfigPath) -or $SelectedClient -like 'cursor-*') {
        $profile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
        if (-not [string]::IsNullOrWhiteSpace([string]$profile.ConfigPath)) {
            $paths.Add([string]$profile.ConfigPath)
            return @($paths)
        }
    }

    foreach ($candidatePath in @(
            (Resolve-CursorProjectConfigPath)
            (Resolve-CursorGlobalConfigPath)
        )) {
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        $alreadyAdded = $false
        foreach ($existingPath in $paths) {
            if (Test-InstallerPathEqualsCore -Left $existingPath -Right $candidatePath) {
                $alreadyAdded = $true
                break
            }
        }

        if (-not $alreadyAdded) {
            $paths.Add($candidatePath)
        }
    }

    return @($paths)
}

function Invoke-RegistrationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        throw "$Command is not installed. Cannot register $ClientName automatically."
    }

    & $Command @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed for $ClientName with exit code $LASTEXITCODE."
    }

    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $Command
        backupPath = $null
        applied = $true
    }
}

function Invoke-OptionalRemovalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        return [ordered]@{
            client = $ClientName
            mode = 'cli'
            target = $Command
            backupPath = $null
            applied = $false
        }
    }

    & $Command @Arguments | Out-Null
    $succeeded = ($LASTEXITCODE -eq 0)
    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $Command
        backupPath = $null
        applied = $succeeded
    }
}

function Invoke-DocsHomepage {
    $uri = 'https://wpf-mcptools.evanlau1798.com'
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND)) {
        & $env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND $uri | Out-Null
        return
    }

    Start-Process $uri | Out-Null
}

function Invoke-ClientRegistration {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$InstallBase
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    switch ($clientBaseId) {
        'claude-code' {
            return @(Invoke-RegistrationCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $clientBaseId)
        }
        'codex' {
            return @(Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $clientBaseId)
        }
        'cursor' {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -PromptIfNeeded
            $registration = Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath ([string]$cursorProfile.ConfigPath) -InstalledExecutable $InstalledExecutable
            $registration['mode'] = "cursor-$([string]$cursorProfile.Mode)"
            $registration['target'] = [string]$cursorProfile.ConfigPath
            return @($registration)
        }
        'vscode' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'visual-studio' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'claude-desktop' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'other' {
            return @([ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = (Join-Path $InstallBase 'client-registration\other.mcpServers.json')
                    backupPath = $null
                    applied = $true
                })
        }
    }
}

function Invoke-ClientUnregistration {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    switch ($clientBaseId) {
        'claude-code' {
            return @(Invoke-OptionalRemovalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'codex' {
            return @(Invoke-OptionalRemovalCommand -Command 'codex' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'cursor' {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -PromptIfNeeded -RegistrationRecord $RegistrationRecord
            $registration = Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath ([string]$cursorProfile.ConfigPath)
            $registration['mode'] = "cursor-$([string]$cursorProfile.Mode)"
            $registration['target'] = [string]$cursorProfile.ConfigPath
            return @($registration)
        }
        'vscode' {
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath))
        }
        'visual-studio' {
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath))
        }
        'claude-desktop' {
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath))
        }
        'other' {
            return @([ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = $null
                    backupPath = $null
                    applied = $true
                })
        }
    }
}

function New-ClientRegistrationArtifacts {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $registrationDir = Join-Path $InstallBase 'client-registration'
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null

    $serverNode = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'vscode.json') -Encoding UTF8
    ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'visual-studio.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'cursor.global.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'cursor.project.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'claude-desktop.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'other.mcpServers.json') -Encoding UTF8

    @"
claude mcp add --transport stdio wpf-devtools -- "$InstalledExecutable"

claude mcp remove wpf-devtools
"@ | Set-Content -Path (Join-Path $registrationDir 'claude-code.txt') -Encoding UTF8

    @"
codex mcp add wpf-devtools -- "$InstalledExecutable"

codex mcp remove wpf-devtools
"@ | Set-Content -Path (Join-Path $registrationDir 'codex.txt') -Encoding UTF8
}
