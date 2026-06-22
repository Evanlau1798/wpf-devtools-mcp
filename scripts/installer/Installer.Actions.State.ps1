function Update-InstallerStateAfterInstall {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] $Registration,
        [Parameter(Mandatory)] [string]$LastVerifiedUtc
    )

    $State.lastInstallRoot = $ResolvedInstallRoot
    $State.architectures[$ResolvedArchitecture] = [ordered]@{
        version = $ResolvedVersion
        executable = $InstalledExecutable
        installRoot = $ResolvedInstallRoot
    }
    $stateKey = Resolve-ClientStateKey -ClientId $SelectedClient -RegistrationMode ([string]$Registration.mode)
    $State.registrations[$stateKey] = [ordered]@{
        architecture = $ResolvedArchitecture
        installRoot = $ResolvedInstallRoot
        mode = [string]$Registration.mode
        target = [string]$Registration.target
        resolvedVersion = $ResolvedVersion
        installedExecutable = $InstalledExecutable
        lastVerifiedUtc = $LastVerifiedUtc
    }
}

function Merge-RegistrationRecordWithStateFallback {
    param(
        $RegistrationRecord,
        $StateRecord,
        [string]$SelectedClient
    )

    if ($null -eq $RegistrationRecord -and $null -eq $StateRecord) {
        return $null
    }

    $clientId = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ClientId', 'clientId', 'client')))) {
        Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ClientId', 'clientId', 'client')
    }
    else {
        $SelectedClient
    }

    $installerOwned = $false
    if ($null -ne $RegistrationRecord) {
        if ($RegistrationRecord.Contains('InstallerOwned')) {
            $installerOwned = [bool]$RegistrationRecord.InstallerOwned
        }
        elseif ($RegistrationRecord.Contains('installerOwned')) {
            $installerOwned = [bool]$RegistrationRecord.installerOwned
        }
    }

    $evidenceSource = if ($null -ne $RegistrationRecord) {
        Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('EvidenceSource', 'evidenceSource')
    }
    else {
        $null
    }

    return [ordered]@{
        ClientId = $clientId
        RegistrationMode = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationMode', 'registrationMode', 'mode')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationMode', 'registrationMode', 'mode') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('mode', 'RegistrationMode', 'registrationMode') }
        RegistrationTarget = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationTarget', 'target')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationTarget', 'target') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('target', 'RegistrationTarget') }
        InstalledExecutable = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('installedExecutable', 'InstalledExecutable') }
        InstallRoot = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstallRoot', 'installRoot')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstallRoot', 'installRoot') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('installRoot', 'InstallRoot') }
        Architecture = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('Architecture', 'architecture')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('Architecture', 'architecture') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('architecture', 'Architecture') }
        InstallerOwned = $installerOwned
        EvidenceSource = $evidenceSource
        ResolvedVersion = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ResolvedVersion', 'resolvedVersion')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ResolvedVersion', 'resolvedVersion') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('resolvedVersion', 'ResolvedVersion') }
        LastVerifiedUtc = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('LastVerifiedUtc', 'lastVerifiedUtc')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('LastVerifiedUtc', 'lastVerifiedUtc') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('lastVerifiedUtc', 'LastVerifiedUtc') }
    }
}

function Test-InstallerRegistrationMatchesInstallRoot {
    param(
        $RegistrationRecord,
        [string]$ExpectedInstallRoot
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedInstallRoot)) {
        return $true
    }

    if ($null -eq $RegistrationRecord) {
        return $false
    }

    $recordInstallRoot = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    if (-not [string]::IsNullOrWhiteSpace($recordInstallRoot)) {
        return (Test-InstallerPathEqualsCore -Left $recordInstallRoot -Right $ExpectedInstallRoot)
    }

    $installedExecutable = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable', 'executable', 'Executable')
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        return $false
    }

    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    return ([bool]$ownership.InstallerOwned -and (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $ExpectedInstallRoot))
}

function Test-InstallerExplicitRootCliUninstallNoOp {
    param(
        $RegistrationRecord,
        [bool]$InstallRootWasSpecified
    )

    if (-not $InstallRootWasSpecified -or $null -eq $RegistrationRecord) {
        return $false
    }

    $mode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
    if (-not [string]::Equals($mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $installedExecutable = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    return [string]::IsNullOrWhiteSpace($installedExecutable)
}
