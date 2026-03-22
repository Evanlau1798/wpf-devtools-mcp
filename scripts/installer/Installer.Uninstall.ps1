function Invoke-InstallerVerifiedRemoval {
    param(
        [Parameter(Mandatory)] [string]$RegistrationMode,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [bool]$InstallerOwned
    )

    return [ordered]@{
        RegistrationMode = $RegistrationMode
        InstalledExecutable = $InstalledExecutable
        InstallerOwned = $InstallerOwned
    }
}

function Invoke-InstallerFullUninstall {
    return [ordered]@{
        Action = 'full-uninstall'
        Label = 'Full Uninstall'
    }
}
