using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.InstallerScriptTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareVerificationCommandHelper()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("scripts/installer/Installer.Verification.Commands.ps1");
        manifestContent.Should().Contain("Installer.Verification.Commands.ps1");
        content.IndexOf(
                "scripts/installer/Installer.Verification.Commands.ps1",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "scripts/installer/Installer.Verification.ps1",
                StringComparison.Ordinal));
        content.IndexOf(
                "'Installer.Verification.Commands.ps1'",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "'Installer.Verification.ps1'",
                StringComparison.Ordinal));
    }

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
