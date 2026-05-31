using System.Security.AccessControl;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseSigningScriptSecurityTests
{
    [Fact]
    public void ReleaseWorkflow_ShouldMaterializeSigningCertificateViaHardenedHelper()
    {
        var workflow = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(".github/workflows/release.yml"));

        workflow.Should().Contain("./scripts/tools/packaging/Write-ReleaseSigningCertificate.ps1",
            "release secrets should be materialized by a tested helper instead of inline workflow PowerShell");
        workflow.Should().NotContain("[System.Convert]::FromBase64String($env:WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64)",
            "the workflow must not decode signing PFX bytes inline where ACL hardening and zeroing can drift");
    }

    [Fact]
    public void ReleaseSigningCertificateMaterializer_ShouldClearDecodedBytesAndProtectAcl()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Write-ReleaseSigningCertificate.ps1");

        File.Exists(scriptPath).Should().BeTrue("release.yml must call a tracked helper script");
        var source = File.ReadAllText(scriptPath);

        source.Should().Contain("[Array]::Clear($certificateBytes, 0, $certificateBytes.Length)",
            "decoded signing PFX bytes must be wiped after writing");
        source.Should().Contain("SetAccessRuleProtection($true, $false)",
            "the materialized PFX must not inherit broad temp-directory ACLs");
        source.Should().Contain("Set-Acl -LiteralPath $Path",
            "the helper must apply the hardened ACL to the materialized certificate file");
    }

    [Fact]
    public void WriteReleaseSigningCertificate_ShouldWriteRestrictedFileAndGitHubEnv()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/packaging/Write-ReleaseSigningCertificate.ps1");
            var certificatePath = Path.Combine(tempRoot, "release-signing.pfx");
            var githubEnvPath = Path.Combine(tempRoot, "github-env.txt");
            byte[] expectedBytes = [0x50, 0x46, 0x58, 0x01, 0x02];

            var command = $$"""
                & '{{EscapePowerShellString(scriptPath)}}' `
                    -CertificatePath '{{EscapePowerShellString(certificatePath)}}' `
                    -GitHubEnvPath '{{EscapePowerShellString(githubEnvPath)}}'
                """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64"] = Convert.ToBase64String(expectedBytes)
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllBytes(certificatePath).Should().Equal(expectedBytes);
            File.ReadAllText(githubEnvPath).Should()
                .Contain($"WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH={certificatePath}");

            var security = new FileInfo(certificatePath).GetAccessControl();
            security.AreAccessRulesProtected.Should().BeTrue(
                "the materialized signing PFX must not inherit temp-directory permissions");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

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

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
