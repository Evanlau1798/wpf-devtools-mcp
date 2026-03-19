using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class OnlineInstallerIntegrationTests
{
    [Fact]
    public void OnlineInstaller_ShouldInstallFromLocalArchiveWithoutNetwork_AndPersistState()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");

            var packageResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild" });
            packageResult.ExitCode.Should().Be(0, packageResult.Stderr);

            var archivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var installResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Version", "latest",
                    "-Architecture", "x64",
                    "-Client", "vscode",
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                });

            installResult.ExitCode.Should().Be(0, installResult.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
            File.Exists(Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json")).Should().BeTrue();

            using var json = JsonDocument.Parse(installResult.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().Be("offline");
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("vscode");
            json.RootElement.GetProperty("statePath").GetString().Should().EndWith("installer-state.json");
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
