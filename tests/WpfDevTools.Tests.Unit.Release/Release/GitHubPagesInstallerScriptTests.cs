using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class GitHubPagesInstallerScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldInstallFromLocalArchiveWithoutLegacySetupChain()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().Be("offline");
            json.RootElement.GetProperty("downloadSource").GetString().Should().Be("local-package");
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().EndWith("x64\\current\\bin\\wpf-devtools-x64.exe");
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseRequestedArchitectureForPackageMetadata()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "arm64");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Architecture", "arm64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PROCESSOR_ARCHITECTURE"] = "ARM64",
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().Contain("arm64\\current\\bin\\wpf-devtools-arm64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUsePackageManifestArchitectureForOfflineArchiveMetadataWhenArchitectureWasNotSpecified()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PROCESSOR_ARCHITECTURE"] = "x86",
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("architecture").GetString().Should().Be("x64");
            json.RootElement.GetProperty("downloadUri").GetString()
                .Should().EndWith("/release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSurfaceReleaseArchiveMetadataAndResolvedVersion()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, "x64");
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64",
                    channel = "dev",
                    buildConfiguration = "Debug",
                    signaturePolicy = "DebugTrustedRootSkip"
                }));
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), Path.Combine(packageDir, "bin", "install.ps1"), overwrite: true);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat"), Path.Combine(packageDir, "run.bat"), overwrite: true);

            var archivePath = Path.Combine(tempRoot, "release_1.2.3_win-x64.zip");
            ZipFile.CreateFromDirectory(packageDir, archivePath);
            ReleaseScriptTestHarness.WriteAdjacentReleaseMetadata(archivePath);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Version", "1.2.3",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("resolvedVersion").GetString().Should().Be("1.2.3");
            json.RootElement.GetProperty("architecture").GetString().Should().Be("x64");
            json.RootElement.GetProperty("packageAssetName").GetString().Should().Be("release_1.2.3_win-x64.zip");
            json.RootElement.GetProperty("downloadUri").GetString().Should().Contain("github.com/Evanlau1798/wpf-devtools-mcp/releases/download/v1.2.3/release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectTamperedLocalArchive_WhenSignerOverrideCannotProveArchiveIntegrity()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: true, isolateArchiveContents: true);
            var extractRoot = Path.Combine(tempRoot, "tampered-package");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var installScriptPath = Path.Combine(extractRoot, "bin", "install.ps1");
            File.AppendAllText(installScriptPath, Environment.NewLine + "# tampered local archive script");

            var tamperedArchivePath = Path.Combine(tempRoot, "release_1.2.3_win-x64.zip");
            if (File.Exists(tamperedArchivePath))
            {
                File.Delete(tamperedArchivePath);
            }

            ZipFile.CreateFromDirectory(extractRoot, tamperedArchivePath);
            var signer = ReleaseScriptTestHarness.GetSignedPayloadSigner();
            File.Delete(Path.Combine(tempRoot, "release-assets.json"));
            File.Delete(Path.Combine(tempRoot, "SHA256SUMS.txt"));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", tamperedArchivePath,
                    "-InstallRoot", Path.Combine(tempRoot, "install-root"),
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty,
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = signer.Thumbprint,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT"] = signer.Subject
                });

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("Archive integrity", "a signer override can validate signed binaries but cannot prove that an entire local archive with unsigned installer scripts remained untampered");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectRenamedTamperedLocalArchive_WhenCanonicalIdentityCannotBeResolved()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: true, isolateArchiveContents: true);
            var extractRoot = Path.Combine(tempRoot, "renamed-tampered-package");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var installScriptPath = Path.Combine(extractRoot, "bin", "install.ps1");
            File.AppendAllText(installScriptPath, Environment.NewLine + "# tampered renamed local archive script");

            var renamedArchivePath = Path.Combine(tempRoot, "renamed-local-package.zip");
            if (File.Exists(renamedArchivePath))
            {
                File.Delete(renamedArchivePath);
            }

            ZipFile.CreateFromDirectory(extractRoot, renamedArchivePath);
            var signer = ReleaseScriptTestHarness.GetSignedPayloadSigner();
            File.Delete(Path.Combine(tempRoot, "release-assets.json"));
            File.Delete(Path.Combine(tempRoot, "SHA256SUMS.txt"));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", renamedArchivePath,
                    "-InstallRoot", Path.Combine(tempRoot, "install-root"),
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = signer.Thumbprint,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT"] = signer.Subject
                });

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("canonical release asset identity",
                    "renaming a local release archive must not bypass the trusted archive provenance gate for unsigned installer script content");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSupportIrmStyleScriptblockExecution()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            var wrapperPath = Path.Combine(tempRoot, "invoke-scriptblock.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                scriptPath,
                overwrite: true);
            File.WriteAllText(
                wrapperPath,
                string.Join(Environment.NewLine,
                [
                    "$scriptText = Get-Content -Path '" + scriptPath.Replace("'", "''") + "' -Raw",
                    "$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true",
                    "$script:WpfDevToolsInstallerTestModeEnabled = $true",
                    "$installer = [scriptblock]::Create($scriptText)",
                    "& $installer -PackageArchivePath '" + archivePath.Replace("'", "''") + "' -InstallRoot '" + installRoot.Replace("'", "''") + "' -Client other -NonInteractive -Force -OutputJson"
                ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                wrapperPath,
                Array.Empty<string>(),
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().NotBeNullOrWhiteSpace();
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().EndWith("x64\\current\\bin\\wpf-devtools-x64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageArchive_ShouldContainAllInstallerHelpersNeededByTheCliFirstInstaller()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var extractRoot = Path.Combine(tempRoot, "extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.ScreenModel.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Renderer.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Terminal.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Layout.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Input.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Flow.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Confirm.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Discovery.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Uninstall.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Release.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.PackageIntegrity.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.State.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Registration.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Actions.ps1")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
