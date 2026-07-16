function Resolve-StandaloneExecutableCommandPath {
    param([Parameter(Mandatory)] [string]$Command)

    if (Test-StandaloneInstallerRunningElevated) {
        return $null
    }

    $resolvedCommand = Get-Command $Command -CommandType Application,ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $resolvedCommand) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
        return [string]$resolvedCommand.Path
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
        return [string]$resolvedCommand.Source
    }

    return $null
}
function Resolve-StandaloneCliCommandName {
    param([Parameter(Mandatory)] [string]$ClientBaseId)

    switch ($ClientBaseId) {
        'claude-code' { return 'claude' }
        'codex' { return 'codex' }
        'grok' { return 'grok' }
        default { return $null }
    }
}
function Get-StandaloneCliAddArguments {
    param(
        [Parameter(Mandatory)] [string]$ClientBaseId,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if ($ClientBaseId -eq 'grok') {
        return @('mcp', 'add', '--scope', 'user', 'wpf-devtools', '--', $InstalledExecutable)
    }

    return @('mcp', 'add', 'wpf-devtools', '--', $InstalledExecutable)
}
function Get-StandaloneCliRemoveArguments {
    param([Parameter(Mandatory)] [string]$ClientBaseId)

    if ($ClientBaseId -eq 'grok') {
        return @('mcp', 'remove', '--scope', 'user', 'wpf-devtools')
    }

    return @('mcp', 'remove', 'wpf-devtools')
}
function Format-StandaloneCommandArgument {
    param([AllowNull()] [string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    $argument = [string]$Value
    if ($argument.Length -eq 0) {
        return '""'
    }

    if ($argument -match '[\s"]') {
        return '"' + $argument.Replace('"', '\"') + '"'
    }

    return $argument
}
function Invoke-StandaloneVerificationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ExpectedToken,
        [Parameter(Mandatory)] [bool]$ExpectPresent,
        [string]$WorkingDirectory
    )

    $isElevated = Test-StandaloneInstallerRunningElevated
    $selectedCommandPath = $null
    try {
        $selectedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $Command
    }
    catch {
        return [ordered]@{
            Succeeded = $false
            Output = $_.Exception.Message
            ExitCode = -1
        }
    }

    if ([string]::IsNullOrWhiteSpace($selectedCommandPath)) {
        return [ordered]@{
            Succeeded = $false
            Output = if ($isElevated) { "Automatic $Command verification is blocked while the installer is elevated because resolving '$Command' from PATH is unsafe." } else { "$Command is not installed." }
            ExitCode = -1
        }
    }

    $quotedArguments = @($Arguments | ForEach-Object { Format-StandaloneCommandArgument -Value ([string]$_) })
    $filePath = $selectedCommandPath
    $argumentText = $quotedArguments -join ' '
    $selectedExtension = [System.IO.Path]::GetExtension($selectedCommandPath).ToLowerInvariant()
    if (@('.cmd', '.bat') -contains $selectedExtension) {
        $filePath = if (-not [string]::IsNullOrWhiteSpace($env:ComSpec)) { $env:ComSpec } else { 'cmd.exe' }
        $argumentText = '/d /c "' + $selectedCommandPath + '"'
        if ($quotedArguments.Count -gt 0) {
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
    $stdoutTask = $null
    $stderrTask = $null
    $exitCode = -3
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $filePath
        $startInfo.Arguments = $argumentText
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $startInfo.WorkingDirectory = $WorkingDirectory }

        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        $null = $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timeoutSeconds = Get-InstallerVerificationTimeoutSeconds
        if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
            try {
                $process.Kill()
            }
            catch {
            }

            return [ordered]@{
                Succeeded = $false
                Output = "$Command timed out after $timeoutSeconds second(s)."
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
        if ($null -ne $stdoutTask) { $stdoutTask.GetAwaiter().GetResult() }
        if ($null -ne $stderrTask) { $stderrTask.GetAwaiter().GetResult() }
    ) -join [Environment]::NewLine
    $output = $output.Trim()
    $containsToken = -not [string]::IsNullOrWhiteSpace($output) -and $output.Contains($ExpectedToken)
    if ($exitCode -ne 0) {
        if (-not $ExpectPresent -and -not $containsToken) {
            return [ordered]@{
                Succeeded = $true
                Output = $output
                ExitCode = $exitCode
            }
        }

        return [ordered]@{
            Succeeded = $false
            Output = $output
            ExitCode = $exitCode
        }
    }

    return [ordered]@{
        Succeeded = ($containsToken -eq $ExpectPresent)
        Output = $output
        ExitCode = $exitCode
    }
}
function Invoke-StandaloneUninstallVerification {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord,
        $RegistrationChanges
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $verificationSucceeded = switch ($clientBaseId) {
        'claude-code' {
            (Invoke-StandaloneVerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'codex' {
            (Invoke-StandaloneVerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false -WorkingDirectory ([Environment]::SystemDirectory)).Succeeded
            break
        }
        'grok' {
            (Invoke-StandaloneVerificationCommand -Command 'grok' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false -WorkingDirectory ([Environment]::SystemDirectory)).Succeeded
            break
        }
        'cursor' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'vscode' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'servers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'visual-studio' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'servers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'claude-desktop' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'other' {
            $verificationTargets = New-Object System.Collections.Generic.List[string]
            foreach ($candidateTarget in @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $RegistrationRecord)) {
                Add-StandaloneTrustedTargetCandidate -Targets $verificationTargets -Candidate $candidateTarget
            }

            if ($verificationTargets.Count -eq 0) {
                $recordedTarget = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('target', 'Target', 'RegistrationTarget')
                $trustedRecordedTarget = $null
                if (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
                    try {
                        $trustedRecordedTarget = Assert-InstallerLocalPathTrusted -Path $recordedTarget
                    }
                    catch {
                        $trustedRecordedTarget = $null
                    }
                }

                if (-not [string]::IsNullOrWhiteSpace($trustedRecordedTarget) -and (Test-Path -LiteralPath $trustedRecordedTarget)) {
                    $false
                }
                else {
                    $true
                }
            }
            else {
                @($verificationTargets.ToArray()).Where({
                        $trustedVerificationTarget = $null
                        try {
                            $trustedVerificationTarget = Assert-InstallerLocalPathTrusted -Path $_
                        }
                        catch {
                            $trustedVerificationTarget = $null
                        }

                        -not [string]::IsNullOrWhiteSpace($trustedVerificationTarget) -and (Test-Path -LiteralPath $trustedVerificationTarget)
                    }).Count -eq 0
            }
            break
        }
        default {
            $true
        }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}

function Get-StandaloneUninstallCleanupGuidance {
    param([Parameter(Mandatory)] [string]$ResolvedClient)

    $otherClause = if ((Resolve-ClientBaseId -ClientId $ResolvedClient) -eq 'other') {
        ' For -Client other, other.mcpServers.json is the selected artifact-only registration target.'
    }
    else {
        ''
    }

    return "uninstall removes or verifies only the selected registration and leaves installer-owned server locations in place.$otherClause For E2E, temporary, or decommissioning cleanup, use the full cleanup action. Use -Action full-uninstall to remove all detected registrations, generated client-registration artifacts, and installer-owned server locations."
}

function Get-StandaloneFullUninstallCleanupGuidance {
    param([switch]$InstallRootWasSpecified)
    if ($InstallRootWasSpecified) { return 'full-uninstall is scoped to the exact -InstallRoot path and removes detected registrations, generated client-registration artifacts, and installer-owned server locations only for that root. Persisted auth secrets and certificate stores remain manual cleanup items.' }
    return 'full-uninstall removes all detected registrations, generated client-registration artifacts, and installer-owned server locations. Persisted auth secrets and certificate stores remain manual cleanup items.'
}

function Get-StandaloneFullUninstallResultSummary {
    param(
        [object[]]$RemovedInstallations,
        [string]$RequestedVersion = 'latest'
    )

    $versions = @($RemovedInstallations |
        ForEach-Object { Get-StandaloneRecordStringValue -Record $_ -PropertyNames @('ResolvedVersion', 'resolvedVersion') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    $installRoots = @($RemovedInstallations |
        ForEach-Object { Get-StandaloneRecordStringValue -Record $_ -PropertyNames @('InstallRoot', 'installRoot') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    $releaseChannels = @($versions |
        ForEach-Object { if ($_ -match '-') { 'prerelease' } else { 'stable' } } |
        Sort-Object -Unique)

    return [ordered]@{
        version = if ($versions.Count -eq 1) { $versions[0] } elseif ($versions.Count -gt 1) { 'multiple' } else { $RequestedVersion }
        resolvedVersion = if ($versions.Count -eq 1) { $versions[0] } else { $null }
        installRoot = if ($installRoots.Count -eq 1) { $installRoots[0] } else { $null }
        releaseChannel = if ($releaseChannels.Count -eq 1) { $releaseChannels[0] } elseif ($releaseChannels.Count -gt 1) { 'mixed' } else { $null }
    }
}
