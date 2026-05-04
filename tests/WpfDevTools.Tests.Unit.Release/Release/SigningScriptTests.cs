using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class SigningScriptTests
{
    private const string TrustedSidecarSignerThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    [Fact]
    public void SignBinariesScript_ShouldAvoidPassingPfxPasswordsToSigntoolArguments()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"));

        content.Should().NotContain("/p $Password");
        content.Should().Contain("Import-PfxCertificate",
            "the script should import the PFX with a secure password handling path and sign by thumbprint instead of exposing the password in process arguments");
        content.Should().Contain("CertificateThumbprint");
        content.Should().Contain("https://",
            "timestamp traffic for production signing should use HTTPS");
    }

    [Fact]
    public void SignBinariesScript_ShouldAlwaysCleanupImportedCertificatesInFinally()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"));

        content.Should().Contain("finally",
            "PFX imports must be cleaned up even when signtool fails");
        content.Should().MatchRegex(
            @"(?s)finally\s*\{[^}]*Remove-ImportedSigningCertificates",
            "certificate-store cleanup must live in a finally block so failure paths cannot skip it");
    }

    [Fact]
    public void SignBinariesScript_ShouldNotImportExportablePrivateKeys()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"));

        content.Should().NotContain("-Exportable",
            "temporary signing imports should not keep an exportable private key in the CurrentUser certificate store");
    }

    [Fact]
    public void SignBinariesScript_ShouldOnlyUseProcessScopedPasswordEnvironmentVariables()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"));

        content.Should().Contain("GetEnvironmentVariable($EnvironmentVariableName, 'Process')");
        content.Should().NotContain("GetEnvironmentVariable($EnvironmentVariableName, 'User')",
            "persisted user-scope password variables are too broad for transient code-signing secrets");
        content.Should().NotContain("GetEnvironmentVariable($EnvironmentVariableName, 'Machine')",
            "machine-scope password variables are too broad for transient code-signing secrets");
    }

    [Fact]
    public void SignBinariesScript_ShouldSupportSigntoolAndRootOverridesForDeterministicTests()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "signtool.log");
            var fakeSigntool = ReleaseScriptTestHarness.CreateFakeCommand(tempRoot, "fake-signtool", logPath);
            var fakeRoot = Path.Combine(tempRoot, "fake-build-root");
            var binaryDir = Path.Combine(fakeRoot, "src", "Sample", "bin", "Release");
            Directory.CreateDirectory(binaryDir);
            File.WriteAllText(Path.Combine(binaryDir, "WpfDevTools.Sample.exe"), "stub");

            var certificatePath = Path.Combine(tempRoot, "dummy.pfx");
            File.WriteAllText(certificatePath, "not-a-real-pfx");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"),
                new[]
                {
                    "-CertificateThumbprint", "ABCD1234",
                    "-BuildConfiguration", "Release"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_SIGN_BINARIES_ROOTS"] = fakeRoot
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var invocation = File.ReadAllText(logPath);
            invocation.Should().Contain("WpfDevTools.Sample.exe");
            invocation.Should().Contain("/sha1");
            invocation.Should().Contain("ABCD1234");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SignBinariesScript_ShouldCleanupImportedCertificatesWhenSigningFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var certificateName = "WpfDevTools Cleanup Test " + Guid.NewGuid().ToString("N");
            var certOutputRoot = Path.Combine(tempRoot, "cert-output");
            var createResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Create-SelfSignedCert.ps1"),
                new[]
                {
                    "-CertificateName", certificateName,
                    "-OutputPath", certOutputRoot,
                    "-Password", "CleanupTest123!"
                });

            createResult.ExitCode.Should().Be(0, createResult.Stderr);

            var thumbprintMatch = Regex.Match(
                createResult.Stdout,
                @"Thumbprint:\s*(?<thumbprint>[A-Fa-f0-9]+)",
                RegexOptions.CultureInvariant);
            thumbprintMatch.Success.Should().BeTrue(createResult.Stdout);

            var thumbprint = thumbprintMatch.Groups["thumbprint"].Value;
            var pfxPath = Path.Combine(certOutputRoot, "WpfDevTools.pfx");
            File.Exists(pfxPath).Should().BeTrue();

            var removeOriginalCertificate = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}' -Force -ErrorAction SilentlyContinue");
            removeOriginalCertificate.ExitCode.Should().Be(0, removeOriginalCertificate.Stderr);

            var fakeRoot = Path.Combine(tempRoot, "fake-build-root");
            var binaryDir = Path.Combine(fakeRoot, "src", "Sample", "bin", "Release");
            Directory.CreateDirectory(binaryDir);
            File.WriteAllText(Path.Combine(binaryDir, "WpfDevTools.Sample.exe"), "stub");

            var fakeSigntool = Path.Combine(tempRoot, "fake-signtool.cmd");
            File.WriteAllText(
                fakeSigntool,
                "@echo off" + Environment.NewLine +
                "exit /b 1" + Environment.NewLine);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"),
                new[]
                {
                    "-CertificatePath", pfxPath,
                    "-BuildConfiguration", "Release"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_SIGN_BINARIES_ROOTS"] = fakeRoot,
                    ["WPFDEVTOOLS_PFX_PASSWORD"] = "CleanupTest123!"
                });

            result.ExitCode.Should().NotBe(0, result.Stdout + result.Stderr);

            var certExists = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Test-Path -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}'");
            certExists.Stdout.Trim().Should().Be("False", "the imported signing certificate must always be removed even when signtool fails");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SignBinariesScript_ShouldNotDeleteCertificatesThatAlreadyExistedInStore()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var certificateName = "WpfDevTools Existing Store Cert " + Guid.NewGuid().ToString("N");
            var certOutputRoot = Path.Combine(tempRoot, "cert-output");
            var createResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Create-SelfSignedCert.ps1"),
                new[]
                {
                    "-CertificateName", certificateName,
                    "-OutputPath", certOutputRoot,
                    "-Password", "ExistingStore123!"
                });

            createResult.ExitCode.Should().Be(0, createResult.Stderr);

            var thumbprintMatch = Regex.Match(
                createResult.Stdout,
                @"Thumbprint:\s*(?<thumbprint>[A-Fa-f0-9]+)",
                RegexOptions.CultureInvariant);
            thumbprintMatch.Success.Should().BeTrue(createResult.Stdout);

            var thumbprint = thumbprintMatch.Groups["thumbprint"].Value;
            var pfxPath = Path.Combine(certOutputRoot, "WpfDevTools.pfx");
            File.Exists(pfxPath).Should().BeTrue();

            var fakeRoot = Path.Combine(tempRoot, "fake-build-root");
            var binaryDir = Path.Combine(fakeRoot, "src", "Sample", "bin", "Release");
            Directory.CreateDirectory(binaryDir);
            File.WriteAllText(Path.Combine(binaryDir, "WpfDevTools.Sample.exe"), "stub");

            var fakeSigntool = Path.Combine(tempRoot, "fake-signtool.cmd");
            File.WriteAllText(
                fakeSigntool,
                "@echo off" + Environment.NewLine +
                "exit /b 1" + Environment.NewLine);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"),
                new[]
                {
                    "-CertificatePath", pfxPath,
                    "-BuildConfiguration", "Release"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_SIGN_BINARIES_ROOTS"] = fakeRoot,
                    ["WPFDEVTOOLS_PFX_PASSWORD"] = "ExistingStore123!"
                });

            result.ExitCode.Should().NotBe(0, result.Stdout + result.Stderr);

            var certExists = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Test-Path -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}'");
            certExists.Stdout.Trim().Should().Be("True", "the signing script must not delete a certificate that already existed in the CurrentUser store before this run");

            var removeOriginalCertificate = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}' -Force -ErrorAction SilentlyContinue");
            removeOriginalCertificate.ExitCode.Should().Be(0, removeOriginalCertificate.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void CreateSelfSignedCertScript_ShouldNotShipHardCodedDevelopmentPasswordDefaults()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Create-SelfSignedCert.ps1"));

        content.Should().NotContain("DevPassword123!");
        content.Should().Contain("[Parameter(Mandatory)]",
            "development certificate generation should require an explicit password or equivalent caller input");
    }

    [Fact]
    public void WriteReleaseSidecars_WhenPackageSignerIsOnlySelfDeclared_ShouldFailClosedInProduction()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            CreateReleaseArchiveWithSignerManifest(tempRoot, TrustedSidecarSignerThumbprint, "CN=Self Declared");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
                ["-ArchiveRoot", tempRoot, "-Tag", "v1.2.3"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0" });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("trusted signer");
            result.Stderr.Should().Contain("self-declared");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseSidecars_WithTrustedSignerPolicyFile_ShouldAnnotateIndependentTrustPolicy()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            CreateReleaseArchiveWithSignerManifest(tempRoot, TrustedSidecarSignerThumbprint, "CN=Trusted");
            var policyPath = Path.Combine(tempRoot, "release-trust-policy.json");
            File.WriteAllText(
                policyPath,
                JsonSerializer.Serialize(new { trustedSignerThumbprints = new[] { TrustedSidecarSignerThumbprint } }));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
                ["-ArchiveRoot", tempRoot, "-Tag", "v1.2.3", "-TrustPolicyPath", policyPath, "-OutputJson"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0" });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempRoot, "release-assets.json")));
            var trustPolicy = manifest.RootElement.GetProperty("assets")[0].GetProperty("signerTrustPolicy");
            trustPolicy.GetProperty("source").GetString().Should().Be("policyFile");
            trustPolicy.GetProperty("trustedSignerThumbprint").GetString().Should().Be(TrustedSidecarSignerThumbprint);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void CreateReleaseArchiveWithSignerManifest(
        string archiveRoot,
        string signerThumbprint,
        string signerSubject)
    {
        var archivePath = Path.Combine(archiveRoot, "release_1.2.3_win-x64.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("bin/manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(new
        {
            signerThumbprint,
            signerSubject,
            signaturePolicy = "RequireAuthenticodeSignature"
        }));
    }
}
