using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerFullUninstallLockFailureTests
{
    [Fact]
    public void FullUninstall_WhenPayloadFileIsLocked_ShouldExplainHowToRecover()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        FileStream? lockHandle = null;
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
                tempRoot,
                architecture: "x64",
                isolateArchiveContents: true);
            var installRoot = Path.Combine(tempRoot, "install");
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var environment = CreateInstallerEnvironment(tempRoot);

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-PackageArchivePath", archivePath,
                    "-TrustedReleaseMetadataDirectory", tempRoot,
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            install.ExitCode.Should().Be(0, install.Stdout + install.Stderr);
            using var installDocument = JsonDocument.Parse(install.Stdout);
            var installedExecutable = installDocument.RootElement
                .GetProperty("installedExecutable")
                .GetString();
            installedExecutable.Should().NotBeNullOrWhiteSpace();

            var bootstrapperPath = Path.Combine(
                Path.GetDirectoryName(installedExecutable!)!,
                "bootstrapper",
                "x64",
                "WpfDevTools.Bootstrapper.x64.dll");
            File.Exists(bootstrapperPath).Should().BeTrue();
            lockHandle = new FileStream(bootstrapperPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            uninstall.ExitCode.Should().NotBe(0);
            (uninstall.Stdout + uninstall.Stderr).Should().Contain("Close any running WPF target applications");
            (uninstall.Stdout + uninstall.Stderr).Should().Contain("-Action full-uninstall");
        }
        finally
        {
            lockHandle?.Dispose();
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
            ["TEMP"] = Path.Combine(tempRoot, "Temp"),
            ["TMP"] = Path.Combine(tempRoot, "Temp"),
            ["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "1",
            ["WPFDEVTOOLS_SKIP_ELEVATION"] = "1"
        };
}
