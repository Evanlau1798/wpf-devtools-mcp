function New-ManualCliArtifactRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$ArtifactFileName,
        [Parameter(Mandatory)] [string]$ErrorMessage
    )

    $artifactPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $InstallBase "client-registration\$ArtifactFileName")
    if (-not (Test-Path -LiteralPath $artifactPath)) {
        throw "Automatic $ClientName registration failed and the manual registration artifact is missing: $artifactPath. Original error: $ErrorMessage"
    }

    return [ordered]@{
        client = $ClientName
        mode = 'manual-cli-artifact'
        target = $artifactPath
        backupPath = $null
        applied = $false
        manualRegistrationRequired = $true
        registrationError = $ErrorMessage
    }
}

function Invoke-CliRegistrationWithManualArtifactFallback {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$ArtifactFileName
    )

    try {
        return (Invoke-RegistrationCommand -Command $Command -Arguments $Arguments -ClientName $ClientName)
    }
    catch {
        $failureMessage = [string]$_.Exception.Message
        if (-not (Test-ManualCliArtifactFallbackEligible `
                    -Command $Command `
                    -ClientName $ClientName `
                    -FailureMessage $failureMessage)) {
            throw
        }

        $global:LASTEXITCODE = 0
        return (New-ManualCliArtifactRegistration `
                -ClientName $ClientName `
                -InstallBase $InstallBase `
                -ArtifactFileName $ArtifactFileName `
                -ErrorMessage $failureMessage)
    }
}

function Test-ManualCliArtifactFallbackEligible {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$FailureMessage
    )

    $expectedExecutionFailurePrefix = "$Command registration failed for $ClientName"
    if ($FailureMessage.IndexOf($expectedExecutionFailurePrefix, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $true
    }

    $envVarName = Get-TrustedCliCommandPathEnvVarName -Command $Command
    return -not [string]::IsNullOrWhiteSpace($envVarName) -and
        $FailureMessage.IndexOf("$envVarName cannot be used while the installer is elevated.", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Remove-ManualCliArtifactRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        $RegistrationRecord
    )

    $targetPath = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    if ([string]::IsNullOrWhiteSpace($targetPath)) {
        return [ordered]@{
            client = $ClientName
            mode = 'manual-cli-artifact'
            target = $null
            backupPath = $null
            applied = $false
        }
    }

    $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path $targetPath
    $backupPath = $null
    $applied = $false
    if (Test-Path -LiteralPath $trustedTargetPath) {
        $backupPath = Assert-InstallerLocalPathTrusted -Path "$trustedTargetPath.bak-$([guid]::NewGuid().ToString('N'))"
        Copy-Item -LiteralPath $trustedTargetPath -Destination $backupPath -Force
        Remove-PathIfExists -Path $trustedTargetPath
        $applied = $true
    }

    return [ordered]@{
        client = $ClientName
        mode = 'manual-cli-artifact'
        target = $trustedTargetPath
        backupPath = $backupPath
        applied = $applied
    }
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
            return @(Invoke-CliRegistrationWithManualArtifactFallback `
                    -Command 'claude' `
                    -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $InstalledExecutable) `
                    -ClientName $clientBaseId `
                    -InstallBase $InstallBase `
                    -ArtifactFileName 'claude-code.txt')
        }
        'codex' {
            return @(Invoke-CliRegistrationWithManualArtifactFallback `
                    -Command 'codex' `
                    -Arguments @('mcp', 'add', 'wpf-devtools', '--', $InstalledExecutable) `
                    -ClientName $clientBaseId `
                    -InstallBase $InstallBase `
                    -ArtifactFileName 'codex.txt')
        }
        'grok' {
            return @(Invoke-CliRegistrationWithManualArtifactFallback `
                    -Command 'grok' `
                    -Arguments @('mcp', 'add', '--scope', 'user', 'wpf-devtools', '--', $InstalledExecutable) `
                    -ClientName $clientBaseId `
                    -InstallBase $InstallBase `
                    -ArtifactFileName 'grok.txt')
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
    $recordedTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
    $recordedMode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'RegistrationMode')
    if ([string]::Equals($recordedMode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
        return @(Remove-ManualCliArtifactRegistration -ClientName $clientBaseId -RegistrationRecord $RegistrationRecord)
    }

    switch ($clientBaseId) {
        'claude-code' {
            return @(Invoke-OptionalRemovalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'codex' {
            return @(Invoke-OptionalRemovalCommand -Command 'codex' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'grok' {
            return @(Invoke-OptionalRemovalCommand -Command 'grok' -Arguments @('mcp', 'remove', '--scope', 'user', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'cursor' {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -PromptIfNeeded -RegistrationRecord $RegistrationRecord
            $registration = Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath ([string]$cursorProfile.ConfigPath)
            $registration['mode'] = "cursor-$([string]$cursorProfile.Mode)"
            $registration['target'] = [string]$cursorProfile.ConfigPath
            return @($registration)
        }
        'vscode' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-VsCodeConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath $configPath)
        }
        'visual-studio' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-VisualStudioConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath $configPath)
        }
        'claude-desktop' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-ClaudeDesktopConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath $configPath)
        }
        'other' {
            $artifactTargets = @(Get-TrustedOtherRegistrationArtifactTargets -RegistrationRecord $RegistrationRecord)
            $targetPath = if ($artifactTargets.Count -gt 0) {
                [string]$artifactTargets[0]
            }
            elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
                $recordedTarget
            }
            else {
                $null
            }
            $backupPath = $null
            $applied = $false
            foreach ($candidateTarget in @($artifactTargets + @($targetPath))) {
                if ([string]::IsNullOrWhiteSpace([string]$candidateTarget)) {
                    continue
                }

                try {
                    $trustedCandidateTarget = Assert-InstallerLocalPathTrusted -Path ([string]$candidateTarget)
                }
                catch {
                    continue
                }

                if (-not (Test-Path -LiteralPath $trustedCandidateTarget)) {
                    continue
                }

                if ([string]::IsNullOrWhiteSpace($backupPath)) {
                    $targetPath = $trustedCandidateTarget
                    $backupPath = Assert-InstallerLocalPathTrusted -Path "$targetPath.bak-$([guid]::NewGuid().ToString('N'))"
                    Copy-Item -LiteralPath $targetPath -Destination $backupPath -Force
                }

                Remove-PathIfExists -Path $trustedCandidateTarget
                $applied = $true
            }

            return @([ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = $targetPath
                    backupPath = $backupPath
                    applied = $applied
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
    $registrationDir = Assert-InstallerLocalPathTrusted -Path $registrationDir
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null
    Assert-InstallerLocalPathTrusted -Path $registrationDir | Out-Null

    $serverNode = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $stdioRegistrationJson = ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5)
    $mcpServersRegistrationJson = ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5)

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'vscode.json') -Content $stdioRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'visual-studio.json') -Content $stdioRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'cursor.global.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'cursor.project.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'claude-desktop.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'other.mcpServers.json') -Content $mcpServersRegistrationJson

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'claude-code.txt') -Content @"
claude mcp add --transport stdio wpf-devtools -- "$InstalledExecutable"

claude mcp remove wpf-devtools
"@

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'codex.txt') -Content @"
codex mcp add wpf-devtools -- "$InstalledExecutable"

codex mcp remove wpf-devtools
"@

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'grok.txt') -Content @"
grok mcp add --scope user wpf-devtools -- "$InstalledExecutable"

grok mcp remove --scope user wpf-devtools
"@
}
