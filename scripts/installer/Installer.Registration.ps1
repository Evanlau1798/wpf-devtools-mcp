$installerRegistrationHelperLeafNames = @(
    'Installer.Registration.Paths.ps1'
    'Installer.Registration.Json.ps1'
    'Installer.Registration.TrustedTargets.ps1'
    'Installer.Registration.Cursor.ps1'
    'Installer.Registration.Commands.ps1'
    'Installer.Registration.Clients.ps1'
)

foreach ($helperLeafName in $installerRegistrationHelperLeafNames) {
    $helperPath = Join-Path $PSScriptRoot $helperLeafName
    if (-not (Test-Path -LiteralPath $helperPath)) {
        throw "Installer registration helper script was not found: $helperPath"
    }

    . $helperPath
}
