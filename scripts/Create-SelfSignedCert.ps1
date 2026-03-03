# Create Self-Signed Certificate for Code Signing
# This script creates a self-signed certificate for development and testing purposes
# For production, you should use a paid code signing certificate from a trusted CA

param(
    [string]$CertificateName = "WpfDevTools Development",
    [string]$OutputPath = ".\cert",
    [string]$Password = "DevPassword123!"
)

Write-Host "Creating self-signed certificate for code signing..." -ForegroundColor Green

# Create output directory if it doesn't exist
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$CertificateName" `
    -KeyUsage DigitalSignature `
    -FriendlyName $CertificateName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(3)

Write-Host "Certificate created with thumbprint: $($cert.Thumbprint)" -ForegroundColor Cyan

# Export certificate to PFX file (with private key)
$pfxPath = Join-Path $OutputPath "WpfDevTools.pfx"
$securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null

Write-Host "Certificate exported to: $pfxPath" -ForegroundColor Cyan

# Export certificate to CER file (public key only)
$cerPath = Join-Path $OutputPath "WpfDevTools.cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host "Public certificate exported to: $cerPath" -ForegroundColor Cyan

# Display certificate information
Write-Host "`nCertificate Information:" -ForegroundColor Yellow
Write-Host "  Subject: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Valid From: $($cert.NotBefore)"
Write-Host "  Valid To: $($cert.NotAfter)"

Write-Host "`nIMPORTANT NOTES:" -ForegroundColor Red
Write-Host "1. This is a SELF-SIGNED certificate for DEVELOPMENT/TESTING only"
Write-Host "2. Windows SmartScreen will NOT trust this certificate"
Write-Host "3. Users will see security warnings when running signed binaries"
Write-Host "4. For PRODUCTION, purchase a code signing certificate from:"
Write-Host "   - DigiCert (recommended)"
Write-Host "   - Sectigo"
Write-Host "   - GlobalSign"
Write-Host "   Cost: ~`$100-500 USD per year"

Write-Host "`nTo install the certificate on this machine:" -ForegroundColor Green
Write-Host "  1. Double-click $cerPath"
Write-Host "  2. Click 'Install Certificate'"
Write-Host "  3. Select 'Local Machine' (requires admin)"
Write-Host "  4. Place in 'Trusted Root Certification Authorities'"

Write-Host "`nTo use this certificate for signing:" -ForegroundColor Green
Write-Host "  .\scripts\Sign-Binaries.ps1 -CertificatePath '$pfxPath' -Password '$Password'"

Write-Host "`nDone!" -ForegroundColor Green
