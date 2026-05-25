using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerScreenshotResidueTests
{
    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveRuntimeScreenshotCache()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var environment = InstallerScriptTestSupport.CreateInstallerEnvironment(tempRoot);
            var screenshotDirectory = CreateRuntimeScreenshotCache(environment);

            var install = InstallOtherClient(tempRoot, archivePath, installRoot, environment);

            install.ExitCode.Should().Be(0, install.Stderr);

            var uninstall = InstallerScriptTestSupport.RunInstaller(
                tempRoot,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(screenshotDirectory).Should().BeFalse(
                "uninstall should purge retained runtime screenshot pixels");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRemoveRuntimeScreenshotCache()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var environment = InstallerScriptTestSupport.CreateInstallerEnvironment(tempRoot);
            var screenshotDirectory = CreateRuntimeScreenshotCache(environment);

            var install = InstallOtherClient(tempRoot, archivePath, installRoot, environment);

            install.ExitCode.Should().Be(0, install.Stderr);

            var uninstall = InstallerScriptTestSupport.RunInstaller(
                tempRoot,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(screenshotDirectory).Should().BeFalse(
                "full uninstall should purge retained runtime screenshot pixels");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) InstallOtherClient(
        string tempRoot,
        string archivePath,
        string installRoot,
        IReadOnlyDictionary<string, string?> environment)
        => InstallerScriptTestSupport.RunInstaller(
            tempRoot,
            [
                "-PackageArchivePath", archivePath,
                "-InstallRoot", installRoot,
                "-Client", "other",
                "-NonInteractive",
                "-Force",
                "-OutputJson"
            ],
            environment);

    private static string CreateRuntimeScreenshotCache(
        IReadOnlyDictionary<string, string?> environment)
    {
        var screenshotDirectory = Path.Combine(
            environment["LOCALAPPDATA"]!,
            "WpfDevTools",
            "tmp",
            "screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        File.WriteAllBytes(
            Path.Combine(screenshotDirectory, "shot_0123456789abcdef0123456789abcdef.png"),
            [137, 80, 78, 71]);
        return screenshotDirectory;
    }
}
