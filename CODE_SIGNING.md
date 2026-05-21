# Code Signing Guide

This guide explains how to sign WPF DevTools binaries to reduce antivirus false positives, preserve file integrity, and show a verified publisher. For non-Store downloads, Windows SmartScreen reputation still builds over time even when files are signed.

## Why Code Signing?

Code signing provides several benefits:
1. **Antivirus Trust**: Signed binaries are less likely to be flagged by antivirus software
2. **User Trust**: Users can verify the publisher and integrity of the software
3. **Windows SmartScreen**: Shows a verified publisher and allows reputation to build across signed releases
4. **DLL Injection**: Some antivirus software blocks unsigned DLL injection

## Option 1: Self-Signed Certificate (Free, Development Only)

### Create Self-Signed Certificate

```powershell
.\scripts\tools\Create-SelfSignedCert.ps1 -Password "<YOUR_STRONG_PASSWORD>"
```

This creates:
- `tmp/cert/WpfDevTools.pfx` - Certificate with private key (for signing)
- `tmp/cert/WpfDevTools.cer` - Public certificate (for distribution)

### Install Certificate (Required for Testing)

1. Double-click `tmp/cert/WpfDevTools.cer`
2. Click "Install Certificate"
3. Select "Local Machine" (requires administrator)
4. Place in "Trusted Root Certification Authorities"
5. Click "Finish"

### Sign Binaries

```powershell
# Build in Release mode first
dotnet build -c Release

# Sign all binaries (replace the placeholder below with a strong local-only secret;
# never reuse production passwords and never commit the secret to source control)
$env:WPFDEVTOOLS_PFX_PASSWORD = "<YOUR_STRONG_PASSWORD>"
.\scripts\tools\Sign-Binaries.ps1 -CertificatePath ".\tmp\cert\WpfDevTools.pfx"
```

### Limitations

⚠️ **IMPORTANT**: Self-signed certificates have significant limitations:
- Windows SmartScreen will NOT trust them
- Users will see security warnings
- Antivirus software may still flag binaries
- Only suitable for development and testing

## Option 2: Production Signing

### Choose a signing path

Choose a signing path that matches your distribution model:

| Option | Type | Cost | Notes |
|--------|------|------|-------|
| **Microsoft Store** | Store signing | Free after Store enrollment | Most reliable way to avoid SmartScreen download prompts for Store-installed apps |
| **Azure Artifact Signing** | Microsoft-managed signing | About $10/month | Recommended by Microsoft for non-Store distribution where available; reputation builds over time |
| **OV certificate** | Traditional CA code signing | Often $150-300/year | Valid option when Azure Artifact Signing is unavailable or a specific CA is required |
| **EV certificate** | Extended Validation code signing | Often $400+/year | Still valid for signing, but no longer provides SmartScreen first-download bypass |

**Recommendation**: Use Microsoft Store distribution when feasible. For non-Store releases, prefer Azure Artifact Signing where available, or an OV certificate from a CA that meets your release and procurement needs. For qualifying open-source projects, also evaluate SignPath Foundation.

### Certificate Types

**Azure Artifact Signing / OV Code Signing**:
- Certificate stored as file (.pfx)
- Azure Artifact Signing is Microsoft-managed and CI/CD friendly
- Traditional OV certificates may require a hardware token or HSM-backed key storage depending on CA/browser requirements
- SmartScreen reputation builds over time for new apps and new files
- Signed files show a verified publisher when the chain is trusted

**EV (Extended Validation) Code Signing**:
- Certificate stored on hardware token (USB)
- Cannot be copied
- More expensive than OV
- EV certificates no longer bypass SmartScreen on first download
- Use EV only when enterprise procurement or organizational policy specifically requires it

### Validation Process

1. **Purchase Certificate**: Choose provider and certificate type
2. **Verify Identity**: Provide business documents (1-7 days)
3. **Receive Certificate**: Download or receive USB token
4. **Install Certificate**: Import to Windows certificate store
5. **Sign Binaries**: Use the same signing script

### Sign with Commercial Certificate

```powershell
# If the certificate is already in the Windows certificate store
.\scripts\tools\Sign-Binaries.ps1 -CertificateThumbprint "<THUMBPRINT>"

# If certificate is in a PFX file
$env:WPFDEVTOOLS_PFX_PASSWORD = "<PFX_PASSWORD>"
.\scripts\tools\Sign-Binaries.ps1 -CertificatePath "C:\path\to\cert.pfx"
```

## Verify Signature

### Using PowerShell

```powershell
Get-AuthenticodeSignature ".\src\WpfDevTools.Mcp.Server\bin\Release\net8.0\WpfDevTools.Mcp.Server.exe"
```

Expected output:
```
SignerCertificate      : [Subject]
                           CN=WpfDevTools Development
                         [Issuer]
                           CN=WpfDevTools Development
                         [Serial Number]
                           ...
                         [Not Before]
                           ...
                         [Not After]
                           ...
                         [Thumbprint]
                           ...

TimeStamperCertificate :
Status                 : Valid
StatusMessage          : Signature verified.
Path                   : ...
```

### Using File Properties

1. Right-click the binary file
2. Select "Properties"
3. Go to "Digital Signatures" tab
4. Select the signature and click "Details"

## CI/CD Integration

### GitHub Actions

The production GitHub Release workflow should materialize the certificate from a base64 secret and let `Publish-Release.ps1` sign the packaged payloads directly:

```yaml
- name: Materialize signing certificate
  env:
    WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64: ${{ secrets.WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64 }}
  run: |
    $certificatePath = Join-Path $env:RUNNER_TEMP 'wpf-devtools-release-signing.pfx'
    [System.IO.File]::WriteAllBytes(
      $certificatePath,
      [System.Convert]::FromBase64String($env:WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64))
    "WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH=$certificatePath" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8

- name: Build release packages
  env:
    WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT: ${{ secrets.WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT }}
    WPFDEVTOOLS_PFX_PASSWORD: ${{ secrets.WPFDEVTOOLS_PFX_PASSWORD }}
  run: |
    .\scripts\tools\packaging\Publish-Release.ps1 -Configuration Release
```

For hosted-runner smoke validation without production signing material, use:

```yaml
env:
  WPFDEVTOOLS_INSTALLER_TEST_MODE: '1'
  WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA: '1'
  WPFDEVTOOLS_TEST_SIGNATURE_STATUS: Valid
```

When `WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH` points at a PFX file in CI, `WPFDEVTOOLS_PFX_PASSWORD` must be present and non-empty. `Publish-Release.ps1` now fails fast instead of falling back to an interactive prompt.

### Store Certificate Securely

1. Go to GitHub repository settings
2. Navigate to "Secrets and variables" → "Actions"
3. Add secrets:
   - `WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64`: Base64-encoded PFX content for hosted runners
   - `WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT`: Expected release signer thumbprint used for package verification
   - `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT`: Optional signer subject pin
   - `WPFDEVTOOLS_PFX_PASSWORD`: Certificate password

**Security Note**: Never commit certificate files or passwords to git!

## Troubleshooting

### signtool.exe not found

Install Windows SDK:
https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/

Or install via Visual Studio Installer:
- Individual components → Windows SDK

### Timestamp server timeout

Try different timestamp servers:
- `http://timestamp.digicert.com` (default)
- `http://timestamp.sectigo.com`
- `http://timestamp.globalsign.com`

### Certificate not trusted

For self-signed certificates:
1. Install certificate in "Trusted Root Certification Authorities"
2. Restart applications that need to verify the signature

For commercial certificates:
1. Ensure certificate chain is complete
2. Check certificate is not expired
3. Verify certificate is issued by trusted CA

## Best Practices

1. **Protect Private Key**: Never share or commit certificate files
2. **Use Strong Password**: Protect PFX files with strong passwords
3. **Timestamp Signatures**: Always use timestamp server (signatures remain valid after certificate expires)
4. **Sign All Binaries**: Sign all DLLs and EXEs, not just the main executable
5. **Verify After Signing**: Always verify signatures after signing
6. **Backup Certificate**: Keep secure backups of certificate files
7. **Rotate Certificates**: Renew certificates before expiration

## Cost Comparison

| Approach | Initial Cost | Annual Cost | SmartScreen Trust | Antivirus Trust |
|----------|--------------|-------------|-------------------|-----------------|
| Self-Signed | Free | Free | ❌ No | ⚠️ Limited |
| Azure Artifact Signing | About $10/month | About $10/month | ⚠️ After reputation | ✅ Yes |
| OV Code Signing | $150-300 | $150-300 | ⚠️ After reputation | ✅ Yes |
| EV Code Signing | $400+ | $400+ | ⚠️ After reputation | ✅ Yes |

## Recommendation

- **Development/Testing**: Use self-signed certificate
- **Open Source Release**: Evaluate SignPath Foundation, Azure Artifact Signing where available, or an OV certificate
- **Commercial Product**: Prefer Microsoft Store distribution where feasible; otherwise use Azure Artifact Signing or OV signing

For WPF DevTools (open source), we recommend:
1. Start with self-signed for development
2. Evaluate SignPath Foundation or Azure Artifact Signing for public release
3. Use an OV certificate when managed signing is unavailable or a traditional CA is required

## Resources

- [Microsoft Code Signing Best Practices](https://docs.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-best-practices)
- [DigiCert Code Signing](https://www.digicert.com/signing/code-signing-certificates)
- [Sectigo Code Signing](https://sectigo.com/ssl-certificates-tls/code-signing)
- [SignTool Documentation](https://docs.microsoft.com/en-us/windows/win32/seccrypto/signtool)
