<#
.SYNOPSIS
    Loads installer action helpers that install prepared package directories and run install/uninstall workflows.

.PARAMETER TrustedArchiveManifestPolicy
    Indicates the package directory came from an archive session that already
    passed release checksum and archive safety validation, allowing
    DebugTrustedRootSkip manifests to skip per-payload Authenticode checks.
#>
$installerActionHelperLeafNames = @(
    'Installer.Actions.Paths.ps1'
    'Installer.Actions.Payload.ps1'
    'Installer.Actions.Rollback.ps1'
    'Installer.Actions.State.ps1'
    'Installer.Actions.Core.ps1'
)

foreach ($helperLeafName in $installerActionHelperLeafNames) {
    $helperPath = Join-Path $PSScriptRoot $helperLeafName
    if (-not (Test-Path -LiteralPath $helperPath)) {
        throw "Installer action helper script was not found: $helperPath"
    }

    . $helperPath
}
