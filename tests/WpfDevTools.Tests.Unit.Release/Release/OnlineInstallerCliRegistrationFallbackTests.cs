using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerCliRegistrationFallbackTests
{
    [Fact]
    public void Install_WithCodexAccessDenied_ShouldKeepPayloadAndExportManualArtifact()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
                tempRoot,
                architecture: "x64",
                isolateArchiveContents: true);
            var fakeCommandPath = CreateAccessDeniedFakeCommand(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install");
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-PackageArchivePath",
                    archivePath,
                    "-TrustedReleaseMetadataDirectory",
                    tempRoot,
                    "-Architecture",
                    "x64",
                    "-Client",
                    "codex",
                    "-InstallRoot",
                    installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateInstallerEnvironment(tempRoot, fakeCommandPath),
                timeout: TimeSpan.FromMinutes(2));

            result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var root = document.RootElement;
            var installedExecutable = root.GetProperty("installedExecutable").GetString();
            var registration = root.GetProperty("registrations")[0];
            var artifactPath = registration.GetProperty("target").GetString();

            File.Exists(installedExecutable).Should().BeTrue();
            registration.GetProperty("mode").GetString().Should().Be("manual-cli-artifact");
            registration.GetProperty("applied").GetBoolean().Should().BeFalse();
            artifactPath.Should().EndWith(Path.Combine("client-registration", "codex.txt"));
            File.ReadAllText(artifactPath!).Should().Contain(installedExecutable);
            root.GetProperty("verificationMessage").GetString()
                .Should().Contain("Manual Codex registration artifact");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Uninstall_AfterManualCliArtifactFallback_ShouldRemoveManualArtifact()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
                tempRoot,
                architecture: "x64",
                isolateArchiveContents: true);
            var fakeCommandPath = CreateAccessDeniedFakeCommand(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install");
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var environment = CreateInstallerEnvironment(tempRoot, fakeCommandPath);

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-PackageArchivePath", archivePath,
                    "-TrustedReleaseMetadataDirectory", tempRoot,
                    "-Architecture", "x64",
                    "-Client", "codex",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            install.ExitCode.Should().Be(0, install.Stdout + install.Stderr);
            using var installDocument = JsonDocument.Parse(install.Stdout);
            var artifactPath = installDocument.RootElement
                .GetProperty("registrations")[0]
                .GetProperty("target")
                .GetString();
            File.Exists(artifactPath).Should().BeTrue();

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-Client", "codex",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            uninstall.ExitCode.Should().Be(0, uninstall.Stdout + uninstall.Stderr);
            File.Exists(artifactPath).Should().BeFalse();
            using var uninstallDocument = JsonDocument.Parse(uninstall.Stdout);
            uninstallDocument.RootElement.GetProperty("verificationMessage").GetString()
                .Should().Contain("manual CLI registration artifact removal");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateAccessDeniedFakeCommand(string tempRoot)
    {
        var directory = Path.Combine(tempRoot, "fake-bin");
        Directory.CreateDirectory(directory);
        var commandPath = Path.Combine(directory, "codex.cmd");
        File.WriteAllText(
            commandPath,
            "@echo off" + Environment.NewLine +
            "echo %*>>\"" + Path.Combine(tempRoot, "codex.log") + "\"" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp add\" echo Access is denied. 1>&2" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp add\" exit /b 5" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp list\" echo Access is denied. 1>&2" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp list\" exit /b 5" + Environment.NewLine +
            "exit /b 0" + Environment.NewLine);

        return commandPath;
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot, string fakeCommandPath)
    {
        return new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
            ["TEMP"] = Path.Combine(tempRoot, "Temp"),
            ["TMP"] = Path.Combine(tempRoot, "Temp"),
            ["WPFDEVTOOLS_CODEX_COMMAND_PATH"] = fakeCommandPath,
            ["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "1",
            ["WPFDEVTOOLS_SKIP_ELEVATION"] = "1"
        };
    }
}
