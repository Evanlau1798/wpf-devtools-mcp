using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerStatePersistenceTests
{
    [Fact]
    public void OnlineInstaller_ShouldPersistSharedInstallerStateUnderRoamingAppData()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "shared-server-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            Directory.CreateDirectory(appData);

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
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            File.Exists(statePath).Should().BeTrue("the installer should persist the chosen install root for reuse");

            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            state.RootElement.GetProperty("lastInstallRoot").GetString().Should().Be(installRoot);
            state.RootElement.GetProperty("architectures").GetProperty("x64").GetProperty("installRoot").GetString()
                .Should().Be(installRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
