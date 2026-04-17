# Code Signing Guide

This guide explains how to sign WPF DevTools binaries to avoid antivirus false positives and Windows SmartScreen warnings.

## Why Code Signing?

Code signing provides several benefits:
1. **Antivirus Trust**: Signed binaries are less likely to be flagged by antivirus software
2. **User Trust**: Users can verify the publisher and integrity of the software
3. **Windows SmartScreen**: Reduces or eliminates SmartScreen warnings
4. **DLL Injection**: Some antivirus software blocks unsigned DLL injection

## Option 1: Self-Signed Certificate (Free, Development Only)

### Create Self-Signed Certificate

```powershell
.\scripts\Create-SelfSignedCert.ps1
```

This creates:
- `cert/WpfDevTools.pfx` - Certificate with private key (for signing)
- `cert/WpfDevTools.cer` - Public certificate (for distribution)

### Install Certificate (Required for Testing)

1. Double-click `cert/WpfDevTools.cer`
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
.\scripts\Sign-Binaries.ps1 -CertificatePath ".\cert\WpfDevTools.pfx" -Password "<YOUR_STRONG_PASSWORD>"
```

### Limitations

⚠️ **IMPORTANT**: Self-signed certificates have significant limitations:
- Windows SmartScreen will NOT trust them
- Users will see security warnings
- Antivirus software may still flag binaries
- Only suitable for development and testing

## Option 2: Commercial Certificate (Recommended for Production)

### Purchase Certificate

Purchase a code signing certificate from a trusted Certificate Authority (CA):

| Provider | Type | Cost (Annual) | Notes |
|----------|------|---------------|-------|
| **DigiCert** | EV Code Signing | $400-500 | Best reputation, fastest validation |
| **Sectigo** | Standard Code Signing | $100-200 | Good value, slower validation |
| **GlobalSign** | EV Code Signing | $300-400 | Good reputation |

**Recommendation**: DigiCert EV Code Signing Certificate
- Immediate SmartScreen reputation
- No warnings on first download
- Best for open-source projects

### Certificate Types

**Standard Code Signing**:
- Certificate stored as file (.pfx)
- Can be copied and shared
- Requires building SmartScreen reputation over time
- Cheaper ($100-200/year)

**EV (Extended Validation) Code Signing**:
- Certificate stored on hardware token (USB)
- Cannot be copied
- Immediate SmartScreen reputation
- More expensive ($300-500/year)
- **Recommended for production**

### Validation Process

1. **Purchase Certificate**: Choose provider and certificate type
2. **Verify Identity**: Provide business documents (1-7 days)
3. **Receive Certificate**: Download or receive USB token
4. **Install Certificate**: Import to Windows certificate store
5. **Sign Binaries**: Use the same signing script

### Sign with Commercial Certificate

```powershell
# If certificate is in Windows certificate store
.\scripts\Sign-Binaries.ps1 -CertificatePath "Cert:\CurrentUser\My\<THUMBPRINT>" -Password ""

# If certificate is in PFX file
.\scripts\Sign-Binaries.ps1 -CertificatePath "C:\path\to\cert.pfx" -Password "YourPassword"
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

Add signing step to `.github/workflows/ci-cd.yml`:

```yaml
- name: Sign binaries
  if: github.ref == 'refs/heads/master'
  run: |
    .\scripts\Sign-Binaries.ps1 `
      -CertificatePath ${{ secrets.CERT_PATH }} `
      -Password ${{ secrets.CERT_PASSWORD }}
```

### Store Certificate Securely

1. Go to GitHub repository settings
2. Navigate to "Secrets and variables" → "Actions"
3. Add secrets:
   - `CERT_PATH`: Path to certificate file (or base64-encoded certificate)
   - `CERT_PASSWORD`: Certificate password

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
| Standard Code Signing | $100-200 | $100-200 | ⚠️ After reputation | ✅ Yes |
| EV Code Signing | $300-500 | $300-500 | ✅ Immediate | ✅ Yes |

## Recommendation

- **Development/Testing**: Use self-signed certificate
- **Open Source Release**: Use EV Code Signing certificate
- **Commercial Product**: Use EV Code Signing certificate

For WPF DevTools (open source), we recommend:
1. Start with self-signed for development
2. Upgrade to DigiCert EV Code Signing when ready for public release
3. Budget: ~$400/year for certificate

## Resources

- [Microsoft Code Signing Best Practices](https://docs.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-best-practices)
- [DigiCert Code Signing](https://www.digicert.com/signing/code-signing-certificates)
- [Sectigo Code Signing](https://sectigo.com/ssl-certificates-tls/code-signing)
- [SignTool Documentation](https://docs.microsoft.com/en-us/windows/win32/seccrypto/signtool)
