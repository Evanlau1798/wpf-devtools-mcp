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
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldRejectPayloadWhenManifestSignerThumbprintDoesNotMatch()
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
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("Expected signer thumbprint", "package-local installs must pin payload signatures to the shipped signer");
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

    private static IReadOnlyDictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };
}
