function Reset-TuiConfirmationCore {
    param([Parameter(Mandatory)] $State)

    $State.ConfirmationMode = $null
    $State.ConfirmationStep = 0
    return $State
}

function Enter-TuiConfirmationCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [ValidateSet('unregister', 'full-uninstall', 'close-app')] [string]$ConfirmationMode,
        [string]$ClientId
    )

    $State.CurrentScreen = 'ConfirmScreen'
    $State.PendingAction = $null
    $State.PendingClient = $ClientId
    $State.ConfirmationMode = $ConfirmationMode
    $State.ConfirmationStep = 1
    $State.SelectionIndex = 0
    $State.ScrollOffset = 0
    return $State
}

function Get-TuiConfirmationLinesCore {
    param([Parameter(Mandatory)] $State)

    $lines = New-Object System.Collections.Generic.List[string]
    $totalSteps = if ([string]$State.ConfirmationMode -eq 'close-app') { 1 } else { 2 }
    $stepLabel = "Step $([int]$State.ConfirmationStep) of $totalSteps"
    $lines.Add($stepLabel)
    $lines.Add('')

    if ([string]$State.ConfirmationMode -eq 'close-app') {
        $lines.Add('Close the installer now?')
        $lines.Add('No changes will be made unless you have already confirmed a pending action.')
        $lines.Add('Press Enter to exit, or Escape to stay on the home screen.')
    }
    elseif ([string]$State.ConfirmationMode -eq 'full-uninstall') {
        $registrations = @(Get-DetectedInstallerRegistrations -State $State.InstallerState)
        $installations = @(Get-DetectedInstallerInstallations -State $State.InstallerState)
        if ([int]$State.ConfirmationStep -eq 1) {
            $lines.Add('Full Uninstall removes every detected registration and every installer-owned server location.')
            $lines.Add('Server files are only deleted when ownership can be verified from installer metadata.')
            $lines.Add('Press Enter to review the final summary, or Escape to cancel.')
        }
        else {
            $lines.Add("Detected registrations: $($registrations.Count)")
            $lines.Add("Installer-owned server locations: $($installations.Count)")
            $lines.Add('Press Enter again to continue with Full Uninstall, or Escape to cancel.')
        }
    }
    else {
        $clientLabel = Resolve-ClientLabel -ClientId ([string]$State.PendingClient)
        if ([int]$State.ConfirmationStep -eq 1) {
            $lines.Add("You are about to remove the $clientLabel registration.")
            $lines.Add('This keeps all installed server files on disk.')
            $lines.Add('Press Enter to continue, or Escape to cancel.')
        }
        else {
            $lines.Add("Final confirmation: remove the $clientLabel registration now.")
            $lines.Add('Server files will remain available for other clients or a later Full Uninstall.')
            $lines.Add('Press Enter again to confirm, or Escape to cancel.')
        }
    }

    return ,$lines.ToArray()
}

function Handle-TuiConfirmationKeyCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $KeyInfo
    )

    switch ($KeyInfo.Key) {
        ([ConsoleKey]::Escape) {
            $confirmationMode = [string]$State.ConfirmationMode
            $State = Reset-TuiConfirmationCore -State $State
            $State.CurrentScreen = if ($confirmationMode -eq 'close-app') { 'HomeScreen' } else { 'UninstallScreen' }
            return $State
        }
        ([ConsoleKey]::Backspace) {
            $confirmationMode = [string]$State.ConfirmationMode
            $State = Reset-TuiConfirmationCore -State $State
            $State.CurrentScreen = if ($confirmationMode -eq 'close-app') { 'HomeScreen' } else { 'UninstallScreen' }
            return $State
        }
        ([ConsoleKey]::Enter) {
            if ([string]$State.ConfirmationMode -eq 'close-app') {
                $State.PendingAction = 'exit'
                return $State
            }

            if ([int]$State.ConfirmationStep -lt 2) {
                $State.ConfirmationStep = [int]$State.ConfirmationStep + 1
                return $State
            }

            $State.PendingAction = switch ([string]$State.ConfirmationMode) {
                'close-app' { 'exit' }
                'full-uninstall' { 'full-uninstall' }
                default { 'uninstall' }
            }
            return $State
        }
    }

    return $State
}
