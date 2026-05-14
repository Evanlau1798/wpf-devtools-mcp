using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void BuildReleaseScript_ShouldExistAsPublicPackagingEntryPoint()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "maintainers should have a stable packaging entrypoint under scripts/tools");
    }

    [Fact]
    public void BuildReleaseScript_ShouldDelegateToPublishReleaseScript()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1");
        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("packaging\\Publish-Release.ps1");
        content.Should().Contain("release");
    }

    [Fact]
    public void BuildReleaseScript_ShouldAllowPublishScriptOverrideForDeterministicScriptTests()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var publishLog = Path.Combine(tempRoot, "publish-log.json");
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            var outputRoot = Path.Combine(tempRoot, "custom-release");
            File.WriteAllText(
                fakePublishScript,
                string.Join(
                    Environment.NewLine,
                    [
                        "param(",
                        "    [string]$Configuration,",
                        "    [string[]]$Architectures,",
                        "    [string]$OutputRoot,",
                        "    [switch]$SkipBuild",
                        ")",
                        "$payload = @{",
                        "    Configuration = $Configuration",
                        "    Architectures = $Architectures",
                        "    OutputRoot = $OutputRoot",
                        "    SkipBuild = $SkipBuild.IsPresent",
                        "} | ConvertTo-Json -Depth 3",
                        $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Value $payload -Encoding UTF8"
                    ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                ["-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(publishLog).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(publishLog));
            document.RootElement.GetProperty("Configuration").GetString().Should().Be("Debug");
            document.RootElement.GetProperty("Architectures").EnumerateArray().Select(x => x.GetString()).Should().Equal("x64");
            document.RootElement.GetProperty("OutputRoot").GetString().Should().Be(outputRoot);
            document.RootElement.GetProperty("SkipBuild").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_WhenOverridePublishScriptFails_ShouldSurfacePublishExitCode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var fakePublishScript = Path.Combine(tempRoot, "failing-publish.ps1");
            File.WriteAllText(fakePublishScript, "exit 23");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                Array.Empty<string>(),
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Release build failed with exit code 23");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_ShouldNormalizeCommaSeparatedArchitectureInput()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var publishLog = Path.Combine(tempRoot, "publish-architectures.json");
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            File.WriteAllText(
                fakePublishScript,
                string.Join(
                    Environment.NewLine,
                    [
                        "param(",
                        "    [string]$Configuration,",
                        "    [string[]]$Architectures,",
                        "    [string]$OutputRoot,",
                        "    [switch]$SkipBuild",
                        ")",
                        ("@{ Architectures = $Architectures } | ConvertTo-Json -Depth 3 | " +
                         $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Encoding UTF8")
                    ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                ["-Architectures", "x64,x86,arm64", "-SkipBuild"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(File.ReadAllText(publishLog));
            document.RootElement.GetProperty("Architectures").EnumerateArray().Select(x => x.GetString())
                .Should().Equal("x64", "x86", "arm64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallBatchTemplate_ShouldBootstrapPackagedInstallerWithElevationAndExecutionPolicyBypass()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        File.Exists(batchTemplatePath).Should().BeTrue();
        content.Should().Contain("bin\\install.ps1");
        content.Should().Contain("powershell.exe");
        content.Should().Contain("-ExecutionPolicy Bypass");
        content.Should().Contain("RunAs");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCopyCanonicalInstallerAndAvoidLegacyScriptChain()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("run-template.bat");
        content.Should().Contain("run.bat");
        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().Contain("scripts\\installer");
        content.Should().Contain("bin\\install.ps1");
        content.Should().Contain("Join-Path $binDir 'installer'");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
        content.Should().NotContain("Uninstall-WpfDevTools.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldUseVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("release_${version}_win-$architecture.zip");
        content.Should().NotContain("_dev_win-");
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailClosedWhenExpectedReleaseTagDoesNotMatchProjectVersion()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var fakeMsbuild = Path.Combine(tempRoot, "fake-msbuild.exe");
            File.WriteAllText(fakeMsbuild, "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild", "-ExpectedReleaseTag", "v9.9.9"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Expected release tag 'v9.9.9' does not match project version '1.2.3'",
                "release packaging should fail before publishing mismatched asset names into a different Git tag");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldRetryArchiveCreationWhenTransientFileLocksOccur()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Invoke-ArchiveCreation");
        content.Should().Contain("Compress-Archive");
        content.Should().Contain("Start-Sleep");
    }

    [Fact]
    public void PublishReleaseScript_ShouldSupportWorkflowManagedSigningInputs()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

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

    [Fact]
    public void PublishReleaseScript_WhenSkipBuildUsesFrameworkOnlyServerOutput_ShouldFailInsteadOfPackagingWrongArchitecture()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var repoRoot = Path.Combine(tempRoot, "repo");
            var packagingRoot = Path.Combine(repoRoot, "scripts", "tools", "packaging");
            var installerRoot = Path.Combine(repoRoot, "scripts", "installer");
            var serverProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Mcp.Server");
            var inspectorProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Inspector");
            var bootstrapperProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Bootstrapper");
            Directory.CreateDirectory(packagingRoot);
            Directory.CreateDirectory(installerRoot);
            Directory.CreateDirectory(serverProjectRoot);
            Directory.CreateDirectory(inspectorProjectRoot);
            Directory.CreateDirectory(bootstrapperProjectRoot);

            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                Path.Combine(packagingRoot, "Publish-Release.ps1"),
                overwrite: true);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat"),
                Path.Combine(packagingRoot, "run-template.bat"),
                overwrite: true);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                Path.Combine(repoRoot, "scripts", "online-installer.ps1"),
                overwrite: true);

            var manifestSource = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json");
            File.Copy(manifestSource, Path.Combine(installerRoot, "installer-helpers.manifest.json"), overwrite: true);
            using var helperManifest = JsonDocument.Parse(File.ReadAllText(manifestSource));
            foreach (var helperFile in helperManifest.RootElement.GetProperty("helperFiles")
                         .EnumerateArray()
                         .Select(static entry => entry.ValueKind == JsonValueKind.Object
                             ? entry.GetProperty("path").GetString()
                             : entry.GetString())
                         .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                         .Cast<string>())
            {
                File.Copy(
                    ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", helperFile)),
                    Path.Combine(installerRoot, helperFile),
                    overwrite: true);
            }

            File.WriteAllText(
                Path.Combine(serverProjectRoot, "WpfDevTools.Mcp.Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Version>1.2.3</Version>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(inspectorProjectRoot, "WpfDevTools.Inspector.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(bootstrapperProjectRoot, "WpfDevTools.Bootstrapper.vcxproj"), "<Project />");

            var frameworkOutputDir = Path.Combine(serverProjectRoot, "bin", "Release", "net8.0");
            Directory.CreateDirectory(frameworkOutputDir);
            File.WriteAllText(Path.Combine(frameworkOutputDir, "WpfDevTools.Mcp.Server.exe"), "framework-apphost");

            var inspectorNet8Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net8.0-windows");
            var inspectorNet48Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net48");
            Directory.CreateDirectory(inspectorNet8Dir);
            Directory.CreateDirectory(inspectorNet48Dir);
            File.WriteAllText(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), "net8");
            File.WriteAllText(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), "net48");

            var bootstrapperOutputDir = Path.Combine(repoRoot, "artifacts", "bootstrapper", "Release", "Win32");
            Directory.CreateDirectory(bootstrapperOutputDir);
            File.WriteAllText(Path.Combine(bootstrapperOutputDir, "WpfDevTools.Bootstrapper.x86.dll"), "bootstrapper");

            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);
            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");
            var fakeMsbuild = Path.Combine(toolRoot, "msbuild.cmd");
            File.WriteAllText(fakeMsbuild, "@echo off\r\nexit /b 0\r\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(packagingRoot, "Publish-Release.ps1"),
                ["-Configuration", "Release", "-Architectures", "x86", "-OutputRoot", Path.Combine(tempRoot, "release"), "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("win-x86");
            result.Stderr.Should().Contain("existing server output");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptTestHarness_CreatePackageArchive_ShouldIncludeInspectorAndBootstrapperPayloads()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "arm64");
            var extractRoot = Path.Combine(tempRoot, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractRoot);

            File.Exists(Path.Combine(extractRoot, "bin", "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "inspectors", "net48", "WpfDevTools.Inspector.dll")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "bootstrapper", "arm64", "WpfDevTools.Bootstrapper.arm64.dll")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldInstallServerExecutableFromBinDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var packageLocalScript = Path.Combine(packageDir, "bin", "install.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                packageLocalScript,
                overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                packageLocalScript,
                ["-InstallRoot", installRoot, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailFastWhenArm64ToolchainIsUnavailable()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeVsRoot = Path.Combine(tempRoot, "vs", "MSBuild", "Current", "Bin");
            Directory.CreateDirectory(fakeVsRoot);
            var fakeMsbuildPath = Path.Combine(fakeVsRoot, "MSBuild.exe");
            File.WriteAllText(fakeMsbuildPath, "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                ["-Configuration", "Debug", "-Architectures", "arm64", "-SkipBuild", "-OutputRoot", Path.Combine(tempRoot, "release-output")],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuildPath });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("ARM64 bootstrapper build requires");
            result.Stderr.Should().Contain("Microsoft.VisualStudio.Component.VC.Tools.ARM64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (string RepoRoot, string PackagingScriptPath, string OutputRoot) CreateMinimalSkipBuildReleaseRepo(
        string tempRoot,
        string architecture)
    {
        var repoRoot = Path.Combine(tempRoot, "repo");
        var packagingRoot = Path.Combine(repoRoot, "scripts", "tools", "packaging");
        var installerRoot = Path.Combine(repoRoot, "scripts", "installer");
        var serverProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Mcp.Server");
        var inspectorProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Inspector");
        var bootstrapperProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Bootstrapper");
        Directory.CreateDirectory(packagingRoot);
        Directory.CreateDirectory(installerRoot);
        Directory.CreateDirectory(serverProjectRoot);
        Directory.CreateDirectory(inspectorProjectRoot);
        Directory.CreateDirectory(bootstrapperProjectRoot);

        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
            Path.Combine(packagingRoot, "Publish-Release.ps1"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat"),
            Path.Combine(packagingRoot, "run-template.bat"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
            Path.Combine(packagingRoot, "Write-ReleaseSidecars.ps1"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            Path.Combine(repoRoot, "scripts", "online-installer.ps1"),
            overwrite: true);

        var manifestSource = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json");
        File.Copy(manifestSource, Path.Combine(installerRoot, "installer-helpers.manifest.json"), overwrite: true);
        using var helperManifest = JsonDocument.Parse(File.ReadAllText(manifestSource));
        foreach (var helperFile in helperManifest.RootElement.GetProperty("helperFiles")
                     .EnumerateArray()
                     .Select(static entry => entry.ValueKind == JsonValueKind.Object
                         ? entry.GetProperty("path").GetString()
                         : entry.GetString())
                     .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                     .Cast<string>())
        {
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", helperFile)),
                Path.Combine(installerRoot, helperFile),
                overwrite: true);
        }

        File.WriteAllText(
            Path.Combine(serverProjectRoot, "WpfDevTools.Mcp.Server.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.2.3</Version>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(inspectorProjectRoot, "WpfDevTools.Inspector.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(bootstrapperProjectRoot, "WpfDevTools.Bootstrapper.vcxproj"), "<Project />");

        var runtimeId = architecture switch
        {
            "x64" => "win-x64",
            "x86" => "win-x86",
            "arm64" => "win-arm64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture))
        };

        var bootstrapperPlatform = architecture switch
        {
            "x64" => "x64",
            "x86" => "Win32",
            "arm64" => "ARM64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture))
        };

        var serverOutputDir = Path.Combine(serverProjectRoot, "bin", "Release", "net8.0", runtimeId);
        Directory.CreateDirectory(serverOutputDir);
        File.WriteAllText(Path.Combine(serverOutputDir, "WpfDevTools.Mcp.Server.exe"), "server");

        var inspectorNet8Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net8.0-windows");
        var inspectorNet48Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net48");
        Directory.CreateDirectory(inspectorNet8Dir);
        Directory.CreateDirectory(inspectorNet48Dir);
        File.WriteAllText(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), "net8");
        File.WriteAllText(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), "net48");

        var bootstrapperOutputDir = Path.Combine(repoRoot, "artifacts", "bootstrapper", "Release", bootstrapperPlatform);
        Directory.CreateDirectory(bootstrapperOutputDir);
        File.WriteAllText(Path.Combine(bootstrapperOutputDir, $"WpfDevTools.Bootstrapper.{architecture}.dll"), "bootstrapper");

        return (
            repoRoot,
            Path.Combine(packagingRoot, "Publish-Release.ps1"),
            Path.Combine(tempRoot, "release-output"));
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
