using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class GrokInstallerScriptTests
{
    [Fact]
    public void OnlineInstaller_ShouldApplyGrokRegistrationViaCliAndReportAppliedStatus()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var grokLog = Path.Combine(tempRoot, "grok.log");
            var grokCommandPath = ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "grok", grokLog);
            var environment = CreateInstallerEnvironment(tempRoot, fakeBin);
            environment["WPFDEVTOOLS_GROK_COMMAND_PATH"] = grokCommandPath;

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "grok",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(grokLog)
                .Should().Contain("mcp add --scope user wpf-devtools --")
                .And.Contain("wpf-devtools-x64.exe")
                .And.Contain("mcp list");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["grok"]);
            var registration = json.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("mode").GetString().Should().Be("cli");
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("verificationMessage").GetString()
                .Should().Be("Verified with grok mcp list.");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_NonJsonGrokManualCliFallback_ShouldShowManualRegistrationGuidance()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var commandPath = Path.Combine(fakeBin, "grok.cmd");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(
                commandPath,
                "@echo off" + Environment.NewLine +
                "if /I \"%1 %2\"==\"mcp add\" echo Access is denied. 1>&2" + Environment.NewLine +
                "if /I \"%1 %2\"==\"mcp add\" exit /b 5" + Environment.NewLine +
                "exit /b 0" + Environment.NewLine);

            var environment = CreateInstallerEnvironment(tempRoot, fakeBin);
            environment["WPFDEVTOOLS_GROK_COMMAND_PATH"] = commandPath;

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "grok",
                    "-NonInteractive",
                    "-Force"
                ],
                environment);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Manual registration required");
            result.Stdout.Should().Contain(Path.Combine("client-registration", "grok.txt"));
            result.Stdout.Should().Contain("grok mcp add --scope user wpf-devtools --");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot, string? fakeBin)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };

        if (!string.IsNullOrWhiteSpace(fakeBin))
        {
            environment["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        return environment;
    }
}
