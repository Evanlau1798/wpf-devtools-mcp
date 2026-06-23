using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerPrereleasePlanModeTests
{
    [Fact]
    public void OnlineInstaller_PlanMode_WithPrereleaseLatest_ShouldReportPrereleaseChannelWithoutDownloading()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Version", "latest",
                    "-Prerelease",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-WorkingRoot", workingRoot,
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;
            root.GetProperty("action").GetString().Should().Be("plan");
            root.GetProperty("version").GetString().Should().Be("latest");
            root.GetProperty("releaseChannel").GetString().Should().Be("prerelease");
            root.GetProperty("downloadsReleaseAssets").GetBoolean().Should().BeFalse();
            Directory.Exists(installRoot).Should().BeFalse();
            Directory.Exists(workingRoot).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_PlanMode_WithExplicitPrereleaseTag_ShouldReportPrereleaseChannel()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Version", "v0.1.0-e2e.20260623140607",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-WorkingRoot", workingRoot,
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;
            root.GetProperty("action").GetString().Should().Be("plan");
            root.GetProperty("version").GetString().Should().Be("v0.1.0-e2e.20260623140607");
            root.GetProperty("releaseChannel").GetString().Should().Be("prerelease");
            root.GetProperty("downloadsReleaseAssets").GetBoolean().Should().BeFalse();
            Directory.Exists(installRoot).Should().BeFalse();
            Directory.Exists(workingRoot).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
