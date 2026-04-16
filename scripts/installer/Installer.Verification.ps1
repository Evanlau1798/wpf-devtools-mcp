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
            $installerOwned = if ($registration.Contains('InstallerOwned')) {
                [bool]$registration.InstallerOwned
            }
            elseif ($registration.Contains('installerOwned')) {
                [bool]$registration.installerOwned
            }
            else {
                $false
            }

            if (-not $installerOwned) {
                $installedExecutable = if ($registration.Contains('InstalledExecutable')) { [string]$registration.InstalledExecutable } else { [string]$registration.installedExecutable }
                if (-not [string]::IsNullOrWhiteSpace($installedExecutable) -and $null -ne (Get-Command 'Resolve-InstallerOwnershipFromExecutable' -CommandType Function -ErrorAction SilentlyContinue)) {
                    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
                    $installerOwned = [bool]$ownership.InstallerOwned
                    if ([string]::IsNullOrWhiteSpace($installRoot)) {
                        $installRoot = [string]$ownership.InstallRoot
                    }

                    if ([string]::IsNullOrWhiteSpace($architecture)) {
                        $architecture = [string]$ownership.Architecture
                    }
                }
            }

            if (-not $installerOwned) {
                continue
            }

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

function Get-JsonUninstallVerificationConfigPaths {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord
    )

    $paths = New-Object System.Collections.Generic.List[string]
    $evidenceSource = if ($RegistrationRecord.Contains('EvidenceSource')) {
        [string]$RegistrationRecord.EvidenceSource
    }
    else {
        [string]$RegistrationRecord.evidenceSource
    }
    $installerOwned = if ($RegistrationRecord.Contains('InstallerOwned')) {
        [bool]$RegistrationRecord.InstallerOwned
    }
    else {
        [bool]$RegistrationRecord.installerOwned
    }
    $detectedTarget = if ($RegistrationRecord.Contains('RegistrationTarget')) {
        [string]$RegistrationRecord.RegistrationTarget
    }
    else {
        [string]$RegistrationRecord.target
    }

    if ($installerOwned -and -not [string]::IsNullOrWhiteSpace($detectedTarget) -and -not [string]::Equals($evidenceSource, 'state', [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-TrustedRegistrationTargetCandidate -Targets $paths -Candidate $detectedTarget
    }

    $recordedTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
        Add-TrustedRegistrationTargetCandidate -Targets $paths -Candidate $recordedTarget
    }

    $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) {
        Add-TrustedRegistrationTargetCandidate -Targets $paths -Candidate $manifestTarget
    }

    if ($paths.Count -eq 0) {
        foreach ($candidatePath in @(switch ($SelectedClient) {
                'vscode' { @(Resolve-VsCodeConfigPath); break }
                'visual-studio' { @(Resolve-VisualStudioConfigPath); break }
                'claude-desktop' { @(Resolve-ClaudeDesktopConfigPath); break }
                default { @() }
            })) {
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
    }

    return @($paths.ToArray())
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

function Get-VerifiedWpfDevToolsExecutableFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    foreach ($rawLine in ($Text -split "`r?`n")) {
        $line = [string]$rawLine
        if ($line -notmatch '^\s*(?:[-*]\s*)?["'']?wpf-devtools["'']?(?:\s|:|=|$)') {
            continue
        }

        $match = [regex]::Match($line, '(?<path>[A-Za-z]:\\[^`"\r\n]*wpf-devtools-(x64|x86|arm64)\.exe)', 'IgnoreCase')
        if ($match.Success) {
            return [string]$match.Groups['path'].Value
        }
    }

    return $null
}

function Test-VerifiedInstallerPathEquals {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    try {
        $leftFullPath = [System.IO.Path]::GetFullPath(([string]$Left).Trim().Trim('"')).TrimEnd('\', '/')
        $rightFullPath = [System.IO.Path]::GetFullPath(([string]$Right).Trim().Trim('"')).TrimEnd('\', '/')
        return [System.StringComparer]::OrdinalIgnoreCase.Equals($leftFullPath, $rightFullPath)
    }
    catch {
        return $false
    }
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

    $containsToken = -not [string]::IsNullOrWhiteSpace($output) -and $output.Contains($ExpectedToken)
    return [ordered]@{
        Succeeded = ($containsToken -eq $ExpectPresent)
        Output = $output
        ExitCode = $exitCode
    }
}

function Test-CliRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $verification = Invoke-VerificationCommand -Command $Command -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
    if (-not $verification.Succeeded) {
        return $verification
    }

    $registeredExecutable = Get-VerifiedWpfDevToolsExecutableFromText -Text ([string]$verification.Output)
    if ([string]::IsNullOrWhiteSpace($registeredExecutable)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command mcp list contains wpf-devtools, but could not verify the registered executable path."
            ExitCode = 0
        }
    }

    if (-not (Test-VerifiedInstallerPathEquals -Left $registeredExecutable -Right $InstalledExecutable)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command mcp list registered executable '$registeredExecutable' does not match '$InstalledExecutable'."
            ExitCode = 0
        }
    }

    return $verification
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
            $verification = Test-CliRegistrationMatchesExecutable -Command 'claude' -InstalledExecutable $InstalledExecutable
            $verificationSucceeded = ($verification.Succeeded -and (Test-Path $InstalledExecutable))
            $verificationMessage = if ($verificationSucceeded) { 'Verified with claude mcp list.' } else { "Claude verification failed: $($verification.Output)" }
        }
        'codex' {
            $verification = Test-CliRegistrationMatchesExecutable -Command 'codex' -InstalledExecutable $InstalledExecutable
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
        $RegistrationRecord,
        $RegistrationChanges = @()
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $recordedTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
    $registrationTargets = New-Object System.Collections.Generic.List[string]
    foreach ($registrationChange in @($RegistrationChanges)) {
        if ($null -eq $registrationChange) {
            continue
        }

        $changeClient = if ($registrationChange.Contains('client')) { [string]$registrationChange.client } else { [string]$registrationChange.Client }
        if (-not [string]::IsNullOrWhiteSpace($changeClient) -and -not [string]::Equals($changeClient, $clientBaseId, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $changeTarget = if ($registrationChange.Contains('target')) { [string]$registrationChange.target } else { [string]$registrationChange.Target }
        if ([string]::IsNullOrWhiteSpace($changeTarget)) {
            continue
        }

        Add-TrustedRegistrationTargetCandidate -Targets $registrationTargets -Candidate $changeTarget
    }

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
            $verificationTargets = if ($registrationTargets.Count -gt 0) { @($registrationTargets.ToArray()) } else { @(Get-CursorVerificationConfigPaths -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord) }
            @(
                $verificationTargets
            ).Where({
                    Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $_
                }).Count -eq 0
            break
        }
        'vscode' {
            $verificationTargets = if ($registrationTargets.Count -gt 0) { @($registrationTargets.ToArray()) } else { @(Get-JsonUninstallVerificationConfigPaths -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord) }
            @($verificationTargets).Where({
                    Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath $_
                }).Count -eq 0
            break
        }
        'visual-studio' {
            $verificationTargets = if ($registrationTargets.Count -gt 0) { @($registrationTargets.ToArray()) } else { @(Get-JsonUninstallVerificationConfigPaths -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord) }
            @($verificationTargets).Where({
                    Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath $_
                }).Count -eq 0
            break
        }
        'claude-desktop' {
            $verificationTargets = if ($registrationTargets.Count -gt 0) { @($registrationTargets.ToArray()) } else { @(Get-JsonUninstallVerificationConfigPaths -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord) }
            @($verificationTargets).Where({
                    Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $_
                }).Count -eq 0
            break
        }
        'other' {
            $verificationTargets = New-Object System.Collections.Generic.List[string]
            foreach ($candidateTarget in @(Get-TrustedOtherRegistrationArtifactTargets -RegistrationRecord $RegistrationRecord)) {
                Add-TrustedRegistrationTargetCandidate -Targets $verificationTargets -Candidate $candidateTarget
            }
            foreach ($candidateTarget in @($registrationTargets.ToArray())) {
                Add-TrustedRegistrationTargetCandidate -Targets $verificationTargets -Candidate $candidateTarget
            }

            if ($verificationTargets.Count -eq 0) {
                $false
            }
            else {
                @($verificationTargets).Where({
                        Test-Path $_
                    }).Count -eq 0
            }
            break
        }
        default { $true }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}
