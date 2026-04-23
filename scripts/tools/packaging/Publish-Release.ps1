param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string[]]$Architectures = @('x64'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$SigningCertificatePath,
    [string]$SigningCertificateThumbprint,
    [string]$SigningPasswordEnvironmentVariable = 'WPFDEVTOOLS_PFX_PASSWORD',
    [string]$SigningTimestampServer = 'https://timestamp.digicert.com',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$supportedArchitectures = @('x64', 'x86', 'arm64')

function Invoke-Step {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-RuntimeId {
    param([Parameter(Mandatory)] [string]$Architecture)

    switch ($Architecture) {
        'x64' { return 'win-x64' }
        'x86' { return 'win-x86' }
        'arm64' { return 'win-arm64' }
        default { throw "Unsupported architecture: $Architecture" }
    }
}

function Get-BootstrapperPlatform {
    param([Parameter(Mandatory)] [string]$Architecture)

    switch ($Architecture) {
        'x64' { return 'x64' }
        'x86' { return 'Win32' }
        'arm64' { return 'ARM64' }
        default { throw "Unsupported architecture: $Architecture" }
    }
}

function Get-PackageChannel {
    param([Parameter(Mandatory)] [string]$BuildConfiguration)
    if ($BuildConfiguration -eq 'Debug') {
        return 'dev'
    }

    return 'release'
}

function Get-SignaturePolicy {
    param([Parameter(Mandatory)] [string]$BuildConfiguration)
    if ($BuildConfiguration -eq 'Debug') {
        return 'DebugTrustedRootSkip'
    }

    return 'RequireAuthenticodeSignature'
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Source path does not exist: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Resolve-ArchitectureList {
    param([string[]]$InputArchitectures)

    $resolvedArchitectures = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $InputArchitectures) {
        foreach ($candidate in ($entry -split ',')) {
            $normalized = $candidate.Trim().ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                continue
            }

            if ($supportedArchitectures -notcontains $normalized) {
                throw "Unsupported architecture: $normalized. Supported values: $($supportedArchitectures -join ', ')"
            }

            if (-not $resolvedArchitectures.Contains($normalized)) {
                $resolvedArchitectures.Add($normalized)
            }
        }
    }

    if ($resolvedArchitectures.Count -eq 0) {
        throw 'At least one architecture must be specified.'
    }

    return @($resolvedArchitectures)
}

function Resolve-ServerOutputSource {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$BuildConfiguration,
        [Parameter(Mandatory)] [string]$RuntimeId,
        [Parameter(Mandatory)] [bool]$UseExistingBuildOutput
    )

    $runtimeBuildDir = Join-Path $RepositoryRoot "src\WpfDevTools.Mcp.Server\bin\$BuildConfiguration\net8.0\$RuntimeId"
    if (Test-Path $runtimeBuildDir) {
        return [ordered]@{
            Path = $runtimeBuildDir
        }
    }

    $frameworkBuildDir = Join-Path $RepositoryRoot "src\WpfDevTools.Mcp.Server\bin\$BuildConfiguration\net8.0"
    if ($UseExistingBuildOutput) {
        if (Test-Path (Join-Path $frameworkBuildDir 'WpfDevTools.Mcp.Server.exe')) {
            throw "Expected existing server output was not found for runtime '$RuntimeId'. -SkipBuild requires RID-specific publish output under $runtimeBuildDir; framework-only output at $frameworkBuildDir cannot be repackaged safely."
        }

        throw "Expected existing server output was not found for runtime '$RuntimeId': $runtimeBuildDir"
    }

    return $null
}

function Copy-ServerBuildOutput {
    param(
        [Parameter(Mandatory)] $SourceInfo,
        [Parameter(Mandatory)] [string]$Destination
    )

    Copy-DirectoryContents -Source $SourceInfo.Path -Destination $Destination
}

function Copy-DirectoryFilesOnly {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Source path does not exist: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -Path $Source -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $Destination $_.Name) -Force
    }
}

function Get-InstallerHelperFiles {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $manifestPath = Join-Path $RepositoryRoot 'scripts\installer\installer-helpers.manifest.json'
    if (-not (Test-Path $manifestPath)) {
        throw "Installer helper manifest was not found: $manifestPath"
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    $helperFiles = @(
        $manifest.helperFiles |
            ForEach-Object {
                if ($_ -is [string]) {
                    return [string]$_
                }

                return [string]$_.path
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($helperFiles.Count -eq 0) {
        throw "Installer helper manifest did not declare any helper files: $manifestPath"
    }

    return @($helperFiles)
}

function Copy-InstallerHelperFiles {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$Destination
    )

    $installerRoot = Join-Path $RepositoryRoot 'scripts\installer'
    $manifestPath = Join-Path $installerRoot 'installer-helpers.manifest.json'

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path $manifestPath -Destination (Join-Path $Destination 'installer-helpers.manifest.json') -Force

    foreach ($helperFile in @(Get-InstallerHelperFiles -RepositoryRoot $RepositoryRoot)) {
        $sourcePath = Join-Path $installerRoot $helperFile
        if (-not (Test-Path $sourcePath)) {
            throw "Installer helper file declared in manifest was not found: $sourcePath"
        }

        Copy-Item -Path $sourcePath -Destination (Join-Path $Destination $helperFile) -Force
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Test-InstallerTestModeEnabled {
    return [string]::Equals([string]$env:WPFDEVTOOLS_INSTALLER_TEST_MODE, '1', [System.StringComparison]::Ordinal)
}

function Test-NonInteractiveReleaseSigningContext {
    return [string]::Equals([string]$env:GITHUB_ACTIONS, 'true', [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals([string]$env:CI, 'true', [System.StringComparison]::OrdinalIgnoreCase)
}

function Normalize-SignerThumbprint {
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        return $null
    }

    return $Thumbprint.Replace(' ', '').ToUpperInvariant()
}

function Get-ReleaseSignerPin {
    $thumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT)
    $subject = [string]$env:WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT
    return [ordered]@{
        Thumbprint = $thumbprint
        Subject = if ([string]::IsNullOrWhiteSpace($subject)) { $null } else { $subject }
    }
}

function Get-ReleaseSigningInputs {
    param(
        [string]$CertificatePathParameter,
        [string]$CertificateThumbprintParameter,
        [string]$PasswordEnvironmentVariableParameter,
        [string]$TimestampServerParameter
    )

    $certificatePath = if (-not [string]::IsNullOrWhiteSpace($CertificatePathParameter)) {
        $CertificatePathParameter
    }
    else {
        [string]$env:WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH
    }

    $certificateThumbprint = if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprintParameter)) {
        $CertificateThumbprintParameter
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT)) {
        [string]$env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT
    }
    else {
        [string]$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT
    }

    $passwordEnvironmentVariable = if (-not [string]::IsNullOrWhiteSpace($PasswordEnvironmentVariableParameter)) {
        $PasswordEnvironmentVariableParameter
    }
    else {
        'WPFDEVTOOLS_PFX_PASSWORD'
    }

    $timestampServer = if (-not [string]::IsNullOrWhiteSpace($TimestampServerParameter)) {
        $TimestampServerParameter
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$env:WPFDEVTOOLS_RELEASE_TIMESTAMP_SERVER)) {
        [string]$env:WPFDEVTOOLS_RELEASE_TIMESTAMP_SERVER
    }
    else {
        'https://timestamp.digicert.com'
    }

    return [ordered]@{
        CertificatePath = if ([string]::IsNullOrWhiteSpace($certificatePath)) { $null } else { $certificatePath }
        CertificateThumbprint = Normalize-SignerThumbprint -Thumbprint $certificateThumbprint
        PasswordEnvironmentVariable = $passwordEnvironmentVariable
        TimestampServer = $timestampServer
    }
}

function Resolve-SignToolPath {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_SIGNTOOL_PATH)) {
        return $env:WPFDEVTOOLS_SIGNTOOL_PATH
    }

    $command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) {
        return $null
    }

    $candidate = Get-ChildItem -Path $kitsRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\\x64\\signtool.exe' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    return $candidate.FullName
}

function Get-CertificatePassword {
    param(
        [Parameter(Mandatory)] [string]$EnvironmentVariableName,
        [Parameter(Mandatory)] [string]$CertificatePath
    )

    $passwordValue = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, 'Process')
    if ([string]::IsNullOrWhiteSpace($passwordValue)) {
        $passwordValue = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, 'User')
    }
    if ([string]::IsNullOrWhiteSpace($passwordValue)) {
        $passwordValue = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, 'Machine')
    }

    if (-not [string]::IsNullOrWhiteSpace($passwordValue)) {
        return (ConvertTo-SecureString -String $passwordValue -AsPlainText -Force)
    }

    if (Test-NonInteractiveReleaseSigningContext) {
        throw "Non-interactive release signing requires environment variable '$EnvironmentVariableName' when using certificate path '$CertificatePath'."
    }

    return (Read-Host -Prompt "Enter the PFX password for $CertificatePath" -AsSecureString)
}

function Get-PfxCertificateMetadata {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [securestring]$Password
    )

    $marshal = [System.Runtime.InteropServices.Marshal]
    $bstr = [System.IntPtr]::Zero
    $collection = $null

    try {
        $bstr = $marshal::SecureStringToBSTR($Password)
        $plainTextPassword = $marshal::PtrToStringBSTR($bstr)

        $collection = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2Collection
        $collection.Import(
            $Path,
            $plainTextPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)

        $primaryCertificate = @($collection | Where-Object { $_.HasPrivateKey } | Select-Object -First 1)
        if ($primaryCertificate.Count -eq 0) {
            $primaryCertificate = @($collection | Select-Object -First 1)
        }

        if ($primaryCertificate.Count -eq 0) {
            throw 'PFX metadata inspection did not find any certificates.'
        }

        return [ordered]@{
            PrimaryThumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$primaryCertificate[0].Thumbprint)
            Thumbprints = @($collection | ForEach-Object { Normalize-SignerThumbprint -Thumbprint ([string]$_.Thumbprint) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        }
    }
    finally {
        if ($collection -is [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]) {
            foreach ($certificate in $collection) {
                if ($certificate -is [System.Security.Cryptography.X509Certificates.X509Certificate2]) {
                    $certificate.Reset()
                }
            }
        }

        if ($bstr -ne [System.IntPtr]::Zero) {
            $marshal::ZeroFreeBSTR($bstr)
        }
    }
}

function Import-SigningCertificate {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [securestring]$Password
    )

    $importedCertificates = @(Import-PfxCertificate -FilePath $Path -CertStoreLocation 'Cert:\CurrentUser\My' -Password $Password)
    if ($importedCertificates.Count -eq 0) {
        throw 'Import-PfxCertificate did not return an imported certificate.'
    }

    $primaryCertificate = @($importedCertificates | Where-Object { $_.HasPrivateKey } | Select-Object -First 1)
    if ($primaryCertificate.Count -eq 0) {
        $primaryCertificate = @($importedCertificates | Select-Object -First 1)
    }

    return [ordered]@{
        Thumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$primaryCertificate[0].Thumbprint)
        ImportedThumbprints = @($importedCertificates | ForEach-Object { Normalize-SignerThumbprint -Thumbprint ([string]$_.Thumbprint) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
}

function Remove-ImportedSigningCertificates {
    param([string[]]$Thumbprints)

    if ([string]::Equals([string]$env:WPFDEVTOOLS_TEST_FORCE_SIGNING_CERTIFICATE_CLEANUP_FAILURE, '1', [System.StringComparison]::Ordinal)) {
        throw 'Simulated release signing certificate cleanup failure.'
    }

    foreach ($thumbprint in @($Thumbprints | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $certificatePath = Join-Path 'Cert:\CurrentUser\My' $thumbprint
        if (Test-Path $certificatePath) {
            Remove-Item -Path $certificatePath -Force
        }
    }
}

function Invoke-ReleasePayloadSigning {
    param(
        [Parameter(Mandatory)] [string]$SignaturePolicy,
        [Parameter(Mandatory)] [string[]]$PayloadPaths,
        [string]$CertificatePathParameter,
        [string]$CertificateThumbprintParameter,
        [string]$PasswordEnvironmentVariableParameter,
        [string]$TimestampServerParameter
    )

    if ($SignaturePolicy -ne 'RequireAuthenticodeSignature' -or (Test-InstallerTestModeEnabled)) {
        return
    }

    $signingInputs = Get-ReleaseSigningInputs `
        -CertificatePathParameter $CertificatePathParameter `
        -CertificateThumbprintParameter $CertificateThumbprintParameter `
        -PasswordEnvironmentVariableParameter $PasswordEnvironmentVariableParameter `
        -TimestampServerParameter $TimestampServerParameter

    if ([string]::IsNullOrWhiteSpace([string]$signingInputs.CertificatePath) -and
        [string]::IsNullOrWhiteSpace([string]$signingInputs.CertificateThumbprint)) {
        return
    }

    $signtoolPath = Resolve-SignToolPath
    if ([string]::IsNullOrWhiteSpace($signtoolPath) -or -not (Test-Path $signtoolPath)) {
        throw 'signtool.exe was not found. Install the Windows SDK or set WPFDEVTOOLS_SIGNTOOL_PATH before running signed release packaging.'
    }

    $importedThumbprints = @()
    $activeThumbprint = [string]$signingInputs.CertificateThumbprint
    $certificateAlreadyInstalled = $false
    $shouldImportCertificate = -not [string]::IsNullOrWhiteSpace([string]$signingInputs.CertificatePath)
    $certificatePassword = $null
    $preexistingImportedThumbprints = @()

    if (-not [string]::IsNullOrWhiteSpace($activeThumbprint)) {
        $certificateAlreadyInstalled = Test-Path (Join-Path 'Cert:\CurrentUser\My' $activeThumbprint)
        if ($certificateAlreadyInstalled) {
            $shouldImportCertificate = $false
        }
    }

    if ($shouldImportCertificate) {
        if (-not (Test-Path $signingInputs.CertificatePath)) {
            throw "Release signing certificate was not found: $($signingInputs.CertificatePath)"
        }

        $certificatePassword = Get-CertificatePassword `
            -EnvironmentVariableName $signingInputs.PasswordEnvironmentVariable `
            -CertificatePath $signingInputs.CertificatePath
        $certificateMetadata = Get-PfxCertificateMetadata -Path $signingInputs.CertificatePath -Password $certificatePassword
        if ([string]::IsNullOrWhiteSpace($activeThumbprint)) {
            $activeThumbprint = [string]$certificateMetadata.PrimaryThumbprint
        }
        $preexistingImportedThumbprints = @($certificateMetadata.Thumbprints | Where-Object { Test-Path (Join-Path 'Cert:\CurrentUser\My' $_) })
        if (-not $certificateAlreadyInstalled) {
            $certificateAlreadyInstalled = $preexistingImportedThumbprints -contains $activeThumbprint
            if ($certificateAlreadyInstalled) {
                $shouldImportCertificate = $false
            }
        }
    }

    $signingFailure = $null
    $cleanupFailure = $null

    try {
        if ($shouldImportCertificate) {
            if (-not (Test-Path $signingInputs.CertificatePath)) {
                throw "Release signing certificate was not found: $($signingInputs.CertificatePath)"
            }

            if ($null -eq $certificatePassword) {
                $certificatePassword = Get-CertificatePassword `
                    -EnvironmentVariableName $signingInputs.PasswordEnvironmentVariable `
                    -CertificatePath $signingInputs.CertificatePath
            }
            $importResult = Import-SigningCertificate -Path $signingInputs.CertificatePath -Password $certificatePassword
            $activeThumbprint = [string]$importResult.Thumbprint
            $importedThumbprints = @($importResult.ImportedThumbprints)
        }
        elseif ([string]::IsNullOrWhiteSpace($activeThumbprint)) {
            throw 'Release signing requires either an installed certificate thumbprint or a certificate path.'
        }

        foreach ($payloadPath in $PayloadPaths) {
            if (-not (Test-Path $payloadPath)) {
                throw "Release signing payload was not found: $payloadPath"
            }

            $result = & $signtoolPath sign `
                /sha1 $activeThumbprint `
                /s My `
                /fd SHA256 `
                /tr $signingInputs.TimestampServer `
                /td SHA256 `
                /v `
                $payloadPath 2>&1

            if ($LASTEXITCODE -ne 0) {
                $details = ($result | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
                throw "signtool.exe failed while signing '$payloadPath'. $details"
            }
        }
    }
    catch {
        $signingFailure = $_.Exception
    }

    if (-not $certificateAlreadyInstalled) {
        if (-not $certificateAlreadyInstalled) {
            $newlyImportedThumbprints = @($importedThumbprints | Where-Object { $preexistingImportedThumbprints -notcontains $_ })
            try {
                Remove-ImportedSigningCertificates -Thumbprints $newlyImportedThumbprints
            }
            catch {
                $cleanupFailure = $_.Exception
            }
        }
    }

    if ($null -ne $signingFailure) {
        $failureMessage = [string]$signingFailure.Message
        if ($null -ne $cleanupFailure) {
            $failureMessage += " Certificate cleanup also failed: $($cleanupFailure.Message)"
        }

        throw $failureMessage
    }

    if ($null -ne $cleanupFailure) {
        throw "Release signing certificate cleanup failed. $($cleanupFailure.Message)"
    }
}

function Get-FileAuthenticodeSignatureDetails {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Signature validation target was not found: $Path"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_TEST_SIGNATURE_STATUS is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        $forcedStatus = [string]$env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS
        if ([string]::Equals($forcedStatus, 'Valid', [System.StringComparison]::OrdinalIgnoreCase)) {
            return [ordered]@{
                Status = 'Valid'
                Thumbprint = 'TESTSIGNER00000000000000000000000000000000'
                Subject = 'CN=WPFDEVTOOLS TEST SIGNER'
            }
        }

        throw "Release packaging requires an Authenticode-signed payload. '$Path' reported signature status '$forcedStatus'."
    }

    $signature = Get-AuthenticodeSignature -FilePath $Path
    return [ordered]@{
        Status = [string]$signature.Status
        Thumbprint = if ($null -ne $signature.SignerCertificate) {
            Normalize-SignerThumbprint -Thumbprint ([string]$signature.SignerCertificate.Thumbprint)
        }
        else {
            $null
        }
        Subject = if ($null -ne $signature.SignerCertificate) {
            [string]$signature.SignerCertificate.Subject
        }
        else {
            $null
        }
    }
}

function Assert-FileAuthenticodeSignature {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [string]$ExpectedThumbprint,
        [string]$ExpectedSubject
    )

    $signatureInfo = Get-FileAuthenticodeSignatureDetails -Path $Path
    if ($signatureInfo.Status -ne 'Valid') {
        throw "Release packaging requires an Authenticode-signed payload. '$Path' reported signature status '$($signatureInfo.Status)'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedThumbprint) -and
        -not [string]::Equals([string]$signatureInfo.Thumbprint, $ExpectedThumbprint, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release packaging requires signer thumbprint '$ExpectedThumbprint'. '$Path' reported signer '$([string]$signatureInfo.Thumbprint)'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSubject) -and
        -not [string]::Equals([string]$signatureInfo.Subject, $ExpectedSubject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release packaging requires signer subject '$ExpectedSubject'. '$Path' reported signer '$([string]$signatureInfo.Subject)'."
    }

    return $signatureInfo
}

function Assert-ReleasePayloadSignaturePolicy {
    param(
        [Parameter(Mandatory)] [string]$SignaturePolicy,
        [Parameter(Mandatory)] [string[]]$PayloadPaths
    )

    if ($SignaturePolicy -ne 'RequireAuthenticodeSignature') {
        return
    }

    $signerPin = Get-ReleaseSignerPin
    if (-not (Test-InstallerTestModeEnabled) -and -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS)) {
        throw 'WPFDEVTOOLS_TEST_SIGNATURE_STATUS is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }

    if (-not (Test-InstallerTestModeEnabled) -and [string]::IsNullOrWhiteSpace([string]$signerPin.Thumbprint)) {
        throw 'Release packaging requires WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT when signaturePolicy is RequireAuthenticodeSignature.'
    }

    $observedSigner = $null

    foreach ($payloadPath in $PayloadPaths) {
        $signatureInfo = Assert-FileAuthenticodeSignature -Path $payloadPath -ExpectedThumbprint ([string]$signerPin.Thumbprint) -ExpectedSubject ([string]$signerPin.Subject)
        if ($null -eq $observedSigner) {
            $observedSigner = $signatureInfo
            continue
        }

        if (-not [string]::Equals([string]$observedSigner.Thumbprint, [string]$signatureInfo.Thumbprint, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$observedSigner.Subject, [string]$signatureInfo.Subject, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Release packaging requires every signed payload to use the same signer. '$payloadPath' does not match the signer used by the rest of the package."
        }
    }

    return $observedSigner
}

function Invoke-ArchiveCreation {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ArchivePath
    )

    $retryDelayMilliseconds = 250
    $maxAttempts = 5

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            Compress-Archive -Path (Join-Path $PackageDirectory '*') -DestinationPath $ArchivePath -Force
            return
        }
        catch {
            if ($attempt -eq $maxAttempts) {
                throw
            }

            Start-Sleep -Milliseconds $retryDelayMilliseconds
        }
    }
}

function Resolve-MSBuildPath {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH)) {
        return $env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH
    }

    $command = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere) {
        $resolved = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools or add MSBuild.exe to PATH.'
}

function Get-VisualStudioInstallationRoot {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    $msbuildDirectory = Split-Path -Parent $ResolvedMsBuildPath
    if ([string]::IsNullOrWhiteSpace($msbuildDirectory)) {
        return $null
    }

    $currentDirectory = Split-Path -Parent $msbuildDirectory
    if ([string]::IsNullOrWhiteSpace($currentDirectory) -or
        (Split-Path $currentDirectory -Leaf) -ne 'Current') {
        return $null
    }

    $msbuildRoot = Split-Path -Parent $currentDirectory
    if ([string]::IsNullOrWhiteSpace($msbuildRoot) -or
        (Split-Path $msbuildRoot -Leaf) -ne 'MSBuild') {
        return $null
    }

    return Split-Path -Parent $msbuildRoot
}

function Test-Arm64ToolchainInstalled {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    $visualStudioRoot = Get-VisualStudioInstallationRoot -ResolvedMsBuildPath $ResolvedMsBuildPath
    if ([string]::IsNullOrWhiteSpace($visualStudioRoot)) {
        return $true
    }

    $msvcRoot = Join-Path $visualStudioRoot 'VC\Tools\MSVC'
    if (-not (Test-Path $msvcRoot)) {
        return $false
    }

    $toolDirectories = Get-ChildItem -Path $msvcRoot -Directory -ErrorAction SilentlyContinue
    foreach ($toolDirectory in $toolDirectories) {
        $compilerCandidates = @(
            (Join-Path $toolDirectory.FullName 'bin\Hostx64\arm64\cl.exe'),
            (Join-Path $toolDirectory.FullName 'bin\Hostx86\arm64\cl.exe')
        )

        foreach ($compilerCandidate in $compilerCandidates) {
            if (Test-Path $compilerCandidate) {
                return $true
            }
        }
    }

    return $false
}

function Assert-ArchitectureToolchainAvailable {
    param(
        [Parameter(Mandatory)] [string[]]$ResolvedArchitectures,
        [Parameter(Mandatory)] [string]$ResolvedMsBuildPath
    )

    if ($ResolvedArchitectures -notcontains 'arm64') {
        return
    }

    if (-not (Test-Arm64ToolchainInstalled -ResolvedMsBuildPath $ResolvedMsBuildPath)) {
        throw 'ARM64 bootstrapper build requires the Visual Studio v143 ARM64 C++ toolchain. Install component Microsoft.VisualStudio.Component.VC.Tools.ARM64 and rerun scripts/tools/build-release.ps1.'
    }
}

function Write-ReleaseSidecars {
    param(
        [Parameter(Mandatory)] [string]$PackagingScriptRoot,
        [Parameter(Mandatory)] [string]$ArchiveRoot,
        [Parameter(Mandatory)] [string]$Version
    )

    $sidecarWriter = Join-Path $PackagingScriptRoot 'Write-ReleaseSidecars.ps1'
    if (-not (Test-Path $sidecarWriter)) {
        throw "Write-ReleaseSidecars.ps1 was not found: $sidecarWriter"
    }

    $tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
    & $sidecarWriter -ArchiveRoot $ArchiveRoot -Tag $tag | Out-Null
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$serverProject = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj'
$inspectorProject = Join-Path $repoRoot 'src\WpfDevTools.Inspector\WpfDevTools.Inspector.csproj'
$bootstrapperProject = Join-Path $repoRoot 'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj'
$installScript = Join-Path $repoRoot 'scripts\online-installer.ps1'
$installBatchTemplate = Join-Path $repoRoot 'scripts\tools\packaging\run-template.bat'
$outputRootFullPath = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputRoot)).Path
$msbuildPath = Resolve-MSBuildPath

[xml]$serverProjectXml = Get-Content -Path $serverProject
$version = $serverProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0-dev'
}

$resolvedArchitectures = Resolve-ArchitectureList -InputArchitectures $Architectures
Assert-ArchitectureToolchainAvailable -ResolvedArchitectures $resolvedArchitectures -ResolvedMsBuildPath $msbuildPath
foreach ($architecture in $resolvedArchitectures) {
    $runtimeId = Get-RuntimeId -Architecture $architecture
    $bootstrapperPlatform = Get-BootstrapperPlatform -Architecture $architecture
    $channel = Get-PackageChannel -BuildConfiguration $Configuration
    $signaturePolicy = Get-SignaturePolicy -BuildConfiguration $Configuration
    $packageDir = Join-Path $outputRootFullPath "release_${version}_win-$architecture"
    $packageArchiveName = "release_${version}_win-$architecture.zip"
    $packageArchivePath = Join-Path $outputRootFullPath $packageArchiveName
    $binDir = Join-Path $packageDir 'bin'
    $serverBuildSource = Resolve-ServerOutputSource -RepositoryRoot $repoRoot -BuildConfiguration $Configuration -RuntimeId $runtimeId -UseExistingBuildOutput $SkipBuild.IsPresent

    Remove-PathIfExists -Path $packageDir
    Remove-PathIfExists -Path $packageArchivePath

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $inspectorNet8Dir = Join-Path $binDir 'inspectors\net8.0-windows'
    $inspectorNet48Dir = Join-Path $binDir 'inspectors\net48'
    $bootstrapperDir = Join-Path $binDir (Join-Path 'bootstrapper' $architecture)
    New-Item -ItemType Directory -Force -Path $binDir, $inspectorNet8Dir, $inspectorNet48Dir, $bootstrapperDir | Out-Null

    try {
        if ($SkipBuild) {
            Copy-ServerBuildOutput -SourceInfo $serverBuildSource -Destination $binDir
        }
        else {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'publish', $serverProject,
                '-c', $Configuration,
                '-r', $runtimeId,
                '--self-contained', 'false',
                '-o', $binDir
            )
        }

        if (-not $SkipBuild) {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'build', $inspectorProject,
                '-c', $Configuration,
                '-f', 'net8.0-windows'
            )
        }

        Invoke-Step -FilePath 'dotnet' -Arguments @(
            'build', $inspectorProject,
            '-c', $Configuration,
            '-f', 'net48'
        )

        Invoke-Step -FilePath $msbuildPath -Arguments @(
            $bootstrapperProject,
            "/p:Configuration=$Configuration",
            "/p:Platform=$bootstrapperPlatform"
        )

        $inspectorNet8BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net8.0-windows"
        $inspectorNet48BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net48"
        $bootstrapperSource = Join-Path $repoRoot "artifacts\bootstrapper\$Configuration\$bootstrapperPlatform\WpfDevTools.Bootstrapper.$architecture.dll"

        $packagedExecutableName = "wpf-devtools-$architecture.exe"
        $serverExecutablePath = Join-Path $binDir 'WpfDevTools.Mcp.Server.exe'
        if (Test-Path $serverExecutablePath) {
            Rename-Item -Path $serverExecutablePath -NewName $packagedExecutableName -Force
        }

        Copy-DirectoryFilesOnly -Source $inspectorNet8BuildDir -Destination $inspectorNet8Dir
        Copy-DirectoryContents -Source $inspectorNet48BuildDir -Destination $inspectorNet48Dir
        $bootstrapperDestination = Join-Path $bootstrapperDir (Split-Path $bootstrapperSource -Leaf)
        Copy-Item -Path $bootstrapperSource -Destination $bootstrapperDestination -Force
        Copy-Item -Path $installBatchTemplate -Destination (Join-Path $packageDir 'run.bat') -Force
        Copy-Item -Path $installScript -Destination (Join-Path $binDir 'install.ps1') -Force
        Copy-InstallerHelperFiles -RepositoryRoot $repoRoot -Destination (Join-Path $binDir 'installer')

        $payloadPaths = @(
            (Join-Path $binDir $packagedExecutableName)
            (Join-Path $inspectorNet8Dir 'WpfDevTools.Inspector.dll')
            (Join-Path $inspectorNet48Dir 'WpfDevTools.Inspector.dll')
            $bootstrapperDestination
        )

        Invoke-ReleasePayloadSigning `
            -SignaturePolicy $signaturePolicy `
            -PayloadPaths $payloadPaths `
            -CertificatePathParameter $SigningCertificatePath `
            -CertificateThumbprintParameter $SigningCertificateThumbprint `
            -PasswordEnvironmentVariableParameter $SigningPasswordEnvironmentVariable `
            -TimestampServerParameter $SigningTimestampServer

        $payloadSigner = Assert-ReleasePayloadSignaturePolicy -SignaturePolicy $signaturePolicy -PayloadPaths $payloadPaths

        $manifest = [ordered]@{
            name = 'wpf-devtools'
            version = $version
            architecture = $architecture
            runtimeId = $runtimeId
            channel = $channel
            buildConfiguration = $Configuration
            signaturePolicy = $signaturePolicy
            entryExecutable = "bin/$packagedExecutableName"
            runBatch = 'run.bat'
            installScript = 'bin\install.ps1'
            inspector = [ordered]@{
                net8 = 'bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll'
                net48 = 'bin/inspectors/net48/WpfDevTools.Inspector.dll'
            }
            bootstrapper = "bin/bootstrapper/$architecture/WpfDevTools.Bootstrapper.$architecture.dll"
        }

        if ($null -ne $payloadSigner -and -not [string]::IsNullOrWhiteSpace([string]$payloadSigner.Thumbprint)) {
            $manifest.signerThumbprint = [string]$payloadSigner.Thumbprint
        }

        if ($null -ne $payloadSigner -and -not [string]::IsNullOrWhiteSpace([string]$payloadSigner.Subject)) {
            $manifest.signerSubject = [string]$payloadSigner.Subject
        }

        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $binDir 'manifest.json') -Encoding UTF8
        Invoke-ArchiveCreation -PackageDirectory $packageDir -ArchivePath $packageArchivePath
        Write-Host "Created package: $packageDir"
        Write-Host "Created archive: $packageArchivePath"
    }
    catch {
        $packagingError = $_.Exception
        $cleanupFailures = [System.Collections.Generic.List[string]]::new()

        try {
            Remove-PathIfExists -Path $packageArchivePath
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }

        try {
            Remove-PathIfExists -Path $packageDir
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }

        $failureMessage = "Failed to package architecture $architecture. $($packagingError.Message)"
        if ($cleanupFailures.Count -gt 0) {
            $failureMessage += " Cleanup also failed: $($cleanupFailures -join ' | ')"
        }

        throw $failureMessage
    }
}

Write-ReleaseSidecars -PackagingScriptRoot $PSScriptRoot -ArchiveRoot $outputRootFullPath -Version $version
