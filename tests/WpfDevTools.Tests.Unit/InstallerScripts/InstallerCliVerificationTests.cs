using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerScriptTests
{
    [Fact]
    public void OnlineInstaller_ShouldFailCliRegistrationWhenListDoesNotExposeExecutablePath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + claudeLog + "\"",
                        "if /I \"%1 %2\"==\"mcp list\" echo wpf-devtools",
                        "exit /b 0"
                    ]));

            var result = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("could not verify").And.Contain("mcp list");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
