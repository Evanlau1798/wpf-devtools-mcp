function Invoke-InstallerVerifiedRemoval {
    param(
        [Parameter(Mandatory)] [string]$RegistrationMode,
        [string]$InstalledExecutable,
        [Parameter(Mandatory)] [bool]$InstallerOwned
    )

    return [ordered]@{
        RegistrationMode = $RegistrationMode
        InstalledExecutable = $InstalledExecutable
        InstallerOwned = $InstallerOwned
    }
}

function Invoke-InstallerFullUninstallCore {
    param([Parameter(Mandatory)] $State)

    $detectedRegistrations = @(Get-DetectedInstallerRegistrations -State $State)
    $detectedInstallations = @(Get-DetectedInstallerInstallations -State $State)
    $unregistrationResults = @()

    foreach ($registration in $detectedRegistrations) {
        $unregistrationResults += @(Invoke-ClientUnregistration -SelectedClient ([string]$registration.ClientId))
    }

    $verificationFailures = @()
    foreach ($registration in $detectedRegistrations) {
        $verification = Invoke-UninstallVerification -SelectedClient ([string]$registration.ClientId)
        if (-not $verification.Succeeded) {
            $verificationFailures += [string]$verification.VerificationMessage
        }
    }

    $removedInstallations = @()
    foreach ($installation in $detectedInstallations) {
        if (-not [bool]$installation.InstallerOwned) {
            continue
        }

        Remove-PathIfExists -Path ([string]$installation.InstallBase)
        $removedInstallations += $installation
    }

    foreach ($installation in $removedInstallations) {
        if (Test-Path ([string]$installation.InstallBase)) {
            $verificationFailures += "Installation root still exists: $([string]$installation.InstallBase)"
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$installation.InstalledExecutable) -and (Test-Path ([string]$installation.InstalledExecutable))) {
            $verificationFailures += "Executable still exists: $([string]$installation.InstalledExecutable)"
        }
    }

    if ($verificationFailures.Count -gt 0) {
        throw ($verificationFailures -join ' ')
    }

    $newState = Get-EmptyInstallerState
    $newState.lastInstallRoot = $State.lastInstallRoot
    $statePath = Save-InstallerState -State $newState
    return [ordered]@{
        action = 'full-uninstall'
        client = 'all'
        statePath = $statePath
        removedInstallation = ($removedInstallations.Count -gt 0)
        removedInstallations = @($removedInstallations)
        registrations = @($unregistrationResults)
        verificationMessage = "Verified removal of $($detectedRegistrations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
    }
}
