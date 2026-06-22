using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.StandaloneInstallerRegressionTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneInstallerInstallRootIsolationTests
{
    [Fact]
    public void UninstallWithExplicitDifferentInstallRoot_ShouldNotRemovePreviousCliRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var previousRoot = Path.Combine(tempRoot, "previous-root");
            var requestedRoot = Path.Combine(tempRoot, "requested-root");
            var fakeCliRoot = Path.Combine(tempRoot, "fake-cli");
            var markerPath = Path.Combine(tempRoot, "claude-registration.txt");
            Directory.CreateDirectory(fakeCliRoot);
            WriteFakeCli(fakeCliRoot, markerPath, "FAKE_CLAUDE_REGISTERED_PATH");

            var environment = CreateStandaloneEnvironment(
                tempRoot,
                new Dictionary<string, string?>
                {
                    ["FAKE_CLAUDE_REGISTERED_PATH"] = Path.Combine(previousRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"),
                    ["PATH"] = fakeCliRoot + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", previousRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.Exists(markerPath).Should().BeTrue("the previous install should register the fake Claude CLI");

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", requestedRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("installRoot").GetString().Should().Be(requestedRoot);
            File.Exists(markerPath).Should().BeTrue("explicit InstallRoot cleanup must not mutate a previous live root");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void WriteFakeCli(string fakeCliRoot, string markerPath, string registeredPathVariable)
    {
        var lines = new[]
        {
            "@echo off",
            "if \"%1\"==\"mcp\" if \"%2\"==\"add\" (",
            "  >\"" + markerPath + "\" echo registered",
            "  exit /b 0",
            ")",
            "if \"%1\"==\"mcp\" if \"%2\"==\"remove\" (",
            "  if exist \"" + markerPath + "\" del \"" + markerPath + "\"",
            "  exit /b 0",
            ")",
            "if \"%1\"==\"mcp\" if \"%2\"==\"list\" (",
            "  if exist \"" + markerPath + "\" (",
            "    echo wpf-devtools %" + registeredPathVariable + "%",
            "    exit /b 0",
            "  )",
            "  exit /b 0",
            ")",
            "exit /b 1"
        };
        File.WriteAllText(Path.Combine(fakeCliRoot, "claude.cmd"), string.Join("\r\n", lines));
    }
}
