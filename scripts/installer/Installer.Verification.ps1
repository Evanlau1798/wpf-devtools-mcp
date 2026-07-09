if ($null -eq (Get-Command Invoke-VerificationCommand -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Installer.Verification.Commands.ps1')
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

    try {
        $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    }
    catch {
        return $false
    }

    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
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

function Test-ManualCliArtifactRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$ArtifactPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    try {
        $trustedArtifactPath = Assert-InstallerLocalPathTrusted -Path $ArtifactPath
        if (-not (Test-Path -LiteralPath $trustedArtifactPath)) { return $false }
        $content = Get-Content -LiteralPath $trustedArtifactPath -Raw
        return $content.IndexOf('mcp add', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $content.IndexOf('wpf-devtools', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $content.IndexOf($InstalledExecutable, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    }
    catch {
        return $false
    }
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
    $trustedInstalledExecutable = $null
    try {
        $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $InstalledExecutable
    }
    catch {
        $trustedInstalledExecutable = $null
    }

    switch ($clientBaseId) {
        { $_ -in @('claude-code', 'codex', 'grok') } {
            $command = switch ($clientBaseId) { 'claude-code' { 'claude' } default { $clientBaseId } }
            $label = switch ($clientBaseId) { 'claude-code' { 'Claude Code' } 'codex' { 'Codex' } default { 'Grok' } }
            if ([string]::Equals([string]$Registration.mode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
                $verificationSucceeded = (Test-ManualCliArtifactRegistrationMatchesExecutable -ArtifactPath ([string]$Registration.target) -InstalledExecutable $InstalledExecutable) -and
                    -not [string]::IsNullOrWhiteSpace($trustedInstalledExecutable) -and
                    (Test-Path -LiteralPath $trustedInstalledExecutable)
                $verificationMessage = if ($verificationSucceeded) { "Manual $label registration artifact verified at $([string]$Registration.target)." } else { "Manual $label registration artifact verification failed." }
            }
            else {
                $verification = Test-CliRegistrationMatchesExecutable -Command $command -InstalledExecutable $InstalledExecutable
                $verificationSucceeded = ($verification.Succeeded -and -not [string]::IsNullOrWhiteSpace($trustedInstalledExecutable) -and (Test-Path -LiteralPath $trustedInstalledExecutable))
                $verificationMessage = if ($verificationSucceeded) { "Verified with $command mcp list." } else { "$label verification failed: $($verification.Output)" }
            }
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
    $recordedMode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'RegistrationMode')
    if ([string]::Equals($recordedMode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
        $manualTargets = New-Object System.Collections.Generic.List[string]
        foreach ($registrationChange in @($RegistrationChanges)) {
            if ($null -ne $registrationChange -and $registrationChange.Contains('target') -and -not [string]::IsNullOrWhiteSpace([string]$registrationChange.target)) {
                $manualTargets.Add([string]$registrationChange.target)
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
            $manualTargets.Add($recordedTarget)
        }

        $remainingTargets = @($manualTargets | Where-Object {
            try { Test-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path ([string]$_)) }
            catch { $false }
        })

        return [ordered]@{
            Succeeded = ($remainingTargets.Count -eq 0)
            VerificationMessage = if ($remainingTargets.Count -eq 0) { 'Verified manual CLI registration artifact removal.' } else { 'Manual CLI registration artifact still exists.' }
        }
    }

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
        { $_ -in @('codex', 'grok') } {
            (Invoke-VerificationCommand -Command $clientBaseId -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
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
                $recordedTarget = if ($null -ne $RegistrationRecord) {
                    if ($RegistrationRecord.Contains('target')) { [string]$RegistrationRecord.target }
                    elseif ($RegistrationRecord.Contains('Target')) { [string]$RegistrationRecord.Target }
                    else { [string]$RegistrationRecord.RegistrationTarget }
                }
                else {
                    $null
                }

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
                @($verificationTargets).Where({
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
        default { $true }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}
