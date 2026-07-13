function Get-CliSelection {
    $defaultInstallRoot = $InstallRoot
    try {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
            . $helperPath
        }

        $defaultInstallRoot = Resolve-PreferredInstallRoot
    }
    catch {
    }

    $defaultAction = $Action
    $defaultVersion = $Version
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

    if ($NonInteractive -or $OutputJson) {
        $selectedInstallRoot = $defaultInstallRoot
        if (-not $script:InstallRootWasSpecified -and $defaultAction -ne 'install') {
            $selectedInstallRoot = $null
        }

        return [ordered]@{
            Action = $defaultAction
            Version = $defaultVersion
            Architecture = $defaultArchitecture
            Client = $defaultClient
            InstallRoot = $selectedInstallRoot
        }
    }

    $resolvedAction = Read-ValidatedChoice -Prompt 'Action (install/uninstall)' -DefaultValue $defaultAction -AllowedValues @('install', 'uninstall', 'full-uninstall')
    $resolvedVersion = if ($resolvedAction -eq 'install' -and -not $script:InteractiveReleaseVersionWasPrompted) {
        Read-ValidatedVersion -Prompt 'Release version' -DefaultValue $defaultVersion
    }
    else {
        $defaultVersion
    }
    $resolvedArchitecture = Read-ValidatedChoice -Prompt 'Architecture (x64/x86/arm64)' -DefaultValue $defaultArchitecture -AllowedValues @('x64', 'x86', 'arm64')
    $resolvedClient = Read-ValidatedChoice -Prompt 'Client (claude-code/codex/grok/cursor/vscode/visual-studio/claude-desktop/other)' -DefaultValue $defaultClient -AllowedValues @('claude-code', 'codex', 'grok', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')
    $installRootPrompt = Read-InstallerInput -Prompt 'Install root' -DefaultValue $defaultInstallRoot
    if ([string]::IsNullOrWhiteSpace($installRootPrompt)) {
        $installRootPrompt = $defaultInstallRoot
    }

    return [ordered]@{
        Action = $resolvedAction
        Version = $resolvedVersion
        Architecture = $resolvedArchitecture
        Client = $resolvedClient
        InstallRoot = $installRootPrompt.Trim()
    }
}
function Get-LatestInstallerVersion {
    param([switch]$UseCacheOnly)

    $releaseChannel = Get-InstallerReleaseChannel
    if ($releaseChannel -eq 'prerelease' -and -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_PRERELEASE_VERSION)) {
        return $env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_PRERELEASE_VERSION
    }

    if ($releaseChannel -eq 'stable' -and -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION)) {
        return $env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION
    }

    $cachedVersion = Get-CachedLatestInstallerVersion -ReleaseChannel $releaseChannel
    if ($UseCacheOnly) {
        return $cachedVersion
    }

    try {
        $latestVersion = if ($releaseChannel -eq 'prerelease') {
            Select-LatestInstallerPrereleaseVersion -Releases (Invoke-RestMethod -Uri (Get-GitHubReleaseListApiUri) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10)
        }
        else {
            [string](Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion 'latest') -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
        }

        if (-not [string]::IsNullOrWhiteSpace($latestVersion)) {
            Save-LatestInstallerVersionCache -VersionValue $latestVersion -ReleaseChannel $releaseChannel
            return $latestVersion
        }
    }
    catch {
    }

    return $cachedVersion
}
function Start-LatestInstallerVersionRefresh {
    $releaseChannel = Get-InstallerReleaseChannel
    if ($releaseChannel -eq 'prerelease' -and -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_PRERELEASE_VERSION)) {
        return [ordered]@{
            Mode = 'test'
            Version = $env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_PRERELEASE_VERSION
        }
    }

    if ($releaseChannel -eq 'stable' -and -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION)) {
        return [ordered]@{
            Mode = 'test'
            Version = $env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION
        }
    }

    $refreshDirectory = Resolve-AbsoluteDirectory -Path (Join-Path $env:TEMP 'wpf-devtools-online-installer\latest-version-refresh')
    Assert-InstallerLocalPathTrusted -Path $refreshDirectory | Out-Null
    $releaseApiUri = if ($releaseChannel -eq 'prerelease') { Get-GitHubReleaseListApiUri } else { Get-GitHubReleaseApiUri -ResolvedVersion 'latest' }
    $escapedReleaseApiUri = ConvertTo-SingleQuotedPowerShellLiteral -Value $releaseApiUri
    $prereleaseLiteral = if ($releaseChannel -eq 'prerelease') { '$true' } else { '$false' }
    $encodedCommand = ConvertTo-PowerShellEncodedCommand -CommandText @"
\$ProgressPreference = 'SilentlyContinue'
try {
    \$latestVersion = \$null
    if ($prereleaseLiteral) {
        \$releases = @(Invoke-RestMethod -Uri '$escapedReleaseApiUri' -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10)
        foreach (\$release in \$releases) {
            if (\$null -eq \$release) { continue }
            \$propertyNames = @(\$release.PSObject.Properties.Name)
            \$isDraft = \$propertyNames -contains 'draft' -and [bool]\$release.draft
            \$isPrerelease = \$propertyNames -contains 'prerelease' -and [bool]\$release.prerelease
            \$tagName = if (\$propertyNames -contains 'tag_name') { [string]\$release.tag_name } else { \$null }
            if (-not \$isDraft -and \$isPrerelease -and -not [string]::IsNullOrWhiteSpace(\$tagName)) {
                \$latestVersion = \$tagName.TrimStart('v', 'V')
                break
            }
        }
    }
    else {
        \$latestVersion = [string](Invoke-RestMethod -Uri '$escapedReleaseApiUri' -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
    }
    if (-not [string]::IsNullOrWhiteSpace(\$latestVersion)) {
        [ordered]@{ version = \$latestVersion; error = \$null; exitCode = 0 } | ConvertTo-Json -Depth 3 -Compress
        exit 0
    }
    [ordered]@{ version = \$null; error = 'Latest release metadata did not return a tag_name.'; exitCode = 2 } | ConvertTo-Json -Depth 3 -Compress
    exit 2
}
catch {
    [ordered]@{ version = \$null; error = [string]\$_.Exception.Message; exitCode = 1 } | ConvertTo-Json -Depth 3 -Compress
    exit 1
}
"@

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = (Get-Process -Id $PID).Path
    $process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    $null = $process.Start()

    return [ordered]@{
        Mode = 'process'
        Process = $process
    }
}
function Receive-LatestInstallerVersionRefresh {
    param([Parameter(Mandatory)] $RefreshHandle)

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return [ordered]@{
            IsCompleted = $true
            Version = [string]$RefreshHandle.Version
            ErrorMessage = $null
            ExitCode = 0
        }
    }

    $process = $RefreshHandle.Process
    if ($null -eq $process) {
        return [ordered]@{
            IsCompleted = $true
            Version = $null
            ErrorMessage = $null
            ExitCode = 0
        }
    }

    if (-not $process.HasExited) {
        return [ordered]@{
            IsCompleted = $false
            Version = $null
            ErrorMessage = $null
            ExitCode = $null
        }
    }

    $resolvedVersion = $null
    $errorMessage = $null
    $exitCode = $process.ExitCode
    try {
        $outputJson = [string]$process.StandardOutput.ReadToEnd()
        $standardError = [string]$process.StandardError.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($outputJson)) {
            $parsed = $outputJson | ConvertFrom-Json
            $resolvedVersion = [string]$parsed.version
            $errorMessage = [string]$parsed.error
            if ($parsed.PSObject.Properties.Name -contains 'exitCode') {
                $exitCode = [int]$parsed.exitCode
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($standardError)) {
            $errorMessage = $standardError.Trim()
        }
    }
    catch {
        $errorMessage = [string]$_.Exception.Message
    }

    try {
        $process.Dispose()
    }
    catch {
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
        Save-LatestInstallerVersionCache -VersionValue $resolvedVersion -ReleaseChannel (Get-InstallerReleaseChannel)
    }
    elseif ([string]::IsNullOrWhiteSpace($errorMessage) -and $exitCode -ne 0) {
        $errorMessage = "Background metadata refresh exited with code $exitCode."
    }

    return [ordered]@{
        IsCompleted = $true
        Version = $resolvedVersion
        ErrorMessage = $errorMessage
        ExitCode = $exitCode
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
        if ((Resolve-InstallerMode) -eq 'offline') {
            Close-TuiBootstrapScreen
            throw "The installer runtime bundled with this package failed integrity or bootstrap validation. $script:LastTuiBootstrapFailureReason"
        }

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

    if ($NonInteractive -or $OutputJson) {
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
function Add-InstallerReleaseChannelToResult {
    param($Result)

    if ($null -eq $Result) {
        return $Result
    }

    $releaseChannel = Resolve-InstallerResultReleaseChannel -Result $Result
    if ($Result -is [System.Collections.IDictionary]) {
        $Result['releaseChannel'] = $releaseChannel
        return $Result
    }

    $Result | Add-Member -NotePropertyName 'releaseChannel' -NotePropertyValue $releaseChannel -Force
    return $Result
}
function Resolve-InstallerResultReleaseChannel {
    param($Result)

    $reportedReleaseChannel = Get-InstallerResultStringValue -Result $Result -PropertyName 'releaseChannel'
    if ($reportedReleaseChannel -in @('stable', 'prerelease', 'mixed')) {
        return $reportedReleaseChannel
    }

    $releaseChannel = Get-InstallerReleaseChannel
    if ($releaseChannel -eq 'prerelease') {
        return $releaseChannel
    }

    foreach ($propertyName in @('resolvedVersion', 'ResolvedVersion', 'sourceReleaseVersion', 'SourceReleaseVersion', 'version', 'Version')) {
        if (Test-InstallerPrereleaseVersion -VersionValue (Get-InstallerResultStringValue -Result $Result -PropertyName $propertyName)) {
            return 'prerelease'
        }
    }

    foreach ($propertyName in @('packageAssetName', 'PackageAssetName', 'downloadUri', 'DownloadUri')) {
        if (Test-InstallerPrereleaseArtifactText -Value (Get-InstallerResultStringValue -Result $Result -PropertyName $propertyName)) {
            return 'prerelease'
        }
    }

    return $releaseChannel
}
function Get-InstallerResultStringValue {
    param(
        $Result,
        [Parameter(Mandatory)] [string]$PropertyName
    )

    if ($null -eq $Result) {
        return $null
    }

    if ($Result -is [System.Collections.IDictionary]) {
        if ($Result.Contains($PropertyName) -and $null -ne $Result[$PropertyName]) {
            return [string]$Result[$PropertyName]
        }

        return $null
    }

    $property = $Result.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $null
    }

    return [string]$property.Value
}
