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

function Get-TrustedCursorRecordedTarget {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    $recordedTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        return $null
    }

    $allowedTargets = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate $CursorConfigPath
    }

    Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord)

    switch ($SelectedClient) {
        'cursor-global' {
            Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorGlobalConfigPath)
        }
        'cursor-project' {
            Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
        }
        default {
            if ($CursorMode -eq 'project') {
                Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
            }
            else {
                Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorGlobalConfigPath)
                if (-not [string]::IsNullOrWhiteSpace($CursorProjectRoot)) {
                    Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
                }
            }
        }
    }

    foreach ($allowedTarget in $allowedTargets) {
        if (Test-InstallerPathEqualsCore -Left $recordedTarget -Right $allowedTarget) {
            return $allowedTarget
        }
    }

    return $null
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

    $recordedTarget = Get-TrustedCursorRecordedTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        $recordedTarget = Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    }

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
    $recordTarget = Get-TrustedCursorRecordedTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($recordTarget)) {
        $paths.Add($recordTarget)
    }

    $manifestTarget = Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) {
        $alreadyAdded = $false
        foreach ($existingPath in $paths) {
            if (Test-InstallerPathEqualsCore -Left $existingPath -Right $manifestTarget) {
                $alreadyAdded = $true
                break
            }
        }

        if (-not $alreadyAdded) {
            $paths.Add($manifestTarget)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorMode) -or -not [string]::IsNullOrWhiteSpace($CursorConfigPath) -or $SelectedClient -like 'cursor-*') {
        $profile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
        if (-not [string]::IsNullOrWhiteSpace([string]$profile.ConfigPath)) {
            $alreadyAdded = $false
            foreach ($existingPath in $paths) {
                if (Test-InstallerPathEqualsCore -Left $existingPath -Right ([string]$profile.ConfigPath)) {
                    $alreadyAdded = $true
                    break
                }
            }

            if (-not $alreadyAdded) {
                $paths.Add([string]$profile.ConfigPath)
            }
        }
    }

    $defaultCandidatePaths = switch ($SelectedClient) {
        'cursor-global' { @((Resolve-CursorGlobalConfigPath)) }
        'cursor-project' { @((Resolve-CursorProjectConfigPath)) }
        default {
            switch ($CursorMode) {
                'global' { @((Resolve-CursorGlobalConfigPath)) }
                'project' { @((Resolve-CursorProjectConfigPath)) }
                default {
                    @(
                        (Resolve-CursorProjectConfigPath)
                        (Resolve-CursorGlobalConfigPath)
                    )
                }
            }
        }
    }

    foreach ($candidatePath in $defaultCandidatePaths) {
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
