function Import-TrustedCodeSigningCertificate {
    param(
        [string]$CertificatePath,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        return
    }

    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Trusted code-signing certificate was not found: $CertificatePath"
    }

    $certificateExtension = [System.IO.Path]::GetExtension($CertificatePath)
    if ($certificateExtension -notin @('.cer', '.crt')) {
        throw 'Trusted code-signing certificate must be a .cer or .crt file.'
    }

    $certutilPath = Join-Path $env:SystemRoot 'System32\certutil.exe'
    if (-not (Test-Path -LiteralPath $certutilPath)) {
        throw "certutil.exe was not found: $certutilPath"
    }

    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        Invoke-ExternalWithTimeout `
            -Name "Trust code-signing certificate $storeName" `
            -FilePath $certutilPath `
            -Arguments @('-f', '-addstore', $storeName, $CertificatePath) `
            -TimeoutSeconds 30 `
            -OutputRoot $OutputRoot `
            -Timestamp "$Timestamp-Trust-code-signing-certificate-$storeName"
    }
}
