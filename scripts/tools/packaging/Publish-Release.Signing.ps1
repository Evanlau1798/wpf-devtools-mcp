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

function ConvertTo-ReadOnlySecureString {
    param([Parameter(Mandatory)] [string]$PlainText)

    $secureString = New-Object System.Security.SecureString
    $chars = $PlainText.ToCharArray()
    try {
        foreach ($char in $chars) {
            $secureString.AppendChar($char)
        }

        $secureString.MakeReadOnly()
        return $secureString
    }
    finally {
        [Array]::Clear($chars, 0, $chars.Length)
    }
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
        return (ConvertTo-ReadOnlySecureString -PlainText $passwordValue)
    }

    if (Test-NonInteractiveReleaseSigningContext) {
        throw "Non-interactive release signing requires environment variable '$EnvironmentVariableName' when using certificate path '$CertificatePath'."
    }

    return (Read-Host -Prompt "Enter the PFX password for $CertificatePath" -AsSecureString)
}

function Initialize-CertificateProvider {
    # Hosted Windows runners can start without a Cert: drive until the certificate provider is explicitly loaded.
    Remove-TypeData -TypeName System.Security.AccessControl.ObjectSecurity -ErrorAction SilentlyContinue
    Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
    try { Import-Module PKI -ErrorAction Stop } catch { }

    if ($null -eq (Get-PSProvider Certificate -ErrorAction SilentlyContinue)) {
        throw 'Certificate provider is unavailable.'
    }

    if ($null -eq (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) {
        New-PSDrive -Name Cert -PSProvider Certificate -Root '\' -ErrorAction Stop | Out-Null
    }
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

    Initialize-CertificateProvider
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

    Initialize-CertificateProvider

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

    if ($certificatePassword -is [System.Security.SecureString]) {
        $certificatePassword.Dispose()
        $certificatePassword = $null
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
