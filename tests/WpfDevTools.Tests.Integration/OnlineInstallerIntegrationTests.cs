using System.IO;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

public sealed class OnlineInstallerIntegrationTests
{
    [Fact]
    public void OnlineInstaller_ShouldInstallFromLocalArchiveWithoutNetwork()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var installRoot = Path.Combine(tempRoot, "install-root");

            var packageResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/build-release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild" });
            packageResult.ExitCode.Should().Be(0, packageResult.Stderr);

            var archivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var installResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Version", "latest",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Force",
                    "-OutputJson"
                });

            installResult.ExitCode.Should().Be(0, installResult.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "WpfDevTools.Mcp.Server.exe")).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json")).Should().BeTrue();
            installResult.Stdout.Should().Contain("\"packageAssetName\"");
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
