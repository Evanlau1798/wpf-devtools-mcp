param(
    [string]$PackagePath,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),
    [string[]]$Clients,
    [string]$ClaudeDesktopConfigPath,
    [string]$CursorConfigPath,
    [string]$VisualStudioConfigPath,
    [switch]$NonInteractive,
    [switch]$DetectOnly,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:IsJsonOutput = $OutputJson.IsPresent
$script:KnownClients = @('claude-code', 'claude-desktop', 'codex', 'cursor', 'visual-studio', 'github-copilot-vscode', 'other')
$script:DocsHomepageUrl = 'https://evanlau1798.github.io/wpf-devtools-mcp/index.html'
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES)) {
    foreach ($entry in ($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES -split '\|\|')) {
        $script:InstallerTestResponses.Enqueue($entry)
    }
}

function Write-SetupMessage {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message)

    if (-not $script:IsJsonOutput) {
        Write-Host $Message
    }
}

function Write-InstallerBanner {
    param([Parameter(Mandatory)] [string]$Subtitle)

    if ($script:IsJsonOutput) {
        return
    }

    Write-SetupMessage ''
    Write-SetupMessage '+------------------------------------------------------------------+'
    Write-SetupMessage '|                       WPF DEVTOOLS MCP                           |'
    Write-SetupMessage '|          [Window] [Grid] [Binding] [VisualTree] [Command]       |'
    Write-SetupMessage '+------------------------------------------------------------------+'
    Write-SetupMessage ("| " + $Subtitle.PadRight(64) + ' |')
    Write-SetupMessage '+------------------------------------------------------------------+'
    Write-SetupMessage ''
}

function Read-InstallerInput {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [string]$DefaultValue
    )

    if ($script:InstallerTestResponses.Count -gt 0) {
        return $script:InstallerTestResponses.Dequeue()
    }

    $displayPrompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        $Prompt
    }
    else {
        "$Prompt [$DefaultValue]"
    }

    return Read-Host $displayPrompt
}

function Read-MenuSelection {
    param(
        [Parameter(Mandatory)] [string]$Title,
        [Parameter(Mandatory)] [object[]]$Options,
        [Parameter(Mandatory)] [string]$DefaultKey
    )

    $defaultOption = $Options | Where-Object { $_.Key -eq $DefaultKey } | Select-Object -First 1
    if ($null -eq $defaultOption) {
        throw "Default menu option not found: $DefaultKey"
    }

    Write-SetupMessage "+- $Title"
    foreach ($option in $Options) {
        $marker = if ($option.Key -eq $DefaultKey) { '*' } else { ' ' }
        Write-SetupMessage ("| {0} {1}. {2}  {3}" -f $marker, $option.Key, $option.Label, $option.Description)
    }
    Write-SetupMessage '+------------------------------------------------------------------'

    $selection = Read-InstallerInput -Prompt 'Select an option' -DefaultValue $defaultOption.Key
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $defaultOption.Value
    }

    $resolved = $Options | Where-Object { $_.Key -eq $selection.Trim() } | Select-Object -First 1
    if ($null -eq $resolved) {
        throw "Unsupported selection '$selection'."
    }

    return $resolved.Value
}

function Read-Confirmation {
    param([Parameter(Mandatory)] [string]$Prompt)

    $selection = Read-InstallerInput -Prompt $Prompt -DefaultValue 'Y'
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $true
    }

    return -not $selection.Trim().ToLowerInvariant().StartsWith('n')
}

function Get-InteractiveClientOptions {
    return @(
        [pscustomobject]@{ Key = '1'; Value = 'claude-code'; Label = 'Claude Code'; Description = 'Register with claude mcp add' }
        [pscustomobject]@{ Key = '2'; Value = 'codex'; Label = 'Codex'; Description = 'Register with codex mcp add' }
        [pscustomobject]@{ Key = '3'; Value = 'visual-studio'; Label = 'Visual Studio'; Description = 'Write %USERPROFILE%\\.mcp.json' }
        [pscustomobject]@{ Key = '4'; Value = 'claude-desktop'; Label = 'Claude Desktop'; Description = 'Update claude_desktop_config.json' }
        [pscustomobject]@{ Key = '5'; Value = 'cursor'; Label = 'Cursor / VS Code'; Description = 'Write cursor MCP JSON config' }
        [pscustomobject]@{ Key = '6'; Value = 'other'; Label = 'Artifacts only'; Description = 'Install package and emit registration templates only' }
        [pscustomobject]@{ Key = '7'; Value = 'none'; Label = 'No registration'; Description = 'Install binaries only' }
    )
}

function Get-InteractiveDefaultClientKey {
    param([string[]]$DetectedClients)

    if ($DetectedClients -contains 'claude-code') { return '1' }
    if ($DetectedClients -contains 'codex') { return '2' }
    if ($DetectedClients -contains 'visual-studio') { return '3' }
    if ($DetectedClients -contains 'claude-desktop') { return '4' }
    if ($DetectedClients -contains 'cursor') { return '5' }
    return '6'
}

function Invoke-DocsHomepage {
    if ($script:IsJsonOutput -or $NonInteractive) {
        return
    }

    $browserCommand = $env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND
    if (-not [string]::IsNullOrWhiteSpace($browserCommand)) {
        & $browserCommand $script:DocsHomepageUrl
        return
    }

    Start-Process $script:DocsHomepageUrl | Out-Null
}

function Resolve-PackageDirectory {
    param([string]$ConfiguredPackagePath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPackagePath)) {
        return (Resolve-Path $ConfiguredPackagePath).Path
    }

    $packageRoot = (Resolve-Path $PSScriptRoot).Path
    $currentManifest = Join-Path $packageRoot 'manifest.json'
    $childManifest = Join-Path $packageRoot 'bin\manifest.json'
    $packageParent = Split-Path -Parent $packageRoot

    if ((Split-Path $packageRoot -Leaf) -ieq 'bin' -and
        (Test-Path $currentManifest) -and
        -not [string]::IsNullOrWhiteSpace($packageParent)) {
        return $packageParent
    }

    if ((Test-Path $currentManifest) -or (Test-Path $childManifest)) {
        return $packageRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($packageParent) -and
        ((Test-Path (Join-Path $packageParent 'manifest.json')) -or
         (Test-Path (Join-Path $packageParent 'bin\manifest.json')))) {
        return $packageParent
    }

    throw 'PackagePath was not provided and manifest.json was not found next to Setup-WpfDevTools.ps1.'
}

function Resolve-PackageManifestPath {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    $manifestCandidates = @(
        (Join-Path $PackageDirectory 'manifest.json'),
        (Join-Path $PackageDirectory 'bin\manifest.json')
    )

    foreach ($candidate in $manifestCandidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "manifest.json was not found under package path: $PackageDirectory"
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Get-ClaudeDesktopDefaultPath {
    return (Join-Path $env:APPDATA 'Claude\claude_desktop_config.json')
}

function Get-CursorConfigCandidates {
    return @(
        (Join-Path $env:APPDATA 'Cursor\User\mcp.json'),
        (Join-Path $env:APPDATA 'Cursor\mcp.json'),
        (Join-Path $env:USERPROFILE '.cursor\mcp.json')
    )
}

function Resolve-ClaudeDesktopConfigPath {
    param([string]$OverridePath)

    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) {
        return $OverridePath
    }

    return Get-ClaudeDesktopDefaultPath
}

function Resolve-CursorConfigPath {
    param([string]$OverridePath)

    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) {
        return $OverridePath
    }

    foreach ($candidate in Get-CursorConfigCandidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return (Get-CursorConfigCandidates | Select-Object -First 1)
}

function Resolve-VisualStudioConfigPath {
    param([string]$OverridePath)

    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) {
        return $OverridePath
    }

    return (Join-Path $env:USERPROFILE '.mcp.json')
}

function Resolve-InstallScriptPath {
    $packageLocalScript = Join-Path $PSScriptRoot 'internal-install.ps1'
    if (Test-Path $packageLocalScript) {
        return $packageLocalScript
    }

    $packageLocalScript = Join-Path $PSScriptRoot 'install.ps1'
    if (Test-Path $packageLocalScript) {
        return $packageLocalScript
    }

    $repoScript = Join-Path $PSScriptRoot 'Install-WpfDevTools.ps1'
    if (Test-Path $repoScript) {
        return $repoScript
    }

    throw 'Could not locate install.ps1 or Install-WpfDevTools.ps1 next to Setup-WpfDevTools.ps1.'
}

function Test-ClientDetected {
    param([Parameter(Mandatory)] [string]$Client)

    switch ($Client) {
        'codex' {
            return $null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)
        }
        'claude-code' {
            return $null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)
        }
        'claude-desktop' {
            return (Test-Path (Get-ClaudeDesktopDefaultPath))
        }
        'cursor' {
            if (Test-Path (Join-Path $env:LOCALAPPDATA 'Programs\Cursor\Cursor.exe')) {
                return $true
            }

            foreach ($candidate in Get-CursorConfigCandidates) {
                if (Test-Path $candidate) {
                    return $true
                }
            }

            return $false
        }
        'github-copilot-vscode' {
            return $false
        }
        'visual-studio' {
            $globalConfig = Resolve-VisualStudioConfigPath
            if (Test-Path $globalConfig) {
                return $true
            }

            $devenvRoots = @(
                (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio'),
                (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio')
            ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

            foreach ($root in $devenvRoots) {
                $devenv = Get-ChildItem -Path $root -Recurse -Filter devenv.exe -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ($null -ne $devenv) {
                    return $true
                }
            }

            return $false
        }
        'other' {
            return $false
        }
        default {
            throw "Unknown client: $Client"
        }
    }
}

function Get-DetectedClients {
    $detectedClients = @()
    foreach ($client in $script:KnownClients) {
        if (Test-ClientDetected -Client $client) {
            $detectedClients += $client
        }
    }

    return @($detectedClients | Sort-Object -Unique)
}

function Expand-ClientSelection {
    param(
        [string[]]$RawClients,
        [string[]]$DetectedClients
    )

    if ($null -eq $RawClients -or $RawClients.Count -eq 0) {
        return @($DetectedClients | Sort-Object -Unique)
    }

    $expandedClients = @()
    foreach ($item in $RawClients) {
        foreach ($candidate in ($item -split ',')) {
            $normalized = $candidate.Trim().ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                continue
            }

            if ($normalized -eq 'all') {
                return @($script:KnownClients | Sort-Object -Unique)
            }

            if ($normalized -eq 'none') {
                return @()
            }

            if ($script:KnownClients -notcontains $normalized) {
                throw "Unsupported client selection: $normalized"
            }

            $expandedClients += $normalized
        }
    }

    return @($expandedClients | Sort-Object -Unique)
}

function Get-JsonObject {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    $raw = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    return ($raw | ConvertFrom-Json)
}

function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    $backupPath = "$Path.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Copy-Item -Path $Path -Destination $backupPath -Force
    return $backupPath
}

function New-ConfigRoot {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $existingConfig = Get-JsonObject -Path $ConfigPath
    $servers = [ordered]@{}
    if ($null -ne $existingConfig -and $null -ne $existingConfig.$CollectionName) {
        foreach ($property in $existingConfig.$CollectionName.PSObject.Properties) {
            $servers[$property.Name] = $property.Value
        }
    }

    $servers['wpf-devtools'] = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $root = [ordered]@{}
    if ($null -ne $existingConfig) {
        foreach ($property in $existingConfig.PSObject.Properties) {
            if ($property.Name -ne $CollectionName) {
                $root[$property.Name] = $property.Value
            }
        }
    }

    $root[$CollectionName] = $servers
    return $root
}

function Set-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$Client,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $directory = Split-Path -Parent $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $backupPath = Backup-ConfigFile -Path $ConfigPath
    $configRoot = New-ConfigRoot -CollectionName $CollectionName -ConfigPath $ConfigPath -InstalledExecutable $InstalledExecutable
    $configRoot | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8

    return [ordered]@{
        client = $Client
        mode = 'json-file'
        target = $ConfigPath
        backupPath = $backupPath
        applied = $true
    }
}

function Invoke-RegistrationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$Client
    )

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        throw "$Command is not installed. Cannot register $Client automatically."
    }

    $commandOutput = & $Command @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed for $Client with exit code $LASTEXITCODE. $commandOutput"
    }

    return [ordered]@{
        client = $Client
        mode = 'cli'
        target = $Command
        backupPath = $null
        applied = $true
    }
}

function Read-InteractiveSelections {
    param(
        [string[]]$DetectedClients,
        [string]$CurrentInstallRoot
    )

    Write-InstallerBanner -Subtitle 'Offline installer'
    Write-SetupMessage ("Detected clients: " + ($(if ($DetectedClients.Count -gt 0) { $DetectedClients -join ', ' } else { 'none' })))

    $selectedClient = Read-MenuSelection `
        -Title 'Choose installation target' `
        -DefaultKey (Get-InteractiveDefaultClientKey -DetectedClients $DetectedClients) `
        -Options (Get-InteractiveClientOptions)

    $resolvedInstallRoot = if ($script:InstallRootWasSpecified) {
        $CurrentInstallRoot
    }
    else {
        $installPrompt = Read-InstallerInput -Prompt 'Install root' -DefaultValue $CurrentInstallRoot
        if ([string]::IsNullOrWhiteSpace($installPrompt)) { $CurrentInstallRoot } else { $installPrompt.Trim() }
    }

    Write-SetupMessage "Install root: $resolvedInstallRoot"
    Write-SetupMessage "Target client: $selectedClient"
    if (-not (Read-Confirmation -Prompt 'Proceed with installation?')) {
        throw 'Setup cancelled by user.'
    }

    return [ordered]@{
        InstallRoot = $resolvedInstallRoot
        Clients = if ($selectedClient -eq 'none') { @() } else { @($selectedClient) }
    }
}

$detectedClients = @(Get-DetectedClients)
if ($DetectOnly) {
    $summary = [ordered]@{
        detectedClients = @($detectedClients | ForEach-Object { $_ })
        selectedClients = @(@() | ForEach-Object { $_ })
        installRoot = $null
        installedExecutable = $null
        registrations = @(@() | ForEach-Object { $_ })
    }

    if ($script:IsJsonOutput) {
        $summary | ConvertTo-Json -Depth 8
    }
    else {
        Write-SetupMessage ("Detected clients: " + ($(if ($detectedClients.Count -gt 0) { $detectedClients -join ', ' } else { 'none' })))
    }

    return
}

$selectedClients = @(Expand-ClientSelection -RawClients $Clients -DetectedClients $detectedClients)
$resolvedInstallRoot = $InstallRoot
if (-not $NonInteractive) {
    $interactiveSelections = Read-InteractiveSelections -DetectedClients $detectedClients -CurrentInstallRoot $InstallRoot
    $resolvedInstallRoot = $interactiveSelections.InstallRoot
    $selectedClients = @($interactiveSelections.Clients)
}

$packageDir = Resolve-PackageDirectory -ConfiguredPackagePath $PackagePath
$packageManifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $packageDir) -Raw | ConvertFrom-Json
$architecture = [string]$packageManifest.architecture
if ([string]::IsNullOrWhiteSpace($architecture)) {
    throw 'manifest.json does not define architecture'
}

$channel = [string]$packageManifest.channel
$buildConfiguration = [string]$packageManifest.buildConfiguration
$signaturePolicy = [string]$packageManifest.signaturePolicy
if ([string]::IsNullOrWhiteSpace($channel)) {
    $channel = if ($buildConfiguration -eq 'Debug') { 'dev' } else { 'release' }
}
if ([string]::IsNullOrWhiteSpace($buildConfiguration)) {
    $buildConfiguration = if ($channel -eq 'dev') { 'Debug' } else { 'Release' }
}
if ([string]::IsNullOrWhiteSpace($signaturePolicy)) {
    $signaturePolicy = if ($buildConfiguration -eq 'Debug') { 'DebugTrustedRootSkip' } else { 'RequireAuthenticodeSignature' }
}

$installScript = Resolve-InstallScriptPath
$installArguments = @{
    InstallRoot = $resolvedInstallRoot
    Force = $Force
    Quiet = $true
}

if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
    $installArguments.PackagePath = $packageDir
}

& $installScript @installArguments

$resolvedInstallRootFullPath = Resolve-AbsoluteDirectory -Path $resolvedInstallRoot
$installManifestPath = Join-Path $resolvedInstallRootFullPath "$architecture\install-manifest.json"
$installManifest = Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
$installedExecutable = [string]$installManifest.executable
$registrations = @()
foreach ($client in $selectedClients) {
    switch ($client) {
        'claude-code' {
            $registrations += Invoke-RegistrationCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $installedExecutable) -Client $client
        }
        'codex' {
            $registrations += Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $installedExecutable) -Client $client
        }
        'claude-desktop' {
            $registrations += Set-JsonConfigRegistration -Client $client -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath -OverridePath $ClaudeDesktopConfigPath) -InstalledExecutable $installedExecutable
        }
        'cursor' {
            $registrations += Set-JsonConfigRegistration -Client $client -CollectionName 'servers' -ConfigPath (Resolve-CursorConfigPath -OverridePath $CursorConfigPath) -InstalledExecutable $installedExecutable
        }
        'visual-studio' {
            $registrations += Set-JsonConfigRegistration -Client $client -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath -OverridePath $VisualStudioConfigPath) -InstalledExecutable $installedExecutable
        }
        'github-copilot-vscode' {
            $registrations += [ordered]@{
                client = $client
                mode = 'artifact-only'
                target = (Join-Path $resolvedInstallRootFullPath "$architecture\client-registration\github-copilot-vscode.json")
                backupPath = $null
                applied = $true
            }
        }
        'other' {
            $registrations += [ordered]@{
                client = $client
                mode = 'artifact-only'
                target = (Join-Path $resolvedInstallRootFullPath "$architecture\client-registration\other.mcpServers.json")
                backupPath = $null
                applied = $true
            }
        }
        default {
            throw "Unsupported registration client: $client"
        }
    }
}

$result = [ordered]@{
    detectedClients = @($detectedClients | ForEach-Object { $_ })
    selectedClients = @($selectedClients | ForEach-Object { $_ })
    installRoot = $resolvedInstallRootFullPath
    installedExecutable = $installedExecutable
    channel = $channel
    buildConfiguration = $buildConfiguration
    signaturePolicy = $signaturePolicy
    registrations = @($registrations | ForEach-Object { $_ })
}

if ($script:IsJsonOutput) {
    $result | ConvertTo-Json -Depth 10
}
else {
    Write-SetupMessage "Installed executable: $installedExecutable"
    if ($selectedClients.Count -gt 0) {
        Write-SetupMessage ("Registered clients: " + ($selectedClients -join ', '))
    }
    else {
        Write-SetupMessage 'No client registrations were selected.'
    }

    if (-not $NonInteractive) {
        Write-SetupMessage ''
        Write-SetupMessage '+- Next action'
        Write-SetupMessage '| 1. Open docs homepage'
        Write-SetupMessage '| 2. Exit'
        Write-SetupMessage '+------------------------------------------------------------------'
        $completionSelection = Read-InstallerInput -Prompt 'Select an option' -DefaultValue '2'
        if ($completionSelection.Trim() -eq '1') {
            Invoke-DocsHomepage
        }
    }
}
