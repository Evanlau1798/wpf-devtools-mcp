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
    $recordedTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
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
}
