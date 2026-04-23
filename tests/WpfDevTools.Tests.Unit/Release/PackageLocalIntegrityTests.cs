using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class PackageLocalIntegrityTests
{
    [Fact]
    public void PackageLocalInstaller_ShouldRejectUnsignedPayloadWhenSignaturePolicyRequiresAuthenticode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: false);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var manifestPath = Path.Combine(extractRoot, "bin", "manifest.json");
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var rewrittenManifest = JsonSerializer.Serialize(new
                {
                    name = manifest.RootElement.GetProperty("name").GetString(),
                    version = manifest.RootElement.GetProperty("version").GetString(),
                    architecture = manifest.RootElement.GetProperty("architecture").GetString(),
                    runtimeId = manifest.RootElement.GetProperty("runtimeId").GetString(),
                    channel = "release",
                    buildConfiguration = "Release",
                    signaturePolicy = "RequireAuthenticodeSignature",
                    inspector = new
                    {
                        net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                        net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                    },
                    bootstrapper = "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                });
                File.WriteAllText(manifestPath, rewrittenManifest);
            }

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("signature", "production package-local installs should reject unsigned payloads");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldAllowDeterministicTestSignatureOverridesInInstallerTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: false);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var manifestPath = Path.Combine(extractRoot, "bin", "manifest.json");
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var rewrittenManifest = JsonSerializer.Serialize(new
                {
                    name = manifest.RootElement.GetProperty("name").GetString(),
                    version = manifest.RootElement.GetProperty("version").GetString(),
                    architecture = manifest.RootElement.GetProperty("architecture").GetString(),
                    runtimeId = manifest.RootElement.GetProperty("runtimeId").GetString(),
                    channel = "release",
                    buildConfiguration = "Release",
                    signaturePolicy = "RequireAuthenticodeSignature",
                    inspector = new
                    {
                        net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                        net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                    },
                    bootstrapper = "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                });
                File.WriteAllText(manifestPath, rewrittenManifest);
            }

            var environment = new Dictionary<string, string?>(CreateInstallerEnvironment(tempRoot))
            {
                ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
            };

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldRejectDeterministicTestSignatureOverridesOutsideInstallerTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: false);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var manifestPath = Path.Combine(extractRoot, "bin", "manifest.json");
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var rewrittenManifest = JsonSerializer.Serialize(new
                {
                    name = manifest.RootElement.GetProperty("name").GetString(),
                    version = manifest.RootElement.GetProperty("version").GetString(),
                    architecture = manifest.RootElement.GetProperty("architecture").GetString(),
                    runtimeId = manifest.RootElement.GetProperty("runtimeId").GetString(),
                    channel = "release",
                    buildConfiguration = "Release",
                    signaturePolicy = "RequireAuthenticodeSignature",
                    inspector = new
                    {
                        net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                        net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                    },
                    bootstrapper = "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                });
                File.WriteAllText(manifestPath, rewrittenManifest);
            }

            var environment = new Dictionary<string, string?>(CreateInstallerEnvironment(tempRoot, enforceProductionMode: true))
            {
                ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
            };

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                environment);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("WPFDEVTOOLS_TEST_SIGNATURE_STATUS is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldIgnoreUnsignedThirdPartyDependenciesWhenSignedPayloadsAreValid()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: true);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);
            File.WriteAllText(Path.Combine(extractRoot, "bin", "ThirdParty.Dependency.dll"), "unsigned dependency");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                CreateInstallerEnvironment(tempRoot, includeTrustedSignerOverride: true, enforceProductionMode: true));

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldRequireTrustedSignerOverrideWhenPackageIsNotArchiveBacked()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: true);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var manifestPath = Path.Combine(extractRoot, "bin", "manifest.json");
            var manifestNode = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifestNode["signerThumbprint"] = "0000000000000000000000000000000000000000";
            File.WriteAllText(manifestPath, manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                CreateInstallerEnvironment(tempRoot, enforceProductionMode: true));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("requires pinned signer metadata", "package-local installs must not trust signer metadata embedded inside the package");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldNotTrustTamperedManifestToDisableSignatureVerification()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: false);
            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var manifestPath = Path.Combine(extractRoot, "bin", "manifest.json");
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var rewrittenManifest = JsonSerializer.Serialize(new
                {
                    name = manifest.RootElement.GetProperty("name").GetString(),
                    version = manifest.RootElement.GetProperty("version").GetString(),
                    architecture = manifest.RootElement.GetProperty("architecture").GetString(),
                    runtimeId = manifest.RootElement.GetProperty("runtimeId").GetString(),
                    channel = "release",
                    buildConfiguration = "Release",
                    signaturePolicy = "DebugTrustedRootSkip",
                    inspector = new
                    {
                        net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                        net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                    },
                    bootstrapper = "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
                });
                File.WriteAllText(manifestPath, rewrittenManifest);
            }

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("signature", "release package-local installs must not trust a downgraded embedded manifest");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static IReadOnlyDictionary<string, string?> CreateInstallerEnvironment(
        string tempRoot,
        bool includeTrustedSignerOverride = false,
        bool enforceProductionMode = false)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };

        if (enforceProductionMode)
        {
            environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0";
        }

        if (includeTrustedSignerOverride)
        {
            var signer = ReleaseScriptTestHarness.GetSignedPayloadSigner();
            environment["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = signer.Thumbprint;
            environment["WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT"] = signer.Subject;
        }

        return environment;
    }
}
