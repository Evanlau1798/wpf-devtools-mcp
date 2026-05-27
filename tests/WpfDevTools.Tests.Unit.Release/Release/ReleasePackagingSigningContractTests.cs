using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void PublishReleaseScript_ShouldSupportWorkflowManagedSigningInputs()
    {
        var content = PublishReleaseScriptSource.ReadAll();

        content.Should().Contain("WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH",
            "release packaging should be able to consume a workflow-materialized PFX path instead of requiring manual signing before packaging");
        content.Should().Contain("WPFDEVTOOLS_PFX_PASSWORD",
            "workflow-driven signing needs a non-interactive password path for PFX-backed certificates");
        content.Should().Contain("Resolve-SignToolPath",
            "packaging must be able to find signtool on hosted runners when it signs payloads itself");
        content.Should().Contain("EphemeralKeySet",
            "PFX metadata inspection should avoid persisting private-key material before the real signing import step");
        content.Should().Contain("Assert-ReleasePayloadSignaturePolicy",
            "the packaging entrypoint still needs to enforce signer verification after signing has run");
    }

    [Fact]
    public void PublishReleaseSigning_ShouldResolveWindowsSdkSignToolFallback()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeProgramFilesX86 = Path.Combine(tempRoot, "Program Files (x86)");
            var fakeSignTool = Path.Combine(
                fakeProgramFilesX86,
                "Windows Kits",
                "10",
                "bin",
                "10.0.99999.0",
                "x64",
                "signtool.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeSignTool)!);
            File.WriteAllText(fakeSignTool, "fake signtool");

            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/packaging/Publish-Release.Signing.ps1");
            var command =
                $"$env:WPFDEVTOOLS_SIGNTOOL_PATH = ''; . '{scriptPath}'; Resolve-SignToolPath";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["PATH"] = tempRoot,
                    ["ProgramFiles(x86)"] = fakeProgramFilesX86
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(fakeSignTool);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseSigningEntrypoints_ShouldUseHttpsTimestampDefault()
    {
        var publishRelease = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Publish-Release.ps1"));
        var publishSigning = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Publish-Release.Signing.ps1"));
        var signBinaries = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/Sign-Binaries.ps1"));

        foreach (var content in new[] { publishRelease, publishSigning, signBinaries })
        {
            content.Should().Contain("https://timestamp.digicert.com",
                "production signing should keep timestamp traffic on HTTPS by default");
            content.Should().NotContain("\"http://timestamp.digicert.com\"");
            content.Should().NotContain("'http://timestamp.digicert.com'");
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailFastInCiWhenPfxPasswordIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var (thumbprint, _, pfxPath) = CreateSigningCertificate(tempRoot, removeFromStoreAfterCreate: true);
            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);

            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");

            var fakeSigntool = Path.Combine(toolRoot, "signtool.cmd");
            File.WriteAllText(fakeSigntool, "@echo off\r\nexit /b 0\r\n");
            var fakeToolchain = CreateFakeNativeToolchain(tempRoot, "x64");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["VCToolsInstallDir"] = fakeToolchain.VCToolsDirectory,
                    ["WindowsSDKDir"] = fakeToolchain.WindowsSdkDirectory,
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeToolchain.MSBuildPath,
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH"] = pfxPath,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = thumbprint,
                    ["CI"] = "true"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_PFX_PASSWORD");
            result.Stderr.Should().Contain("Non-interactive release signing");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldNotRemovePreinstalledSignerCertificateWhenSigningFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        string? thumbprint = null;

        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            (thumbprint, _, var pfxPath) = CreateSigningCertificate(tempRoot, removeFromStoreAfterCreate: false);
            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);

            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");

            var fakeSigntool = Path.Combine(toolRoot, "signtool.cmd");
            File.WriteAllText(fakeSigntool, "@echo off\r\nexit /b 1\r\n");
            var fakeToolchain = CreateFakeNativeToolchain(tempRoot, "x64");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["VCToolsInstallDir"] = fakeToolchain.VCToolsDirectory,
                    ["WindowsSDKDir"] = fakeToolchain.WindowsSdkDirectory,
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeToolchain.MSBuildPath,
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH"] = pfxPath,
                    ["WPFDEVTOOLS_PFX_PASSWORD"] = "PackagingTest123!"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("signtool.exe failed");

            var certExists = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Test-Path -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}'");
            certExists.ExitCode.Should().Be(0, certExists.Stderr);
            certExists.Stdout.Trim().Should().Be("True",
                "cleanup should not remove a signer certificate that was already installed before release packaging started");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                ReleaseScriptTestHarness.RunPowerShellCommand(
                    $"Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}' -Force -ErrorAction SilentlyContinue");
            }

            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldPreserveSigningFailureWhenCertificateCleanupAlsoFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        string? thumbprint = null;

        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            (thumbprint, _, var pfxPath) = CreateSigningCertificate(tempRoot, removeFromStoreAfterCreate: true);
            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);

            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");

            var fakeSigntool = Path.Combine(toolRoot, "signtool.cmd");
            File.WriteAllText(fakeSigntool, "@echo off\r\nexit /b 1\r\n");
            var fakeToolchain = CreateFakeNativeToolchain(tempRoot, "x64");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["VCToolsInstallDir"] = fakeToolchain.VCToolsDirectory,
                    ["WindowsSDKDir"] = fakeToolchain.WindowsSdkDirectory,
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeToolchain.MSBuildPath,
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH"] = pfxPath,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = thumbprint,
                    ["WPFDEVTOOLS_PFX_PASSWORD"] = "PackagingTest123!",
                    ["WPFDEVTOOLS_TEST_FORCE_SIGNING_CERTIFICATE_CLEANUP_FAILURE"] = "1"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("signtool.exe failed");
            result.Stderr.Should().Contain("Certificate cleanup also failed");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                ReleaseScriptTestHarness.RunPowerShellCommand(
                    $"Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}' -Force -ErrorAction SilentlyContinue");
            }

            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptHarness_ShouldScrubInheritedReleaseCertificateThumbprint()
    {
        var originalThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT");

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT", "INHERITED_THUMBPRINT");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                "if ([string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT)) { 'EMPTY' } else { $env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT }");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("EMPTY",
                "release test child processes should not inherit host-level certificate thumbprint overrides unless the test passes them explicitly");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT", originalThumbprint);
        }
    }

    [Fact]
    public void ReleaseScriptHarness_ShouldPrepareCertificateProviderAndCleanupSelfSignedFallbackFailures()
    {
        var harnessSource = ReleaseScriptHarnessSource.ReadAll();

        harnessSource.Should().Contain("Remove-TypeData -TypeName System.Security.AccessControl.ObjectSecurity");
        harnessSource.Should().Contain("Import-Module Microsoft.PowerShell.Security -ErrorAction Stop");
        harnessSource.Should().Contain("try { Import-Module PKI -ErrorAction Stop } catch { }");
        harnessSource.Should().Contain("Get-PSProvider Certificate -ErrorAction SilentlyContinue");
        harnessSource.Should().Contain("New-PSDrive -Name Cert -PSProvider Certificate");
        harnessSource.Should().Contain("Get-Command New-SelfSignedCertificate -ErrorAction Stop");
        harnessSource.Should().Contain("TryGetSignedPayloadSignerMetadata");
        harnessSource.Should().Contain("RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10))");
        harnessSource.Should().Contain("SelfSignedPayloadTimeout = TimeSpan.FromMinutes(3)");
        harnessSource.Should().Contain("RunPowerShellCommand(command, timeout: SelfSignedPayloadTimeout)");
        harnessSource.Should().Contain("CleanupGeneratedCertificateFromFile(certificateThumbprintPath);");
        harnessSource.Should().Contain("CleanupGeneratedCertificateIfKnown(generatedThumbprint);");
    }

    [Fact]
    public void ReleaseScriptHarness_ShouldDefaultInstallerTestProcessesToNonElevated()
    {
        var originalAssumeElevated = Environment.GetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED");

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED", "1");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand("$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("0",
                "release tests should not inherit the hosted runner's elevated state unless a test opts in explicitly");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED", originalAssumeElevated);
        }
    }

    private static (string Thumbprint, string Subject, string PfxPath) CreateSigningCertificate(
        string tempRoot,
        bool removeFromStoreAfterCreate)
    {
        var certificateName = "WpfDevTools Packaging Test " + Guid.NewGuid().ToString("N");
        var certificateOutputRoot = Path.Combine(tempRoot, "cert-output", Guid.NewGuid().ToString("N"));
        var createResult = ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Create-SelfSignedCert.ps1"),
            ["-CertificateName", certificateName, "-OutputPath", certificateOutputRoot, "-Password", "PackagingTest123!"],
            new Dictionary<string, string?>
            {
                ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
            });

        createResult.ExitCode.Should().Be(0, createResult.Stderr);

        var thumbprintMatch = Regex.Match(
            createResult.Stdout,
            @"Thumbprint:\s*(?<thumbprint>[A-Fa-f0-9]+)",
            RegexOptions.CultureInvariant);
        thumbprintMatch.Success.Should().BeTrue(createResult.Stdout);

        var subjectMatch = Regex.Match(
            createResult.Stdout,
            @"Subject:\s*(?<subject>.+)",
            RegexOptions.CultureInvariant);
        subjectMatch.Success.Should().BeTrue(createResult.Stdout);

        var thumbprint = thumbprintMatch.Groups["thumbprint"].Value;
        var subject = subjectMatch.Groups["subject"].Value.Trim();
        var pfxPath = Path.Combine(certificateOutputRoot, "WpfDevTools.pfx");
        File.Exists(pfxPath).Should().BeTrue();

        if (removeFromStoreAfterCreate)
        {
            var removeOriginalCertificate = ReleaseScriptTestHarness.RunPowerShellCommand(
                $"Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumbprint}' -Force -ErrorAction SilentlyContinue");
            removeOriginalCertificate.ExitCode.Should().Be(0, removeOriginalCertificate.Stderr);
        }

        return (thumbprint, subject, pfxPath);
    }
}
