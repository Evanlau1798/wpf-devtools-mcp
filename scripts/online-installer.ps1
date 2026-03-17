param(
    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'codex-cli', 'visual-studio', 'claude-desktop', 'cursor-vscode', 'github-copilot-vscode', 'other')]
    [string]$Client,

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-online-installer'),
    [string]$PackageArchivePath,

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:DocsHomepageUrl = 'https://evanlau1798.github.io/wpf-devtools-mcp/index.html'
$script:VersionWasSpecified = $PSBoundParameters.ContainsKey('Version')
$script:ArchitectureWasSpecified = $PSBoundParameters.ContainsKey('Architecture')
$script:ClientWasSpecified = $PSBoundParameters.ContainsKey('Client')
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES)) {
    foreach ($entry in ($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES -split '\|\|')) {
        $script:InstallerTestResponses.Enqueue($entry)
    }
}

function Write-InstallerHostMessage {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message)

    if (-not $OutputJson) {
        Write-Host $Message
    }
}

function Write-InstallerBanner {
    param([Parameter(Mandatory)] [string]$Subtitle)

    if ($OutputJson) {
        return
    }

    Write-InstallerHostMessage ''
    Write-InstallerHostMessage '+------------------------------------------------------------------+'
    Write-InstallerHostMessage '|                       WPF DEVTOOLS MCP                           |'
    Write-InstallerHostMessage '|          [Window] [Grid] [Binding] [VisualTree] [Command]       |'
    Write-InstallerHostMessage '+------------------------------------------------------------------+'
    Write-InstallerHostMessage ("| " + $Subtitle.PadRight(64) + ' |')
    Write-InstallerHostMessage '+------------------------------------------------------------------+'
    Write-InstallerHostMessage ''
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

function Get-MenuOptionsLiteral {
    param([Parameter(Mandatory)] [object[]]$Options)

    return (($Options | ForEach-Object { "{0}:{1}" -f $_.Key, $_.Label }) -join '  ')
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

    Write-InstallerHostMessage "+- $Title"
    foreach ($option in $Options) {
        $marker = if ($option.Key -eq $DefaultKey) { '*' } else { ' ' }
        Write-InstallerHostMessage ("| {0} {1}. {2}  {3}" -f $marker, $option.Key, $option.Label, $option.Description)
    }
    Write-InstallerHostMessage '+------------------------------------------------------------------'

    $selection = Read-InstallerInput -Prompt 'Select an option' -DefaultValue $defaultOption.Key
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $defaultOption.Value
    }

    $resolved = $Options | Where-Object { $_.Key -eq $selection.Trim() } | Select-Object -First 1
    if ($null -eq $resolved) {
        throw "Unsupported selection '$selection'. Available options: $(Get-MenuOptionsLiteral -Options $Options)"
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

function Invoke-DocsHomepage {
    if ($OutputJson -or $NonInteractive) {
        return
    }

    $browserCommand = $env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND
    if (-not [string]::IsNullOrWhiteSpace($browserCommand)) {
        & $browserCommand $script:DocsHomepageUrl
        return
    }

    Start-Process $script:DocsHomepageUrl | Out-Null
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Get-ReleaseAssetName {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return [string]::Format('release_{0}_win-{1}.zip', $ResolvedVersion, $ResolvedArchitecture)
}

function Get-ReleaseDownloadUri {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    if ($ResolvedVersion -eq 'latest') {
        return "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName"
    }

    return "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/$ResolvedVersion/$assetName"
}

function Get-GitHubReleaseApiUri {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $apiBase = 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases'
    if ($ResolvedVersion -eq 'latest') {
        return "$apiBase/latest"
    }

    $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
    return "$apiBase/tags/$tag"
}

function Get-DefaultArchitecture {
    $processorArchitecture = [string]$env:PROCESSOR_ARCHITECTURE
    switch ($processorArchitecture.ToUpperInvariant()) {
        'ARM64' { return 'arm64' }
        'X86' { return 'x86' }
        default { return 'x64' }
    }
}

function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) {
        return 'claude-code'
    }

    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) {
        return 'codex'
    }

    $visualStudioRoot = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio'
    if ((-not [string]::IsNullOrWhiteSpace($visualStudioRoot)) -and (Test-Path $visualStudioRoot)) {
        return 'visual-studio'
    }

    return 'claude-code'
}

function Resolve-SelectedValue {
    param(
        [string]$CurrentValue,
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue,
        [string[]]$AllowedValues,
        [switch]$SkipPrompt
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentValue)) {
        return $CurrentValue
    }

    if ($NonInteractive -or $SkipPrompt) {
        return $DefaultValue
    }

    $selection = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $DefaultValue
    }

    $normalizedSelection = $selection.Trim()
    if ($null -ne $AllowedValues -and $AllowedValues.Count -gt 0 -and ($AllowedValues -notcontains $normalizedSelection)) {
        throw "Unsupported selection '$normalizedSelection'. Allowed values: $($AllowedValues -join ', ')"
    }

    return $normalizedSelection
}

function Get-SelectedClientList {
    param([Parameter(Mandatory)] [string]$SelectedClient)

    switch ($SelectedClient) {
        'claude-code' { return @('claude-code') }
        'codex' { return @('codex') }
        'codex-cli' { return @('codex') }
        'visual-studio' { return @('visual-studio') }
        'claude-desktop' { return @('claude-desktop') }
        'cursor-vscode' { return @('cursor') }
        'github-copilot-vscode' { return @('none') }
        'other' { return @('none') }
        default { throw "Unsupported client option: $SelectedClient" }
    }
}

function Get-ReleaseAssetDownloadDetails {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $fallbackUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $apiUri = Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion

    try {
        $release = Invoke-RestMethod -Uri $apiUri -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' }
        if ($null -ne $release) {
            $asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
            if ($null -ne $asset) {
                return @{
                    AssetName = $assetName
                    DownloadUri = [string]$asset.browser_download_url
                    ResolvedVersion = [string]$release.tag_name
                }
            }
        }
    }
    catch {
    }

    return @{
        AssetName = $assetName
        DownloadUri = $fallbackUri
        ResolvedVersion = $ResolvedVersion
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Resolve-InstallerScriptPath {
    param([Parameter(Mandatory)] [string]$ExtractRoot)

    $packageSetup = Join-Path $ExtractRoot 'bin\install.ps1'
    if (Test-Path $packageSetup) {
        return $packageSetup
    }

    $packageSetup = Join-Path $ExtractRoot 'setup.ps1'
    if (Test-Path $packageSetup) {
        return $packageSetup
    }

    $packageInstaller = Join-Path $ExtractRoot 'install.ps1'
    if (Test-Path $packageInstaller) {
        return $packageInstaller
    }

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $repoInstaller = Join-Path $PSScriptRoot 'tools\release\Setup-WpfDevTools.ps1'
        if (Test-Path $repoInstaller) {
            return $repoInstaller
        }
    }

    throw "A package setup/install script was not found in extracted package or relative to scripts/online-installer.ps1. ExtractRoot: $ExtractRoot"
}

if (-not $NonInteractive -and -not $OutputJson) {
    Write-InstallerBanner -Subtitle 'Online installer'
}

$selectedVersionInput = if ($script:VersionWasSpecified) { $Version } else { $null }
$selectedVersion = Resolve-SelectedValue `
    -CurrentValue $selectedVersionInput `
    -Prompt 'Release version' `
    -DefaultValue 'latest'

$selectedArchitecture = if ($script:ArchitectureWasSpecified -or $NonInteractive) {
    Resolve-SelectedValue -CurrentValue $Architecture -Prompt 'Architecture' -DefaultValue (Get-DefaultArchitecture) -AllowedValues @('x64', 'x86', 'arm64') -SkipPrompt
}
else {
    $defaultArchitecture = Get-DefaultArchitecture
    $defaultArchitectureKey = switch ($defaultArchitecture) {
        'x64' { '1' }
        'x86' { '2' }
        'arm64' { '3' }
        default { '1' }
    }
    Read-MenuSelection -Title 'Choose architecture' -DefaultKey $defaultArchitectureKey -Options @(
        [pscustomobject]@{ Key = '1'; Value = 'x64'; Label = 'x64'; Description = 'Recommended for most Windows PCs' }
        [pscustomobject]@{ Key = '2'; Value = 'x86'; Label = 'x86'; Description = 'For older 32-bit environments' }
        [pscustomobject]@{ Key = '3'; Value = 'arm64'; Label = 'arm64'; Description = 'For Windows on ARM devices' }
    )
}

$selectedClient = if ($script:ClientWasSpecified -or $NonInteractive) {
    Resolve-SelectedValue -CurrentValue $Client -Prompt 'Client' -DefaultValue (Get-DefaultClient) -AllowedValues @('claude-code', 'codex', 'visual-studio', 'other') -SkipPrompt
}
else {
    Read-MenuSelection -Title 'Choose installation target' -DefaultKey '1' -Options @(
        [pscustomobject]@{ Key = '1'; Value = 'claude-code'; Label = 'Claude Code'; Description = 'Register with claude mcp add' }
        [pscustomobject]@{ Key = '2'; Value = 'codex'; Label = 'Codex'; Description = 'Register with codex mcp add' }
        [pscustomobject]@{ Key = '3'; Value = 'visual-studio'; Label = 'Visual Studio'; Description = 'Write %USERPROFILE%\\.mcp.json' }
        [pscustomobject]@{ Key = '4'; Value = 'other'; Label = 'Other'; Description = 'Install package and emit registration artifacts only' }
    )
}

$resolvedInstallRoot = if ($script:InstallRootWasSpecified -or $NonInteractive) {
    $InstallRoot
}
else {
    Resolve-SelectedValue -CurrentValue $null -Prompt 'Install root' -DefaultValue $InstallRoot
}

if (-not $NonInteractive -and -not $OutputJson) {
    Write-InstallerHostMessage "Ready to install release '$selectedVersion' for '$selectedArchitecture' into '$resolvedInstallRoot'."
    Write-InstallerHostMessage "Target client: $selectedClient"
    if (-not (Read-Confirmation -Prompt 'Proceed with download and installation?')) {
        throw 'Installation cancelled by user.'
    }
}

$workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
$downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $selectedVersion -ResolvedArchitecture $selectedArchitecture
$assetName = [string]$downloadDetails.AssetName
$downloadUri = [string]$downloadDetails.DownloadUri
$archivePath = if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) { Join-Path $workingRootPath $assetName } else { (Resolve-Path $PackageArchivePath).Path }
$sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $sessionRoot 'package'

try {
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
    }

    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    $installerScript = Resolve-InstallerScriptPath -ExtractRoot $extractRoot

    $arguments = @{
        PackagePath = $extractRoot
        InstallRoot = $resolvedInstallRoot
        Force = $Force
        NonInteractive = $true
        OutputJson = $OutputJson
    }

    $arguments.Clients = $selectedClient

    if ($OutputJson) {
        $null = & $installerScript @arguments
    }
    else {
        & $installerScript @arguments
    }

    $installManifestPath = Join-Path (Join-Path (Resolve-Path $resolvedInstallRoot).Path $selectedArchitecture) 'install-manifest.json'
    $installManifest = if (Test-Path $installManifestPath) {
        Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    if (-not $NonInteractive -and -not $OutputJson) {
        Write-InstallerHostMessage ''
        Write-InstallerHostMessage 'Installation complete.'
        Write-InstallerHostMessage '+- Next action'
        Write-InstallerHostMessage '| 1. Open docs homepage'
        Write-InstallerHostMessage '| 2. Exit'
        Write-InstallerHostMessage '+------------------------------------------------------------------'
        $completionSelection = Read-InstallerInput -Prompt 'Select an option' -DefaultValue '2'
        if ($completionSelection.Trim() -eq '1') {
            Invoke-DocsHomepage
        }
    }

    if ($OutputJson) {
        [ordered]@{
            version = $selectedVersion
            architecture = $selectedArchitecture
            client = $selectedClient
            packageAssetName = $assetName
            downloadUri = $downloadUri
            installRoot = (Resolve-Path $resolvedInstallRoot).Path
            installedExecutable = [string]$installManifest.executable
            selectedClients = @(Get-SelectedClientList -SelectedClient $selectedClient)
        } | ConvertTo-Json -Depth 6
    }
}
finally {
    Remove-PathIfExists -Path $sessionRoot
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Remove-PathIfExists -Path $archivePath
    }
}
