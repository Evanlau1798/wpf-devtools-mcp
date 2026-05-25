using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseSigningScriptSecurityTests
{
    [Fact]
    public void InvokeReleasePayloadSigning_WhenMetadataInspectionFails_ShouldDisposeCertificatePassword()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Publish-Release.Signing.ps1");

        var command = $$"""
            . '{{scriptPath}}'

            $certificatePath = Join-Path $env:TEMP 'test-signing-cert.pfx'
            $payloadPath = Join-Path $env:TEMP 'payload.exe'
            $signToolPath = Join-Path $env:TEMP 'signtool.exe'
            Set-Content -LiteralPath $certificatePath -Value 'not a pfx'
            Set-Content -LiteralPath $payloadPath -Value 'payload'
            Set-Content -LiteralPath $signToolPath -Value 'fake signtool'

            function Initialize-CertificateProvider { }
            function Resolve-SignToolPath { return $signToolPath }
            function Get-CertificatePassword {
                $script:ObservedPassword = New-Object System.Security.SecureString
                $script:ObservedPassword.AppendChar('x')
                return $script:ObservedPassword
            }
            function Get-PfxCertificateMetadata { throw 'metadata inspection failed' }

            try {
                Invoke-ReleasePayloadSigning `
                    -SignaturePolicy 'RequireAuthenticodeSignature' `
                    -PayloadPaths @($payloadPath) `
                    -CertificatePathParameter $certificatePath `
                    -CertificateThumbprintParameter '' `
                    -PasswordEnvironmentVariableParameter 'WPFDEVTOOLS_PFX_PASSWORD' `
                    -TimestampServerParameter 'https://timestamp.example.invalid'
            }
            catch {
                Write-Output "caught=$($_.Exception.Message)"
            }

            try {
                $script:ObservedPassword.AppendChar('y')
                Write-Output 'passwordDisposed=false'
            }
            catch [System.ObjectDisposedException] {
                Write-Output 'passwordDisposed=true'
            }
            """;

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(
            command,
            new Dictionary<string, string?>
            {
                ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                ["WPFDEVTOOLS_PFX_PASSWORD"] = "test-password"
            });

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("caught=metadata inspection failed");
        result.Stdout.Should().Contain("passwordDisposed=true");
    }
}
