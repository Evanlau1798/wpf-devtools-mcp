param(
    [ValidateSet('install', 'uninstall', 'full-uninstall')]
    [string]$Action = 'install',

    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client,

    [string]$InstallRoot = (Join-Path $env:APPDATA 'WpfDevToolsMcp'),
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-online-installer'),
    [string]$PackageArchivePath,
    [string]$VsCodeConfigPath,
    [string]$VisualStudioConfigPath,
    [string]$ClaudeDesktopConfigPath,
    [string]$CursorConfigPath,
    [string]$CursorProjectRoot,
    [ValidateSet('global', 'project')]
    [string]$CursorMode,

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES)) {
    foreach ($entry in ($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES -split '\|\|')) {
        $script:InstallerTestResponses.Enqueue($entry)
    }
}

function Read-InstallerInput {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [string]$DefaultValue
    )

    if ($script:InstallerTestResponses.Count -gt 0) {
        return $script:InstallerTestResponses.Dequeue()
    }

    if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        return Read-Host $Prompt
    }

    return Read-Host "$Prompt [$DefaultValue]"
}

function Write-InstallerMessage {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message)

    if (-not $OutputJson) {
        Write-Host $Message
    }
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Resolve-AbsolutePath -Path $Path
    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
    return $resolvedPath
}

function Resolve-AbsolutePath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Get-SystemDefaultArchitecture {
    if ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        return 'arm64'
    }

    if ($env:PROCESSOR_ARCHITEW6432 -eq 'x86' -or $env:PROCESSOR_ARCHITECTURE -eq 'x86') {
        return 'x86'
    }

    return 'x64'
}

$script:InstallerHelperManifestFileName = 'installer-helpers.manifest.json'
$script:InstallerHelperSourcePaths = @(
    'scripts/installer/Tui.Terminal.ps1'
    'scripts/installer/Tui.Layout.ps1'
    'scripts/installer/Tui.ScreenModel.ps1'
    'scripts/installer/Tui.Sections.ps1'
    'scripts/installer/Tui.Window.ps1'
    'scripts/installer/Tui.TitleBar.ps1'
    'scripts/installer/Tui.StatusBar.ps1'
    'scripts/installer/Tui.Presenters.ps1'
    'scripts/installer/Tui.PathEditor.ps1'
    'scripts/installer/Tui.PathEditor.Views.ps1'
    'scripts/installer/Tui.Renderer.ps1'
    'scripts/installer/Tui.Input.ps1'
    'scripts/installer/Tui.Flow.ps1'
    'scripts/installer/Tui.Confirm.ps1'
    'scripts/installer/Installer.Discovery.ps1'
    'scripts/installer/Installer.Uninstall.ps1'
)
$script:InstallerHelperRepositoryRelativePath = 'scripts/installer'
$script:TuiScreenNames = @('HomeScreen', 'InstallScreen', 'UninstallScreen', 'ConfirmScreen', 'PathEditorScreen', 'DirectoryPickerScreen', 'FolderNamePromptScreen', 'ProgressScreen')
$script:TuiUiMarkers = @('Installed v', 'Update available', 'Architecture', 'Install location', 'Update All')
$script:TuiConfirmationModes = @('unregister', 'full-uninstall', 'close-app')
$script:TuiUninstallActions = @('UnregisterTarget', 'FullUninstall', 'Full Uninstall')
$script:InstallerDiscoveryContractFields = @('RegistrationMode', 'InstalledExecutable', 'InstallerOwned', 'ConfirmationStep')
$script:TuiNavigationKeys = @(
    [ConsoleKey]::UpArrow
    [ConsoleKey]::DownArrow
    [ConsoleKey]::LeftArrow
    [ConsoleKey]::RightArrow
    [ConsoleKey]::Tab
    [ConsoleKey]::Enter
    [ConsoleKey]::Escape
    [ConsoleKey]::Backspace
)
$script:TuiNavigationTokens = @('ConsoleKey.UpArrow', 'ConsoleKey.DownArrow', 'ConsoleKey.LeftArrow', 'ConsoleKey.RightArrow', 'ConsoleKey.Tab', 'ConsoleKey.Enter')
$script:ResolvedOnlineReleaseVersion = $null

function Resolve-InstallerScriptRoot {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Split-Path -Parent $PSCommandPath)
    }

    return $null
}

function Get-TuiHelperRuntimeRoot {
    $runtimeRoot = Join-Path (Resolve-AbsoluteDirectory -Path $WorkingRoot) 'tui-helpers'
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    return $runtimeRoot
}

function Get-TuiHelperCacheKeyPath {
    param([Parameter(Mandatory)] [string]$RuntimeRoot)

    return (Join-Path $RuntimeRoot 'helper-cache-key.txt')
}

function Get-TuiHelperManifestPath {
    param([Parameter(Mandatory)] [string]$RootPath)

    return (Join-Path $RootPath $script:InstallerHelperManifestFileName)
}

function Get-TuiHelperOverrideDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        return $env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY
    }

    return $null
}

function Get-HelperLeafNames {
    return @($script:InstallerHelperSourcePaths | ForEach-Object { Split-Path $_ -Leaf })
}

function Get-InstallerTimeoutSeconds {
    param(
        [Parameter(Mandatory)] [string]$EnvironmentVariable,
        [Parameter(Mandatory)] [int]$DefaultValue,
        [int]$MinimumValue = 1,
        [int]$MaximumValue = 120
    )

    $rawValue = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return $DefaultValue
    }

    $parsedValue = 0
    if (-not [int]::TryParse($rawValue, [ref]$parsedValue)) {
        return $DefaultValue
    }

    return [Math]::Min($MaximumValue, [Math]::Max($MinimumValue, $parsedValue))
}

function Get-TuiHelperRequestTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC' -DefaultValue 5 -MinimumValue 1 -MaximumValue 30)
}

function Get-TuiHelperBootstrapTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC' -DefaultValue 20 -MinimumValue 3 -MaximumValue 120)
}

function Get-InstallerVerificationTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC' -DefaultValue 2 -MinimumValue 1 -MaximumValue 30)
}

function Test-TuiBootstrapAnsiSupport {
    if ($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI -eq '1') {
        return $false
    }

    if ($env:WPFDEVTOOLS_INSTALLER_TEST_FORCE_ANSI -eq '1') {
        return $true
    }

    try {
        if ([Console]::IsOutputRedirected) {
            return $false
        }
    }
    catch {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WT_SESSION)) {
        return $true
    }

    if (-not [string]::IsNullOrWhiteSpace($env:TERM_PROGRAM)) {
        return $true
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ConEmuANSI) -and $env:ConEmuANSI -eq 'ON') {
        return $true
    }

    return ($PSVersionTable.PSVersion.Major -ge 7)
}

function Get-TuiBootstrapViewport {
    $width = 0
    $height = 0

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH, [ref]$width)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT, [ref]$height)
    }

    if ($width -le 0 -or $height -le 0) {
        try {
            if ($width -le 0) {
                $width = [Console]::WindowWidth
            }

            if ($height -le 0) {
                $height = [Console]::WindowHeight
            }
        }
        catch {
        }
    }

    if ($width -le 0) {
        $width = 100
    }

    if ($height -le 0) {
        $height = 32
    }

    return [ordered]@{
        Width = [Math]::Max(20, [int]$width)
        Height = [Math]::Max(12, [int]$height)
        UseAnsi = [bool](Test-TuiBootstrapAnsiSupport)
    }
}

function Get-TuiBootstrapBorderGlyphs {
    if ($env:WPFDEVTOOLS_INSTALLER_TEST_ASCII_BORDER -eq '1') {
        return [ordered]@{
            Horizontal = '-'
            Vertical = '|'
            TopLeft = '+'
            TopRight = '+'
            BottomLeft = '+'
            BottomRight = '+'
        }
    }

    return [ordered]@{
        Horizontal = [string][char]0x2500
        Vertical = [string][char]0x2502
        TopLeft = [string][char]0x250C
        TopRight = [string][char]0x2510
        BottomLeft = [string][char]0x2514
        BottomRight = [string][char]0x2518
    }
}

function Get-TuiBootstrapRuleLine {
    param(
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Glyphs
    )

    return ($Glyphs.Horizontal * [Math]::Max(0, $Width))
}

function Pad-TuiBootstrapLine {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $value = [string]$Text
    if ($value.Length -gt $Width) {
        return $value.Substring(0, $Width)
    }

    return $value.PadRight($Width)
}

function New-TuiBootstrapCenteredLine {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $value = [string]$Text
    if ([string]::IsNullOrWhiteSpace($value)) {
        return (' ' * $Width)
    }

    if ($value.Length -ge $Width) {
        return $value.Substring(0, $Width)
    }

    $leftPadding = [Math]::Floor(([double]($Width - $value.Length)) / 2)
    return ((' ' * [int]$leftPadding) + $value).PadRight($Width)
}

function ConvertTo-TuiBootstrapWrappedLines {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $remaining = [string]$Text
    if ([string]::IsNullOrWhiteSpace($remaining)) {
        $lines.Add('')
        return @($lines)
    }

    while ($remaining.Length -gt $Width) {
        $lines.Add($remaining.Substring(0, $Width))
        $remaining = $remaining.Substring($Width)
    }

    if ($remaining.Length -gt 0) {
        $lines.Add($remaining)
    }

    return @($lines)
}

function Sync-TuiBootstrapConsoleBuffer {
    param([Parameter(Mandatory)] $Viewport)

    $targetWidth = [Math]::Max(20, [int]$Viewport.Width)
    $targetHeight = [Math]::Max(12, [int]$Viewport.Height)

    try {
        $rawUi = $Host.UI.RawUI
        $windowSize = $rawUi.WindowSize
        $targetWidth = [Math]::Max([int]$windowSize.Width, $targetWidth)
        $targetHeight = [Math]::Max([int]$windowSize.Height, $targetHeight)
        $bufferSize = $rawUi.BufferSize
        if (($bufferSize.Width -ne $targetWidth) -or ($bufferSize.Height -ne $targetHeight)) {
            $rawUi.BufferSize = New-Object Management.Automation.Host.Size($targetWidth, $targetHeight)
        }

        return $true
    }
    catch {
    }

    try {
        if (([Console]::BufferWidth -ne $targetWidth) -or ([Console]::BufferHeight -ne $targetHeight)) {
            [Console]::SetBufferSize($targetWidth, $targetHeight)
        }

        return $true
    }
    catch {
    }

    return $false
}

function Enter-TuiBootstrapTerminalSession {
    param([Parameter(Mandatory)] $Viewport)

    $session = [ordered]@{
        UsedAlternateScreen = $false
        HidCursor = $false
        ManagedBuffer = $false
        SavedBufferWidth = 0
        SavedBufferHeight = 0
        SavedCursorVisible = $null
    }

    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        return $session
    }

    try {
        $rawUi = $Host.UI.RawUI
        $bufferSize = $rawUi.BufferSize
        $session.SavedBufferWidth = [int]$bufferSize.Width
        $session.SavedBufferHeight = [int]$bufferSize.Height
        $session.ManagedBuffer = $true
    }
    catch {
        try {
            $session.SavedBufferWidth = [int][Console]::BufferWidth
            $session.SavedBufferHeight = [int][Console]::BufferHeight
            $session.ManagedBuffer = $true
        }
        catch {
            $session.ManagedBuffer = $false
        }
    }

    try {
        $session.SavedCursorVisible = [bool][Console]::CursorVisible
    }
    catch {
        $session.SavedCursorVisible = $null
    }

    [void](Sync-TuiBootstrapConsoleBuffer -Viewport $Viewport)

    if (-not [bool]$Viewport.UseAnsi) {
        try {
            [Console]::CursorVisible = $false
            $session.HidCursor = $true
        }
        catch {
        }

        return $session
    }

    $escape = [string][char]27
    try {
        [Console]::Write("${escape}[?1049h${escape}[?25l${escape}[2J${escape}[H")
        $session.UsedAlternateScreen = $true
        $session.HidCursor = $true
    }
    catch {
    }

    try {
        [Console]::CursorVisible = $false
        $session.HidCursor = $true
    }
    catch {
    }

    return $session
}

function Exit-TuiBootstrapTerminalSession {
    param($Session)

    if ($null -eq $Session) {
        return
    }

    $escape = [string][char]27
    if ([bool]$Session.UsedAlternateScreen) {
        try {
            [Console]::Write("${escape}[?25h${escape}[?1049l")
        }
        catch {
        }
    }

    if ([bool]$Session.ManagedBuffer -and ([int]$Session.SavedBufferWidth -gt 0) -and ([int]$Session.SavedBufferHeight -gt 0)) {
        try {
            $rawUi = $Host.UI.RawUI
            $rawUi.BufferSize = New-Object Management.Automation.Host.Size([int]$Session.SavedBufferWidth, [int]$Session.SavedBufferHeight)
        }
        catch {
            try {
                [Console]::SetBufferSize([int]$Session.SavedBufferWidth, [int]$Session.SavedBufferHeight)
            }
            catch {
            }
        }
    }

    if ($null -ne $Session.SavedCursorVisible) {
        try {
            [Console]::CursorVisible = [bool]$Session.SavedCursorVisible
            return
        }
        catch {
        }
    }

    if ([bool]$Session.HidCursor) {
        try {
            [Console]::Write("${escape}[?25h")
        }
        catch {
            try {
                [Console]::CursorVisible = $true
            }
            catch {
            }
        }
    }
}

function Close-TuiBootstrapScreen {
    if ($null -ne $script:TuiBootstrapTerminalSession) {
        Exit-TuiBootstrapTerminalSession -Session $script:TuiBootstrapTerminalSession
    }

    $script:TuiBootstrapTerminalSession = $null
    $script:LastTuiBootstrapMessage = $null
}

function Write-TuiBootstrapScreen {
    param([Parameter(Mandatory)] [string]$Message)

    if ($script:LastTuiBootstrapMessage -eq $Message) {
        return $null
    }

    $viewport = Get-TuiBootstrapViewport
    if ($null -eq $script:TuiBootstrapTerminalSession) {
        $script:TuiBootstrapTerminalSession = Enter-TuiBootstrapTerminalSession -Viewport $viewport
    }

    $glyphs = Get-TuiBootstrapBorderGlyphs
    $innerWidth = [Math]::Max(18, [int]$viewport.Width - 2)
    $innerHeight = [Math]::Max(10, [int]$viewport.Height - 2)
    $title = 'WPF DevTools MCP'
    $subtitle = 'Model Context Protocol Server'
    $eyebrow = 'Installation Manager'
    $caption = '[_] [ ] [X]'
    $titleRow = (' ' + $title).PadRight([Math]::Max(1, $innerWidth - $caption.Length)) + $caption
    $rule = Get-TuiBootstrapRuleLine -Width $innerWidth -Glyphs $glyphs
    $progressTitle = switch -Wildcard ($Message) {
        'Preparing installer UI...*' { 'Preparing installer UI' }
        'Loading installer runtime...*' { 'Loading installer runtime' }
        'Loading installer data...*' { 'Loading installer data' }
        'Checking latest release*' { 'Checking latest release' }
        default { 'Loading installer runtime' }
    }

    $heroLines = @(
        New-TuiBootstrapCenteredLine -Text $title -Width $innerWidth
        New-TuiBootstrapCenteredLine -Text $subtitle -Width $innerWidth
        New-TuiBootstrapCenteredLine -Text $eyebrow -Width $innerWidth
        (' ' * $innerWidth)
        New-TuiBootstrapCenteredLine -Text $progressTitle -Width $innerWidth
    )

    $statusLines = @(ConvertTo-TuiBootstrapWrappedLines -Text "[Status] $Message" -Width $innerWidth) | ForEach-Object {
        Pad-TuiBootstrapLine -Text ([string]$_) -Width $innerWidth
    }

    $footerLines = @(
        (Pad-TuiBootstrapLine -Text $rule -Width $innerWidth)
        @($statusLines)
    )

    $bodyLineCount = [Math]::Max(0, $innerHeight - 2 - $footerLines.Count)
    $topPadding = [Math]::Max(0, [Math]::Floor(([double]($bodyLineCount - $heroLines.Count)) / 2))
    $bodyLines = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $topPadding; $index++) {
        $bodyLines.Add((' ' * $innerWidth))
    }
    foreach ($heroLine in $heroLines) {
        if ($bodyLines.Count -ge $bodyLineCount) {
            break
        }

        $bodyLines.Add((Pad-TuiBootstrapLine -Text ([string]$heroLine) -Width $innerWidth))
    }
    while ($bodyLines.Count -lt $bodyLineCount) {
        $bodyLines.Add((' ' * $innerWidth))
    }

    $innerLines = @(
        (Pad-TuiBootstrapLine -Text $titleRow -Width $innerWidth)
        (Pad-TuiBootstrapLine -Text $rule -Width $innerWidth)
        @($bodyLines)
        @($footerLines)
    )

    $frameLines = New-Object System.Collections.Generic.List[string]
    $frameLines.Add($glyphs.TopLeft + $rule + $glyphs.TopRight)
    foreach ($line in $innerLines) {
        $frameLines.Add($glyphs.Vertical + (Pad-TuiBootstrapLine -Text ([string]$line) -Width $innerWidth) + $glyphs.Vertical)
    }
    $frameLines.Add($glyphs.BottomLeft + $rule + $glyphs.BottomRight)

    $frameText = $frameLines -join [Environment]::NewLine
    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        foreach ($line in $frameLines) {
            Write-Host $line
        }
    }
    else {
        [void](Sync-TuiBootstrapConsoleBuffer -Viewport $viewport)
        if ([bool]$viewport.UseAnsi) {
            $escape = [string][char]27
            try {
                [Console]::Write("${escape}[H")
                [Console]::Write($frameText)
            }
            catch {
                [Console]::SetCursorPosition(0, 0)
                [Console]::Write($frameText)
            }
        }
        else {
            try {
                [Console]::SetCursorPosition(0, 0)
                [Console]::Write($frameText)
            }
            catch {
                try {
                    Clear-Host
                }
                catch {
                }

                foreach ($line in $frameLines) {
                    Write-Host $line
                }
            }
        }
    }

    $script:LastTuiBootstrapMessage = $Message
    return $frameText
}

function Get-ComputedInstallerHelperCacheKey {
    param(
        [Parameter(Mandatory)] [string]$HelperDirectory,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    $records = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in ($HelperFiles | Sort-Object)) {
        $helperPath = Join-Path $HelperDirectory $helperFile
        if (-not (Test-Path $helperPath)) {
            throw "Helper file was not found while computing the installer cache key: $helperPath"
        }

        $fileHash = (Get-FileHash -Algorithm SHA256 -Path $helperPath).Hash.ToLowerInvariant()
        $records.Add("${helperFile}:$fileHash")
    }

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($records -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-InstallerHelperRuntimeCacheKey {
    param([Parameter(Mandatory)] $Manifest)

    $seedParts = @(
        [string]$Manifest.CacheKey
        ((Get-Item 'function:Test-TuiSupport').ScriptBlock.ToString())
        ((Get-Item 'function:Resolve-Selection').ScriptBlock.ToString())
        ((Get-Item 'function:Invoke-InstallerAction').ScriptBlock.ToString())
        ($script:InstallerHelperSourcePaths -join '|')
    )

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($seedParts -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'runtime-sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-TuiHelperManifest {
    param(
        [Parameter(Mandatory)] [string]$ManifestPath,
        [Parameter(Mandatory)] [string]$HelperDirectory
    )

    if (-not (Test-Path $ManifestPath)) {
        return $null
    }

    $parsed = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    $helperFiles = @()
    if ($null -ne $parsed.helperFiles) {
        foreach ($entry in $parsed.helperFiles) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry)) {
                $helperFiles += [string]$entry
            }
        }
    }

    if ($helperFiles.Count -eq 0) {
        $helperFiles = @(Get-HelperLeafNames)
    }

    $cacheKey = [string]$parsed.cacheKey
    if ([string]::IsNullOrWhiteSpace($cacheKey)) {
        $cacheKey = Get-ComputedInstallerHelperCacheKey -HelperDirectory $HelperDirectory -HelperFiles $helperFiles
    }

    return [ordered]@{
        CacheKey = $cacheKey
        HelperFiles = @($helperFiles)
    }
}

function Get-TuiHelperManifest {
    if ($null -ne $script:TuiHelperManifest) {
        return $script:TuiHelperManifest
    }

    $localScriptRoot = Resolve-InstallerScriptRoot
    $candidateRoots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        $candidateRoots.Add((Join-Path $localScriptRoot 'installer'))
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    if (-not [string]::IsNullOrWhiteSpace($overrideDirectory)) {
        $candidateRoots.Add($overrideDirectory)
    }

    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot) -or -not (Test-Path $candidateRoot)) {
            continue
        }

        $helperFiles = @(Get-HelperLeafNames)
        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path (Join-Path $candidateRoot $helperFile))) {
                $allPresent = $false
                break
            }
        }

        if (-not $allPresent) {
            continue
        }

        $manifestPath = Get-TuiHelperManifestPath -RootPath $candidateRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $candidateRoot
        if ($null -eq $manifest) {
            $manifest = [ordered]@{
                CacheKey = (Get-ComputedInstallerHelperCacheKey -HelperDirectory $candidateRoot -HelperFiles $helperFiles)
                HelperFiles = $helperFiles
            }
        }

        $script:TuiHelperManifest = $manifest
        return $manifest
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri

    $manifestUri = "$downloadBaseUri/$($script:InstallerHelperManifestFileName)"
    $temporaryManifestPath = "$manifestPath.download"
    try {
        Write-TuiBootstrapScreen 'Preparing installer UI... (manifest)' | Out-Host
        Invoke-WebRequest -Uri $manifestUri -OutFile $temporaryManifestPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-Item -Path $temporaryManifestPath -Destination $manifestPath -Force
    }
    catch {
        Remove-PathIfExists -Path $temporaryManifestPath
        throw "Failed to download installer helper manifest from $manifestUri. $($_.Exception.Message)"
    }

    $script:TuiHelperManifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
    return $script:TuiHelperManifest
}

function Ensure-TuiHelpersAvailable {
    if (-not [string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        return $script:TuiHelperResolvedRoot
    }

    $manifest = Get-TuiHelperManifest
    $localScriptRoot = Resolve-InstallerScriptRoot
    $candidateRoots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        $candidateRoots.Add((Join-Path $localScriptRoot 'installer'))
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    if (-not [string]::IsNullOrWhiteSpace($overrideDirectory)) {
        $candidateRoots.Add($overrideDirectory)
    }

    $helperFiles = if ($null -ne $manifest -and $manifest.HelperFiles.Count -gt 0) { @($manifest.HelperFiles) } else { @(Get-HelperLeafNames) }
    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path (Join-Path $candidateRoot $helperFile))) {
                $allPresent = $false
                break
            }
        }

        if ($allPresent) {
            $script:TuiHelperResolvedRoot = $candidateRoot
            return $candidateRoot
        }
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $cacheKeyPath = Get-TuiHelperCacheKeyPath -RuntimeRoot $runtimeRoot
    $cachedKey = if (Test-Path $cacheKeyPath) { (Get-Content -Path $cacheKeyPath -Raw).Trim() } else { $null }
    $targetCacheKey = if ($null -ne $manifest) { Get-InstallerHelperRuntimeCacheKey -Manifest $manifest } else { $null }
    if ($cachedKey -ne $targetCacheKey) {
        Remove-PathIfExists -Path $runtimeRoot
        New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
        if ($null -ne $manifest) {
            $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Get-TuiHelperManifestPath -RootPath $runtimeRoot) -Encoding UTF8
        }
    }

    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri

    $requestTimeoutSeconds = Get-TuiHelperRequestTimeoutSeconds
    $bootstrapDeadline = [DateTimeOffset]::UtcNow.AddSeconds((Get-TuiHelperBootstrapTimeoutSeconds))
    $totalHelperCount = $helperFiles.Count
    $downloadIndex = 0

    foreach ($helperFile in $helperFiles) {
        if ([DateTimeOffset]::UtcNow -gt $bootstrapDeadline) {
            throw 'Installer UI bootstrap timed out before the runtime assets finished downloading.'
        }

        $destinationPath = Join-Path $runtimeRoot $helperFile
        if (Test-Path $destinationPath) {
            $downloadIndex += 1
            continue
        }

        $downloadIndex += 1
        Write-TuiBootstrapScreen "Preparing installer UI... ($downloadIndex/$totalHelperCount)" | Out-Host
        $downloadUri = "$downloadBaseUri/$helperFile"
        try {
            Invoke-WebRequest -Uri $downloadUri -OutFile $destinationPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec $requestTimeoutSeconds
        }
        catch {
            throw "Failed to download installer UI runtime from $downloadUri. $($_.Exception.Message)"
        }
    }

    Set-Content -Path $cacheKeyPath -Value $targetCacheKey -Encoding UTF8
    $script:TuiHelperResolvedRoot = $runtimeRoot
    return $runtimeRoot
}

function Import-TuiHelpers {
    $helperRoot = Ensure-TuiHelpersAvailable
    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw 'TUI helper scripts are unavailable in the current execution context.'
    }

    $helperPaths = New-Object System.Collections.Generic.List[string]
    foreach ($repoRelativePath in $script:InstallerHelperSourcePaths) {
        $leafName = Split-Path $repoRelativePath -Leaf
        $runtimePath = Join-Path $helperRoot $leafName
        if (-not (Test-Path $runtimePath)) {
            throw "TUI helper script was not found: $runtimePath"
        }

        $helperPaths.Add($runtimePath)
    }

    return @($helperPaths)
}

function Invoke-WithTuiHelpers {
    param([Parameter(Mandatory)] [scriptblock]$ScriptBlock)

    foreach ($helperPath in @(Import-TuiHelpers)) {
        . $helperPath
    }

    return (. $ScriptBlock)
}

function Get-NextArchitecture {
    param(
        [Parameter(Mandatory)] [string]$Current,
        [Parameter(Mandatory)] [int]$Direction
    )

    $architectures = @('x64', 'x86', 'arm64')
    $index = [Array]::IndexOf($architectures, $Current)
    if ($index -lt 0) {
        $index = [Array]::IndexOf($architectures, (Get-SystemDefaultArchitecture))
    }

    $index = ($index + $Direction) % $architectures.Count
    if ($index -lt 0) {
        $index += $architectures.Count
    }

    return $architectures[$index]
}

function Get-SupportedClients {
    return @(
        [pscustomobject]@{ Id = 'claude-code'; Label = 'Claude Code'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'codex'; Label = 'Codex/Codex CLI'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'cursor'; Label = 'Cursor'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'vscode'; Label = 'VS Code'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'visual-studio'; Label = 'Visual Studio'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'claude-desktop'; Label = 'Claude Desktop'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'other'; Label = 'Other'; ConfigType = 'artifact-only' }
    )
}

function Resolve-ClientBaseId {
    param([Parameter(Mandatory)] [string]$ClientId)

    if ($ClientId -like 'cursor-*') {
        return 'cursor'
    }

    return $ClientId
}

function Resolve-ClientStateKey {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [string]$RegistrationMode
    )

    if ((Resolve-ClientBaseId -ClientId $ClientId) -ne 'cursor') {
        return $ClientId
    }

    if ($ClientId -in @('cursor-global', 'cursor-project')) {
        return $ClientId
    }

    switch ([string]$RegistrationMode) {
        'cursor-project' { return 'cursor-project' }
        default { return 'cursor-global' }
    }
}

function Resolve-ClientLabel {
    param([Parameter(Mandatory)] [string]$ClientId)

    switch ($ClientId) {
        'cursor-global' { return 'Cursor (Global)' }
        'cursor-project' { return 'Cursor (Project)' }
    }

    $client = Get-SupportedClients | Where-Object { $_.Id -eq (Resolve-ClientBaseId -ClientId $ClientId) } | Select-Object -First 1
    if ($null -ne $client) {
        return [string]$client.Label
    }

    return $ClientId
}

function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) { return 'claude-code' }
    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) { return 'codex' }
    if ($null -ne (Get-Command 'cursor-agent' -ErrorAction SilentlyContinue)) { return 'cursor' }
    if (Test-Path (Join-Path $env:USERPROFILE '.cursor')) { return 'cursor' }
    if (Test-Path (Join-Path $env:APPDATA 'Code\User')) { return 'vscode' }
    if (Test-Path (Join-Path $env:USERPROFILE '.mcp.json')) { return 'visual-studio' }
    return 'other'
}

function Resolve-InstallerStatePath {
    param([switch]$CreateRoot)

    $stateRoot = Resolve-AbsolutePath -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
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

function Get-InstallerState {
    $statePath = Resolve-InstallerStatePath
    $state = Get-EmptyInstallerState

    if (-not (Test-Path $statePath)) {
        return $state
    }

    $raw = Get-Content -Path $statePath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $state
    }

    $parsed = $raw | ConvertFrom-Json
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
    $State | ConvertTo-Json -Depth 10 | Set-Content -Path $statePath -Encoding UTF8
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

function Get-DefaultInstallRootPath {
    return (Join-Path $env:APPDATA 'WpfDevToolsMcp')
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

    if ([string]::IsNullOrWhiteSpace($InstalledExecutable) -or -not (Test-Path $InstalledExecutable)) {
        return $result
    }

    $architectureMatch = [regex]::Match($InstalledExecutable, 'wpf-devtools-(x64|x86|arm64)\.exe', 'IgnoreCase')
    if ($architectureMatch.Success) {
        $result.Architecture = [string]$architectureMatch.Groups[1].Value.ToLowerInvariant()
    }

    $binDirectory = Split-Path -Parent $InstalledExecutable
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

    $manifestPath = Join-Path $installBase 'install-manifest.json'
    if (-not (Test-Path $manifestPath)) {
        return $result
    }

    try {
        $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
        $manifestExecutable = [string]$manifest.executable
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and (Test-InstallerPathEqualsCore -Left $manifestExecutable -Right $InstalledExecutable)) {
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

function Get-InstallerRecordStringValueCore {
    param(
        $Record,
        [Parameter(Mandatory)] [string[]]$PropertyNames
    )

    if ($null -eq $Record) {
        return $null
    }

    if ($Record -is [System.Collections.IDictionary]) {
        foreach ($propertyName in $PropertyNames) {
            if ($Record.Contains($propertyName) -and -not [string]::IsNullOrWhiteSpace([string]$Record[$propertyName])) {
                return [string]$Record[$propertyName]
            }
        }
    }

    foreach ($propertyName in $PropertyNames) {
        $property = $Record.PSObject.Properties[$propertyName]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return $null
}

function Get-InstallerKnownArchitecturesCore {
    return @('x64', 'x86', 'arm64')
}

function Get-LiveInstallerManifestEvidence {
    param(
        [string]$InstallRoot,
        [string]$Architecture
    )

    if ([string]::IsNullOrWhiteSpace($InstallRoot) -or [string]::IsNullOrWhiteSpace($Architecture)) {
        return $null
    }

    $installBase = Join-Path $InstallRoot $Architecture
    $manifestPath = Join-Path $installBase 'install-manifest.json'
    if (-not (Test-Path $manifestPath)) {
        return $null
    }

    try {
        $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $manifestInstallRoot = [string]$manifest.installRoot
    if (-not [string]::IsNullOrWhiteSpace($manifestInstallRoot) -and -not (Test-InstallerPathEqualsCore -Left $manifestInstallRoot -Right $InstallRoot)) {
        return $null
    }

    $installedExecutable = [string]$manifest.executable
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$Architecture.exe"
    }

    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    if (-not [bool]$ownership.InstallerOwned) {
        return $null
    }

    if (-not (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $InstallRoot)) {
        return $null
    }

    return [ordered]@{
        Architecture = $Architecture
        InstalledExecutable = [string]$ownership.InstalledExecutable
        ResolvedVersion = [string]$ownership.ResolvedVersion
    }
}

function Test-InstallRootHasLiveInstallerEvidence {
    param([string]$InstallRoot)

    foreach ($architecture in @(Get-InstallerKnownArchitecturesCore)) {
        if ($null -ne (Get-LiveInstallerManifestEvidence -InstallRoot $InstallRoot -Architecture $architecture)) {
            return $true
        }
    }

    return $false
}

function Test-StateRecordHasLiveInstallEvidence {
    param(
        $Record,
        [string]$ExpectedInstallRoot
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedInstallRoot) -or $null -eq $Record) {
        return $false
    }

    $recordInstallRoot = Get-InstallerRecordStringValueCore -Record $Record -PropertyNames @('installRoot', 'InstallRoot')
    if (-not (Test-InstallerPathEqualsCore -Left $recordInstallRoot -Right $ExpectedInstallRoot)) {
        return $false
    }

    $installedExecutable = Get-InstallerRecordStringValueCore -Record $Record -PropertyNames @('installedExecutable', 'InstalledExecutable', 'executable', 'Executable')
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        return $false
    }

    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    if (-not [bool]$ownership.InstallerOwned) {
        return $false
    }

    return (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $ExpectedInstallRoot)
}

function Resolve-PreferredInstallRoot {
    if ($script:InstallRootWasSpecified) {
        return $InstallRoot
    }

    $state = Get-InstallerState
    if (-not [string]::IsNullOrWhiteSpace($state.lastInstallRoot)) {
        $lastInstallRoot = [string]$state.lastInstallRoot
        $defaultInstallRoot = Get-DefaultInstallRootPath
        if (Test-InstallerPathEqualsCore -Left $lastInstallRoot -Right $defaultInstallRoot) {
            return $defaultInstallRoot
        }

        $hasArchitectureEvidence = @($state.architectures.GetEnumerator() | Where-Object {
                Test-StateRecordHasLiveInstallEvidence -Record $_.Value -ExpectedInstallRoot $lastInstallRoot
            }).Count -gt 0
        $hasRegistrationEvidence = @($state.registrations.GetEnumerator() | Where-Object {
                Test-StateRecordHasLiveInstallEvidence -Record $_.Value -ExpectedInstallRoot $lastInstallRoot
            }).Count -gt 0
        $hasFilesystemEvidence = Test-InstallRootHasLiveInstallerEvidence -InstallRoot $lastInstallRoot
        if ($hasArchitectureEvidence -or $hasRegistrationEvidence -or $hasFilesystemEvidence) {
            return $lastInstallRoot
        }
    }

    # Default install root: %APPDATA%\WpfDevToolsMcp
    return (Get-DefaultInstallRootPath)
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

function Get-GitHubTagRef {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $tagVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    if ([string]::IsNullOrWhiteSpace($tagVersion)) {
        throw 'Failed to resolve the installer helper release version.'
    }

    if ($tagVersion.StartsWith('v')) {
        return $tagVersion
    }

    return "v$tagVersion"
}

function Get-ReleaseRawContentBaseUri {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$RepositoryRelativePath
    )

    $tagRef = Get-GitHubTagRef -ResolvedVersion $ResolvedVersion
    $normalizedPath = $RepositoryRelativePath.Trim([char[]]@('\', '/')).Replace('\', '/')
    return "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/$tagRef/$normalizedPath"
}

function Resolve-TuiHelperDownloadBaseUri {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        return $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }

    $resolvedVersion = Resolve-RequestedReleaseVersion -RequestedVersion $Version
    return (Get-ReleaseRawContentBaseUri -ResolvedVersion $resolvedVersion -RepositoryRelativePath $script:InstallerHelperRepositoryRelativePath)
}

function Get-ReleaseAssetDownloadDetails {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $fallbackUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture

    try {
        $release = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
        $asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
        if ($null -ne $asset) {
            return [ordered]@{
                AssetName = $assetName
                DownloadUri = [string]$asset.browser_download_url
                ResolvedVersion = ([string]$release.tag_name).TrimStart('v')
            }
        }
    }
    catch {
    }

    return [ordered]@{
        AssetName = $assetName
        DownloadUri = $fallbackUri
        ResolvedVersion = $ResolvedVersion
    }
}

function Resolve-LocalPackageRoot {
    $scriptRoot = Resolve-InstallerScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        return $null
    }

    $binManifestPath = Join-Path $scriptRoot 'manifest.json'
    if (Test-Path $binManifestPath) {
        return (Split-Path -Parent $scriptRoot)
    }

    $packageManifestPath = Join-Path $scriptRoot 'bin\manifest.json'
    if (Test-Path $packageManifestPath) {
        return $scriptRoot
    }

    return $null
}

function Resolve-LatestVersionCachePath {
    param([switch]$CreateRoot)

    $stateRoot = Resolve-AbsolutePath -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'latest-release-cache.json')
}

function Get-CachedLatestInstallerVersion {
    $cachePath = Resolve-LatestVersionCachePath
    if (-not (Test-Path $cachePath)) {
        return $null
    }

    try {
        $parsed = Get-Content -Path $cachePath -Raw | ConvertFrom-Json
        return [string]$parsed.version
    }
    catch {
        return $null
    }
}

function Save-LatestInstallerVersionCache {
    param([Parameter(Mandatory)] [string]$VersionValue)

    if ([string]::IsNullOrWhiteSpace($VersionValue)) {
        return
    }

    $cachePath = Resolve-LatestVersionCachePath -CreateRoot
    [ordered]@{
        version = $VersionValue
        refreshedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3 | Set-Content -Path $cachePath -Encoding UTF8
}

function Resolve-RequestedReleaseVersion {
    param([Parameter(Mandatory)] [string]$RequestedVersion)

    if ($RequestedVersion -ne 'latest') {
        return $RequestedVersion
    }

    if (-not [string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        return $script:ResolvedOnlineReleaseVersion
    }

    $script:ResolvedOnlineReleaseVersion = Get-LatestInstallerVersion
    if ([string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        throw 'Failed to resolve the latest WPF DevTools release version.'
    }

    return $script:ResolvedOnlineReleaseVersion
}

function ConvertTo-PowerShellEncodedCommand {
    param([Parameter(Mandatory)] [string]$CommandText)

    return [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($CommandText))
}

function Resolve-InstallerMode {
    if (-not [string]::IsNullOrWhiteSpace($PackageArchivePath)) { return 'offline' }
    if (-not [string]::IsNullOrWhiteSpace((Resolve-LocalPackageRoot))) { return 'offline' }
    return 'online'
}

function Resolve-PackageManifestPath {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    foreach ($candidate in @((Join-Path $PackageDirectory 'manifest.json'), (Join-Path $PackageDirectory 'bin\manifest.json'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "manifest.json was not found under package path: $PackageDirectory"
}

function Resolve-PackageExecutable {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    foreach ($candidate in @(
            (Join-Path $PackageDirectory "bin\wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory "wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory 'bin\WpfDevTools.Mcp.Server.exe'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Package does not contain an executable for architecture '$ResolvedArchitecture'."
}

function Resolve-PackageSession {
    param(
        [Parameter(Mandatory)] [string]$Mode,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
    $sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
    $extractRoot = Join-Path $sessionRoot 'package'

    if ($Mode -eq 'offline' -and -not [string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Expand-Archive -Path (Resolve-Path $PackageArchivePath).Path -DestinationPath $extractRoot -Force
        return [ordered]@{
            PackageDirectory = $extractRoot
            SessionRoot = $sessionRoot
            CleanupSession = $true
            DownloadSource = 'local-package'
            DownloadUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
            PackageAssetName = (Split-Path -Leaf $PackageArchivePath)
            ResolvedVersion = $ResolvedVersion
        }
    }

    if ($Mode -eq 'offline') {
        $localRoot = Resolve-LocalPackageRoot
        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        return [ordered]@{
            PackageDirectory = $localRoot
            SessionRoot = $null
            CleanupSession = $false
            DownloadSource = 'local-package'
            DownloadUri = $null
            PackageAssetName = $null
            ResolvedVersion = [string]$manifest.version
        }
    }

    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    $downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $ResolvedArchitecture
    $archivePath = Join-Path $workingRootPath ([string]$downloadDetails.AssetName)
    Invoke-WebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    return [ordered]@{
        PackageDirectory = $extractRoot
        SessionRoot = $sessionRoot
        CleanupSession = $true
        DownloadSource = 'github-release'
        DownloadUri = [string]$downloadDetails.DownloadUri
        PackageAssetName = [string]$downloadDetails.AssetName
        ResolvedVersion = [string]$downloadDetails.ResolvedVersion
    }
}

function Get-OfflineVersionHint {
    param([Parameter(Mandatory)] [string]$Mode)

    if ($Mode -ne 'offline') {
        return $null
    }

    try {
        $localRoot = Resolve-LocalPackageRoot
        if ([string]::IsNullOrWhiteSpace($localRoot)) {
            return $null
        }

        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        $localVersion = [string]$manifest.version
        if ([string]::IsNullOrWhiteSpace($localVersion)) {
            return $null
        }

        $latestVersion = Get-LatestInstallerVersion -UseCacheOnly
        if ([string]::IsNullOrWhiteSpace($latestVersion) -or $latestVersion -eq $localVersion) {
            return "Offline package version v$localVersion."
        }

        return "Offline package version v$localVersion. Latest release is v$latestVersion."
    }
    catch {
        return $null
    }
}

function Install-PackagePayload {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedVersion
    )

    $packageExecutable = Resolve-PackageExecutable -PackageDirectory $PackageDirectory -ResolvedArchitecture $ResolvedArchitecture
    $installRootFullPath = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
    $installBase = Join-Path $installRootFullPath $ResolvedArchitecture
    $currentDir = Join-Path $installBase 'current'
    $installManifestPath = Join-Path $installBase 'install-manifest.json'
    $reusedExistingBinary = $false

    if ((Test-Path $installManifestPath) -and -not $Force) {
        $existingManifest = Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
        if (($existingManifest.version -eq $ResolvedVersion) -and (Test-Path ([string]$existingManifest.executable))) {
            $reusedExistingBinary = $true
            New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable ([string]$existingManifest.executable)
            return [ordered]@{
                installRoot = $installRootFullPath
                installBase = $installBase
                installedExecutable = [string]$existingManifest.executable
                reusedExistingBinary = $reusedExistingBinary
            }
        }
    }

    Remove-PathIfExists -Path $currentDir
    New-Item -ItemType Directory -Force -Path $installBase | Out-Null
    New-Item -ItemType Directory -Force -Path $currentDir | Out-Null
    Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $currentDir -Recurse -Force
    Remove-PathIfExists -Path (Join-Path $currentDir 'run.bat')
    Remove-PathIfExists -Path (Join-Path $currentDir 'bin\install.ps1')

    $relativeExecutable = $packageExecutable.Substring($PackageDirectory.Length).TrimStart('\', '/')
    $installedExecutable = Join-Path $currentDir $relativeExecutable

    ([ordered]@{
            name = 'wpf-devtools'
            architecture = $ResolvedArchitecture
            version = $ResolvedVersion
            installRoot = $installRootFullPath
            installDir = $currentDir
            executable = $installedExecutable
            channel = [string]$PackageManifest.channel
            buildConfiguration = [string]$PackageManifest.buildConfiguration
            signaturePolicy = [string]$PackageManifest.signaturePolicy
            installedUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 5) | Set-Content -Path $installManifestPath -Encoding UTF8

    New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable $installedExecutable
    return [ordered]@{
        installRoot = $installRootFullPath
        installBase = $installBase
        installedExecutable = $installedExecutable
        reusedExistingBinary = $reusedExistingBinary
    }
}

function Update-InstallerStateAfterInstall {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] $Registration,
        [Parameter(Mandatory)] [string]$LastVerifiedUtc
    )

    $State.lastInstallRoot = $ResolvedInstallRoot
    $State.architectures[$ResolvedArchitecture] = [ordered]@{
        version = $ResolvedVersion
        executable = $InstalledExecutable
        installRoot = $ResolvedInstallRoot
    }
    $stateKey = Resolve-ClientStateKey -ClientId $SelectedClient -RegistrationMode ([string]$Registration.mode)
    $State.registrations[$stateKey] = [ordered]@{
        architecture = $ResolvedArchitecture
        installRoot = $ResolvedInstallRoot
        mode = [string]$Registration.mode
        target = [string]$Registration.target
        resolvedVersion = $ResolvedVersion
        installedExecutable = $InstalledExecutable
        lastVerifiedUtc = $LastVerifiedUtc
    }
}

function Get-AvailableInstallerUpdates {
    param(
        [Parameter(Mandatory)] $State,
        [string]$LatestVersion,
        $RegistrationMap
    )

    $updates = @()
    if ([string]::IsNullOrWhiteSpace($LatestVersion)) {
        return @($updates)
    }

    $candidateRegistrations = @()
    if ($null -ne $RegistrationMap) {
        $candidateRegistrations = @($RegistrationMap.GetEnumerator() | ForEach-Object {
                [ordered]@{
                    Client = [string]$_.Key
                    Registration = $_.Value
                }
            })
    }
    elseif ($null -ne (Get-Command 'Get-DetectedInstallerRegistrationMap' -CommandType Function -ErrorAction SilentlyContinue)) {
        $detectedRegistrationMap = Get-DetectedInstallerRegistrationMap -State $State
        $candidateRegistrations = @($detectedRegistrationMap.GetEnumerator() | ForEach-Object {
                [ordered]@{
                    Client = [string]$_.Key
                    Registration = $_.Value
                }
            })
    }
    else {
        $candidateRegistrations = @($State.registrations.GetEnumerator() | ForEach-Object {
                [ordered]@{
                    Client = [string]$_.Key
                    Registration = $_.Value
                }
            })
    }

    foreach ($entry in $candidateRegistrations) {
        $registration = $entry.Registration
        $resolvedVersion = if ($registration.Contains('ResolvedVersion')) { [string]$registration.ResolvedVersion } else { [string]$registration.resolvedVersion }
        if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
            continue
        }

        if ($resolvedVersion -ne $LatestVersion) {
            $installRoot = if ($registration.Contains('InstallRoot')) { [string]$registration.InstallRoot } else { [string]$registration.installRoot }
            $architecture = if ($registration.Contains('Architecture')) { [string]$registration.Architecture } else { [string]$registration.architecture }
            if ([string]::IsNullOrWhiteSpace($installRoot) -or [string]::IsNullOrWhiteSpace($architecture)) {
                continue
            }

            $updates += [ordered]@{
                Client = [string]$entry.Client
                CurrentVersion = $resolvedVersion
                LatestVersion = $LatestVersion
                InstallRoot = $installRoot
                Architecture = $architecture
            }
        }
    }

    return @($updates)
}

function Get-InstalledClientStatusMap {
    param([Parameter(Mandatory)] $State)

    $map = [ordered]@{}
    foreach ($client in Get-SupportedClients) {
        $isInstalled = $false
        if ($client.Id -eq 'cursor') {
            $isInstalled = @($State.registrations.Keys | Where-Object { $_ -like 'cursor-*' }).Count -gt 0
        }
        elseif ($State.registrations.Contains($client.Id)) {
            $isInstalled = $true
        }
        else {
            switch ($client.Id) {
                'cursor' {
                    $isInstalled = @(
                        Get-CursorVerificationConfigPaths -SelectedClient $client.Id
                    ).Where({
                            Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $_
                        }).Count -gt 0
                }
                'vscode' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) }
                'visual-studio' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) }
                'claude-desktop' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) }
            }
        }

        $map[$client.Id] = $isInstalled
    }

    return $map
}

function Test-JsonConfigRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $false
    }

    if (-not (Test-Path $ConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return $false
    }

    return (Test-InstallerPathEqualsCore -Left ([string]$servers['wpf-devtools'].command) -Right $InstalledExecutable)
}

function Test-ArtifactRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$ArtifactPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if (-not (Test-Path $ArtifactPath)) {
        return $false
    }

    $artifact = Get-Content -Path $ArtifactPath -Raw | ConvertFrom-Json
    $serverCollection = if ($null -ne $artifact.mcpServers) { $artifact.mcpServers } else { $artifact.servers }
    if ($null -eq $serverCollection -or $null -eq $serverCollection.'wpf-devtools') {
        return $false
    }

    return ([string]$serverCollection.'wpf-devtools'.command -eq $InstalledExecutable)
}

function Invoke-VerificationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ExpectedToken,
        [Parameter(Mandatory)] [bool]$ExpectPresent
    )

    $resolvedCommands = @(Get-Command $Command -All -CommandType Application,ExternalScript -ErrorAction SilentlyContinue)
    if ($resolvedCommands.Count -eq 0) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command is not installed."
            ExitCode = -1
        }
    }

    $selectedCommandPath = $null
    foreach ($resolvedCommand in $resolvedCommands) {
        $candidatePath = if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
            [string]$resolvedCommand.Path
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
            [string]$resolvedCommand.Source
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Definition)) {
            [string]$resolvedCommand.Definition
        }
        else {
            [string]$resolvedCommand.Name
        }

        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            $selectedCommandPath = $candidatePath
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($selectedCommandPath)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command is not installed."
            ExitCode = -1
        }
    }

    $timeoutSeconds = Get-InstallerVerificationTimeoutSeconds
    $quotedArguments = @($Arguments | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([string]$_)) {
                '""'
            }
            elseif ([string]$_ -match '[\s"]') {
                '"' + ([string]$_).Replace('"', '\"') + '"'
            }
            else {
                [string]$_
            }
        })

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $filePath = $selectedCommandPath
    $argumentText = $quotedArguments -join ' '
    $selectedExtension = [System.IO.Path]::GetExtension($selectedCommandPath).ToLowerInvariant()
    if (@('.cmd', '.bat') -contains $selectedExtension) {
        $filePath = if (-not [string]::IsNullOrWhiteSpace($env:ComSpec)) { $env:ComSpec } else { 'cmd.exe' }
        $argumentText = '/c "' + $selectedCommandPath + '"'
        if (-not [string]::IsNullOrWhiteSpace($argumentText) -and $quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }
    elseif ($selectedExtension -eq '.ps1') {
        $filePath = (Get-Process -Id $PID).Path
        $argumentText = '-NoProfile -ExecutionPolicy Bypass -File "' + $selectedCommandPath + '"'
        if ($quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }

    $process = $null
    $exitCode = -3
    try {
        $startInfo.FileName = $filePath
        $startInfo.Arguments = $argumentText
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        $null = $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
            }
            catch {
                try {
                    & taskkill.exe /PID $process.Id /T /F *> $null
                }
                catch {
                    try {
                        $process.Kill()
                    }
                    catch {
                    }
                }
            }

            $timeoutDrainMs = 250
            try {
                $null = $process.WaitForExit($timeoutDrainMs)
            }
            catch {
            }

            $timeoutOutput = @()
            if ($stdoutTask.IsCompleted) {
                $timeoutOutput += $stdoutTask.GetAwaiter().GetResult()
            }
            if ($stderrTask.IsCompleted) {
                $timeoutOutput += $stderrTask.GetAwaiter().GetResult()
            }
            $timeoutOutput = ($timeoutOutput -join [Environment]::NewLine).Trim()

            return [ordered]@{
                Succeeded = $false
                Output = ("$Command timed out after $timeoutSeconds second(s). " + $timeoutOutput).Trim()
                ExitCode = -2
            }
        }

        $exitCode = $process.ExitCode
    }
    catch {
        return [ordered]@{
            Succeeded = $false
            Output = $_.Exception.Message
            ExitCode = -3
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }

    $output = @(
        $stdoutTask.GetAwaiter().GetResult()
        $stderrTask.GetAwaiter().GetResult()
    ) -join [Environment]::NewLine
    $output = $output.Trim()
    if ($exitCode -ne 0) {
        return [ordered]@{
            Succeeded = $false
            Output = $output
            ExitCode = $exitCode
        }
    }

    $containsToken = if ([string]::IsNullOrWhiteSpace($output)) { $ExpectPresent } else { $output.Contains($ExpectedToken) }
    return [ordered]@{
        Succeeded = ($containsToken -eq $ExpectPresent)
        Output = $output
        ExitCode = $exitCode
    }
}

function Read-ValidatedChoice {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue,
        [Parameter(Mandatory)] [string[]]$AllowedValues
    )

    while ($true) {
        $response = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        $normalized = $response.Trim().ToLowerInvariant()
        if ($AllowedValues -contains $normalized) {
            return $normalized
        }

        Write-InstallerMessage ("Allowed values: " + ($AllowedValues -join ', '))
    }
}

function Get-CliSelection {
    $defaultAction = $Action
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
    $defaultInstallRoot = Resolve-PreferredInstallRoot

    if ($NonInteractive -or $OutputJson) {
        return [ordered]@{
            Action = $defaultAction
            Architecture = $defaultArchitecture
            Client = $defaultClient
            InstallRoot = $defaultInstallRoot
        }
    }

    $resolvedAction = Read-ValidatedChoice -Prompt 'Action (install/uninstall)' -DefaultValue $defaultAction -AllowedValues @('install', 'uninstall', 'full-uninstall')
    $resolvedArchitecture = Read-ValidatedChoice -Prompt 'Architecture (x64/x86/arm64)' -DefaultValue $defaultArchitecture -AllowedValues @('x64', 'x86', 'arm64')
    $resolvedClient = Read-ValidatedChoice -Prompt 'Client (claude-code/codex/cursor/vscode/visual-studio/claude-desktop/other)' -DefaultValue $defaultClient -AllowedValues @('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')
    $installRootPrompt = Read-InstallerInput -Prompt 'Install root' -DefaultValue $defaultInstallRoot
    if ([string]::IsNullOrWhiteSpace($installRootPrompt)) {
        $installRootPrompt = $defaultInstallRoot
    }

    return [ordered]@{
        Action = $resolvedAction
        Architecture = $resolvedArchitecture
        Client = $resolvedClient
        InstallRoot = $installRootPrompt.Trim()
    }
}

function Get-InstalledClientLabel {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] $State
    )

    $clientLabel = Resolve-ClientLabel -ClientId $ClientId
    if (-not $State.registrations.Contains($ClientId)) {
        return $clientLabel
    }

    $resolvedVersion = [string]$State.registrations[$ClientId].resolvedVersion
    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        return "$clientLabel (Installed)"
    }

    return "$clientLabel (Installed v$resolvedVersion)"
}

function Invoke-InstallVerification {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] $Registration
    )

    $verificationSucceeded = $false
    $verificationMessage = $null
    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    switch ($clientBaseId) {
        'claude-code' {
            $verification = Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
            $verificationSucceeded = ($verification.Succeeded -and (Test-Path $InstalledExecutable))
            $verificationMessage = if ($verificationSucceeded) { 'Verified with claude mcp list.' } else { "Claude verification failed: $($verification.Output)" }
        }
        'codex' {
            $verification = Invoke-VerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
            $verificationSucceeded = ($verification.Succeeded -and (Test-Path $InstalledExecutable))
            $verificationMessage = if ($verificationSucceeded) { 'Verified with codex mcp list.' } else { "Codex verification failed: $($verification.Output)" }
        }
        'cursor' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'mcpServers' -ConfigPath ([string]$Registration.target) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified Cursor configuration.' } else { 'Cursor verification failed.' }
        }
        'vscode' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified VS Code configuration.' } else { 'VS Code verification failed.' }
        }
        'visual-studio' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified Visual Studio configuration.' } else { 'Visual Studio verification failed.' }
        }
        'claude-desktop' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified Claude Desktop configuration.' } else { 'Claude Desktop verification failed.' }
        }
        'other' {
            $verificationSucceeded = Test-ArtifactRegistrationMatchesExecutable -ArtifactPath ([string]$Registration.target) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified exported registration artifact.' } else { 'Artifact verification failed.' }
        }
    }

    return [ordered]@{
        Succeeded = $verificationSucceeded
        InstalledVersion = $ResolvedVersion
        VerificationMessage = $verificationMessage
        LastVerifiedUtc = [DateTime]::UtcNow.ToString('o')
    }
}

function Invoke-UninstallVerification {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $verificationSucceeded = switch ($clientBaseId) {
        'claude-code' {
            (Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'codex' {
            (Invoke-VerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'cursor' {
            @(
                Get-CursorVerificationConfigPaths -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
            ).Where({
                    Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $_
                }).Count -eq 0
            break
        }
        'vscode' { -not (Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath)); break }
        'visual-studio' { -not (Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath)); break }
        'claude-desktop' { -not (Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath)); break }
        default { $true }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}

function Get-LatestInstallerVersion {
    param([switch]$UseCacheOnly)

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION)) {
        return $env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION
    }

    $cachedVersion = Get-CachedLatestInstallerVersion
    if ($UseCacheOnly) {
        return $cachedVersion
    }

    try {
        $latestVersion = [string](Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion 'latest') -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
        if (-not [string]::IsNullOrWhiteSpace($latestVersion)) {
            Save-LatestInstallerVersionCache -VersionValue $latestVersion
            return $latestVersion
        }
    }
    catch {
    }

    return $cachedVersion
}

function Start-LatestInstallerVersionRefresh {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION)) {
        return [ordered]@{
            Mode = 'test'
            Version = $env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION
        }
    }

    $refreshDirectory = Resolve-AbsoluteDirectory -Path (Join-Path $env:TEMP 'wpf-devtools-online-installer\latest-version-refresh')
    $refreshOutputPath = Join-Path $refreshDirectory ("latest-version-" + [Guid]::NewGuid().ToString('N') + '.json')
    $releaseApiUri = Get-GitHubReleaseApiUri -ResolvedVersion 'latest'
    $encodedCommand = ConvertTo-PowerShellEncodedCommand -CommandText @"
\$ProgressPreference = 'SilentlyContinue'
try {
    \$latestVersion = [string](Invoke-RestMethod -Uri '$releaseApiUri' -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
    if (-not [string]::IsNullOrWhiteSpace(\$latestVersion)) {
        [ordered]@{ version = \$latestVersion } | ConvertTo-Json -Depth 3 | Set-Content -Path '$refreshOutputPath' -Encoding UTF8
    }
}
catch {
}
"@

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = (Get-Process -Id $PID).Path
    $process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $false
    $process.StartInfo.RedirectStandardError = $false
    $process.StartInfo.CreateNoWindow = $true
    $null = $process.Start()

    return [ordered]@{
        Mode = 'process'
        Process = $process
        OutputPath = $refreshOutputPath
    }
}

function Receive-LatestInstallerVersionRefresh {
    param([Parameter(Mandatory)] $RefreshHandle)

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return [ordered]@{
            IsCompleted = $true
            Version = [string]$RefreshHandle.Version
        }
    }

    $process = $RefreshHandle.Process
    if ($null -eq $process) {
        return [ordered]@{
            IsCompleted = $true
            Version = $null
        }
    }

    if (-not $process.HasExited) {
        return [ordered]@{
            IsCompleted = $false
            Version = $null
        }
    }

    $resolvedVersion = $null
    if (Test-Path ([string]$RefreshHandle.OutputPath)) {
        try {
            $parsed = Get-Content -Path ([string]$RefreshHandle.OutputPath) -Raw | ConvertFrom-Json
            $resolvedVersion = [string]$parsed.version
        }
        catch {
        }

        Remove-PathIfExists -Path ([string]$RefreshHandle.OutputPath)
    }

    try {
        $process.Dispose()
    }
    catch {
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
        Save-LatestInstallerVersionCache -VersionValue $resolvedVersion
    }

    return [ordered]@{
        IsCompleted = $true
        Version = $resolvedVersion
    }
}

function Stop-LatestInstallerVersionRefresh {
    param($RefreshHandle)

    if ($null -eq $RefreshHandle) {
        return
    }

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return
    }

    $process = $RefreshHandle.Process
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            try {
                $process.Kill($true)
            }
            catch {
                try {
                    $process.Kill()
                }
                catch {
                }
            }

            try {
                $null = $process.WaitForExit(250)
            }
            catch {
            }
        }

        try {
            $process.Dispose()
        }
        catch {
        }
    }

    Remove-PathIfExists -Path ([string]$RefreshHandle.OutputPath)
}

function Test-TuiSupport {
    if ($NonInteractive -or $OutputJson) {
        Close-TuiBootstrapScreen
        return $false
    }

    $script:LastTuiBootstrapMessage = $null
    $script:LastTuiBootstrapFailureReason = $null
    try {
        Write-TuiBootstrapScreen 'Preparing installer UI...' | Out-Host
        $null = Ensure-TuiHelpersAvailable
    }
    catch {
        $script:LastTuiBootstrapFailureReason = $_.Exception.Message
        Write-TuiBootstrapScreen 'Preparing installer UI... (fallback)' | Out-Host
        Close-TuiBootstrapScreen
        Write-InstallerMessage 'Installer UI bootstrap failed. Falling back to plain CLI.'
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        Close-TuiBootstrapScreen
        return $false
    }

    return (Invoke-WithTuiHelpers -ScriptBlock { Test-TuiSupportCore })
}

function Render-TuiScreen {
    param([Parameter(Mandatory)] $State)

    Invoke-WithTuiHelpers -ScriptBlock { Render-TuiScreenCore -State $State } | Out-Null
}

function Read-TuiKey {
    return (Invoke-WithTuiHelpers -ScriptBlock { Read-TuiKeyCore })
}

function Update-TuiSelection {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $KeyInfo
    )

    return (Invoke-WithTuiHelpers -ScriptBlock { Update-TuiSelectionCore -State $State -KeyInfo $KeyInfo })
}

function Invoke-TuiInstallOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiInstallOperationCore -State $State })
}

function Invoke-TuiUninstallOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiUninstallOperationCore -State $State })
}

function Invoke-TuiUpdateAllOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiUpdateAllOperationCore -State $State })
}

function Initialize-TuiStartupState {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Initialize-TuiStartupStateCore -State $State })
}

function Assert-InstallerHelperRuntimeAvailable {
    param([Parameter(Mandatory)] [string]$ResolvedAction)

    if ($ResolvedAction -eq 'install') {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($script:LastTuiBootstrapFailureReason)) {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package. $script:LastTuiBootstrapFailureReason"
    }

    try {
        $helperRoot = Ensure-TuiHelpersAvailable
    }
    catch {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package. $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package."
    }
}

function Invoke-InstallerAction {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction

    if ($ResolvedAction -eq 'full-uninstall') {
        $state = Get-InstallerState
        $result = Invoke-WithTuiHelpers -ScriptBlock { Invoke-InstallerFullUninstallCore -State $state }
        return [ordered]@{
            action = 'full-uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = 'all'
            client = 'all'
            packageAssetName = $null
            downloadUri = $null
            installRoot = $null
            installedExecutable = $null
            selectedClients = @()
            statePath = [string]$result.statePath
            removedInstallation = [bool]$result.removedInstallation
            removedInstallations = @($result.removedInstallations)
            registrations = @($result.registrations)
            verificationMessage = [string]$result.verificationMessage
        }
    }

    if ($ResolvedAction -eq 'uninstall') {
        $state = Get-InstallerState
        $detectedRegistrations = Invoke-WithTuiHelpers -ScriptBlock { Get-DetectedInstallerRegistrationMap -State $state }
        $cursorRegistrationMode = $null
        if ((Resolve-ClientBaseId -ClientId $ResolvedClient) -eq 'cursor') {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $ResolvedClient
            $cursorRegistrationMode = "cursor-$([string]$cursorProfile.Mode)"
        }

        $requestedStateKey = Resolve-ClientStateKey -ClientId $ResolvedClient -RegistrationMode $cursorRegistrationMode
        $detectedRegistration = if ($detectedRegistrations.Contains($requestedStateKey)) {
            $detectedRegistrations[$requestedStateKey]
        }
        elseif ($detectedRegistrations.Contains($ResolvedClient)) {
            $detectedRegistrations[$ResolvedClient]
        }
        elseif ($ResolvedClient -eq 'cursor') {
            ($detectedRegistrations.GetEnumerator() | Where-Object { $_.Key -like 'cursor-*' } | Select-Object -ExpandProperty Value | Select-Object -First 1)
        }
        else {
            $null
        }

        $stateRegistrationKey = if ($state.registrations.Contains($requestedStateKey)) {
            $requestedStateKey
        }
        elseif ($state.registrations.Contains($ResolvedClient)) {
            $ResolvedClient
        }
        elseif ($null -ne $detectedRegistration) {
            Resolve-ClientStateKey -ClientId ([string]$detectedRegistration.ClientId) -RegistrationMode ([string]$detectedRegistration.RegistrationMode)
        }
        else {
            $null
        }

        $registrationRecord = if (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
            $state.registrations[$stateRegistrationKey]
        }
        else {
            $detectedRegistration
        }
        if ($null -ne $registrationRecord) {
            if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
                $ResolvedArchitecture = if ($registrationRecord.Contains('architecture')) { [string]$registrationRecord.architecture } else { [string]$registrationRecord.Architecture }
            }
            if (-not $script:InstallRootWasSpecified -and [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
                $ResolvedInstallRoot = if ($registrationRecord.Contains('installRoot')) { [string]$registrationRecord.installRoot } else { [string]$registrationRecord.InstallRoot }
            }
        }

        if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
            $ResolvedArchitecture = Get-SystemDefaultArchitecture
        }
        if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
            $ResolvedInstallRoot = Resolve-PreferredInstallRoot
        }

        $ResolvedInstallRoot = Resolve-AbsolutePath -Path $ResolvedInstallRoot
        $installedExecutable = if ($null -ne $detectedRegistration) { [string]$detectedRegistration.InstalledExecutable } else { $null }
        $registrations = @(Invoke-ClientUnregistration -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord)
        $verification = Invoke-UninstallVerification -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        if (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
            [void]$state.registrations.Remove($stateRegistrationKey)
        }

        $statePath = Resolve-InstallerStatePath
        if ((Test-Path $statePath) -or (Test-InstallerStateHasData -State $state)) {
            $statePath = Save-InstallerState -State $state
        }
        else {
            $statePath = $null
        }
        return [ordered]@{
            action = 'uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = $ResolvedArchitecture
            client = $ResolvedClient
            packageAssetName = $null
            downloadUri = $null
            installRoot = $ResolvedInstallRoot
            installedExecutable = $installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            removedInstallation = $false
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
        }
    }

    $mode = if ($UseLatestRelease) { 'online' } else { Resolve-InstallerMode }
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $RequestedVersion -ResolvedArchitecture $ResolvedArchitecture
    try {
        $packageManifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $session.PackageDirectory) -Raw | ConvertFrom-Json
        $resolvedVersion = if ([string]::IsNullOrWhiteSpace([string]$packageManifest.version)) { [string]$session.ResolvedVersion } else { [string]$packageManifest.version }
        $installResult = Install-PackagePayload -PackageDirectory $session.PackageDirectory -PackageManifest $packageManifest -ResolvedArchitecture $ResolvedArchitecture -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedVersion $resolvedVersion
        $registrations = @(Invoke-ClientRegistration -SelectedClient $ResolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        $verification = Invoke-InstallVerification -SelectedClient $ResolvedClient -ResolvedVersion $resolvedVersion -InstalledExecutable $installResult.installedExecutable -Registration $registrations[0]
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        $state = Get-InstallerState
        Update-InstallerStateAfterInstall -State $state -ResolvedInstallRoot $installResult.installRoot -ResolvedArchitecture $ResolvedArchitecture -ResolvedVersion ([string]$verification.InstalledVersion) -InstalledExecutable $installResult.installedExecutable -SelectedClient $ResolvedClient -Registration $registrations[0] -LastVerifiedUtc ([string]$verification.LastVerifiedUtc)
        $statePath = Save-InstallerState -State $state
        return [ordered]@{
            action = 'install'
            mode = $mode
            downloadSource = [string]$session.DownloadSource
            version = $RequestedVersion
            resolvedVersion = [string]$verification.InstalledVersion
            architecture = $ResolvedArchitecture
            client = $ResolvedClient
            packageAssetName = [string]$session.PackageAssetName
            downloadUri = [string]$session.DownloadUri
            installRoot = $installResult.installRoot
            installedExecutable = $installResult.installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            reusedExistingBinary = [bool]$installResult.reusedExistingBinary
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
            lastVerifiedUtc = [string]$verification.LastVerifiedUtc
        }
    }
    finally {
        if ($session.CleanupSession) {
            Remove-PathIfExists -Path $session.SessionRoot
        }
    }
}

function Start-TuiInstaller {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$VersionHint,
        [string]$LatestVersion
    )

    $global:WpfDevToolsInstallerBootstrapSession = $script:TuiBootstrapTerminalSession
    try {
        return (Invoke-WithTuiHelpers -ScriptBlock { Start-TuiInstallerCore `
                -DefaultAction $DefaultAction `
                -DefaultArchitecture $DefaultArchitecture `
                -DefaultClient $DefaultClient `
                -DefaultInstallRoot $DefaultInstallRoot `
                -InstallerState $InstallerState `
                -VersionHint $VersionHint `
                -LatestVersion $LatestVersion })
    }
    finally {
        if ($null -ne $global:WpfDevToolsInstallerBootstrapSession) {
            $script:TuiBootstrapTerminalSession = $global:WpfDevToolsInstallerBootstrapSession
            Close-TuiBootstrapScreen
        }

        $script:TuiBootstrapTerminalSession = $null
        Remove-Variable -Name WpfDevToolsInstallerBootstrapSession -Scope Global -ErrorAction SilentlyContinue
    }
}

function Resolve-Selection {
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

    if (Test-TuiSupport) {
        $installerState = Get-InstallerState
        $defaultInstallRoot = Resolve-PreferredInstallRoot
        $mode = Resolve-InstallerMode
        $versionHint = Get-OfflineVersionHint -Mode $mode
        $latestVersion = Get-LatestInstallerVersion -UseCacheOnly
        $tuiResult = Start-TuiInstaller `
            -DefaultAction $Action `
            -DefaultArchitecture $defaultArchitecture `
            -DefaultClient $defaultClient `
            -DefaultInstallRoot $defaultInstallRoot `
            -InstallerState $installerState `
            -VersionHint $versionHint `
            -LatestVersion $latestVersion

        return [ordered]@{
            Cancelled = [bool]$tuiResult.Cancelled
            Selection = $tuiResult.Selection
            VersionHint = $versionHint
            HandledInWindow = [bool]$tuiResult.HandledInWindow
        }
    }

    Close-TuiBootstrapScreen
    $mode = Resolve-InstallerMode
    $versionHint = Get-OfflineVersionHint -Mode $mode
    return [ordered]@{
        Cancelled = $false
        Selection = (Get-CliSelection)
        VersionHint = $versionHint
        HandledInWindow = $false
    }
}

function Resolve-InstallBasePath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return (Join-Path (Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot) $ResolvedArchitecture)
}

function Get-StandardInstallJson {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $installBase = Resolve-InstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$ResolvedArchitecture.exe"
    $serverNode = [ordered]@{
        type = 'stdio'
        command = $installedExecutable
        args = @()
    }

    return ([ordered]@{
            mcpServers = [ordered]@{
                'wpf-devtools' = $serverNode
            }
        } | ConvertTo-Json -Depth 5)
}

function Get-RegistrationsForArchitecture {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [string]$ExcludeClient
    )

    $remaining = @()
    foreach ($property in $State.registrations.GetEnumerator()) {
        if ($property.Key -eq $ExcludeClient) {
            continue
        }

        if (($property.Value.architecture -eq $ResolvedArchitecture) -and ($property.Value.installRoot -eq $ResolvedInstallRoot)) {
            $remaining += $property.Key
        }
    }

    return @($remaining)
}

$selectionContext = Resolve-Selection
if ($selectionContext.Cancelled) {
    return
}
if ($selectionContext.HandledInWindow) {
    return
}

$interactiveSelection = $selectionContext.Selection
$resolvedAction = [string]$interactiveSelection.Action
$resolvedArchitecture = [string]$interactiveSelection.Architecture
$resolvedClient = [string]$interactiveSelection.Client
$resolvedInstallRoot = [string]$interactiveSelection.InstallRoot
$versionHint = [string]$selectionContext.VersionHint

$result = Invoke-InstallerAction -ResolvedAction $resolvedAction -ResolvedArchitecture $resolvedArchitecture -ResolvedClient $resolvedClient -ResolvedInstallRoot $resolvedInstallRoot -RequestedVersion $Version

if ($OutputJson) {
    $result | ConvertTo-Json -Depth 10
}
else {
    Write-InstallerMessage "$($result.action) finished for $($result.client)."
    if (-not [string]::IsNullOrWhiteSpace($result.installedExecutable)) {
        Write-InstallerMessage "Executable: $($result.installedExecutable)"
    }
    if (-not [string]::IsNullOrWhiteSpace($result.verificationMessage)) {
        Write-InstallerMessage $result.verificationMessage
    }
    if (-not [string]::IsNullOrWhiteSpace($versionHint)) {
        Write-InstallerMessage $versionHint
    }
}
