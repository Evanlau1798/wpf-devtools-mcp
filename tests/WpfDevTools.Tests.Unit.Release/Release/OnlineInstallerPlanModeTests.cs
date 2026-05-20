using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerPlanModeTests
{
    [Fact]
    public void OnlineInstaller_PlanMode_ShouldEmitReadOnlyMachineReadablePlan()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "plan",
                    "-Architecture", "x86",
                    "-InstallRoot", installRoot,
                    "-WorkingRoot", workingRoot,
                    "-OutputJson",
                    "-NonInteractive"
                ],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var root = json.RootElement;

            root.GetProperty("action").GetString().Should().Be("plan");
            root.GetProperty("platform").GetString().Should().Be("windows");
            root.GetProperty("architecture").GetString().Should().Be("x86");
            root.GetProperty("installRootDefault").GetString().Should().Be(installRoot);
            root.GetProperty("requiresUserConfirmationBeforeMutation").GetBoolean().Should().BeTrue();
            root.GetProperty("mutatesFileSystem").GetBoolean().Should().BeFalse();
            root.GetProperty("downloadsReleaseAssets").GetBoolean().Should().BeFalse();
            root.GetProperty("runsClientRegistration").GetBoolean().Should().BeFalse();

            root.GetProperty("supportedClients").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Equal("claude-code", "codex", "cursor", "vscode", "visual-studio", "claude-desktop", "other");

            root.GetProperty("detectedClients").EnumerateArray()
                .Should().Contain(element =>
                    element.GetProperty("client").GetString() == "other" &&
                    element.GetProperty("available").GetBoolean());

            Directory.Exists(installRoot).Should().BeFalse("plan mode must not create the install root");
            Directory.Exists(workingRoot).Should().BeFalse("plan mode must not create the working root");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
