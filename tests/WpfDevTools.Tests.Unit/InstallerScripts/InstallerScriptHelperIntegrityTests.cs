using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.InstallerScriptTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerScriptTests
{
    [Fact]
    public void OnlineInstaller_Uninstall_ShouldNotCreateDefaultInstallRootWhenOnlyExternalRegistrationWasDetected()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var installRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(visualStudioConfigPath)!);
            File.WriteAllText(
                visualStudioConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"C:\\\\external\\\\wpf-devtools-x64.exe\",\"args\":[]}}}");

            var uninstall = RunInstaller(
                tempRoot,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            Directory.Exists(installRoot).Should().BeFalse("external-registration cleanup must not create a new default install root");
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackagedInstaller_ShouldRejectTamperedHelperPayloadBeforeInteractiveBootstrap()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var extractRoot = Path.Combine(tempRoot, "expanded");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var helperPath = Path.Combine(extractRoot, "bin", "installer", "Tui.Flow.ps1");
            File.AppendAllText(helperPath, Environment.NewLine + "# tampered helper payload");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-Action", "install", "-Architecture", "x64", "-Client", "other"],
                CreateInstallerEnvironment(
                    tempRoot,
                    new Dictionary<string, string?>
                    {
                        ["WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS"] = "Escape||Enter",
                        ["WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR"] = "1",
                        ["WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI"] = "1",
                        ["WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION"] = "1.2.3"
                    }));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("helper").And.Contain("integrity");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackagedInstaller_ShouldRejectTamperedBootstrapHelperBeforeExecutingIt()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var extractRoot = Path.Combine(tempRoot, "expanded");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var markerPath = Path.Combine(tempRoot, "bootstrap-executed.txt");
            var helperPath = Path.Combine(extractRoot, "bin", "installer", "Installer.BootstrapUi.ps1");
            File.WriteAllText(
                helperPath,
                "Set-Content -Path '" + markerPath.Replace("'", "''") + "' -Value executed" + Environment.NewLine);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(extractRoot, "bin", "install.ps1"),
                ["-Action", "install", "-Architecture", "x64", "-Client", "other"],
                CreateInstallerEnvironment(
                    tempRoot,
                    new Dictionary<string, string?>
                    {
                        ["WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS"] = "Escape||Enter",
                        ["WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR"] = "1",
                        ["WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI"] = "1",
                        ["WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION"] = "1.2.3"
                    }));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Installer.BootstrapUi.ps1").And.Contain("integrity");
            File.Exists(markerPath).Should().BeFalse("tampered bootstrap helpers must be rejected before dot-sourcing");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RawInstallerScriptExecution_ShouldNotTrustHelpersFromCurrentWorkingDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var helperRoot = Path.Combine(tempRoot, "scripts", "installer");
            Directory.CreateDirectory(helperRoot);

            using var manifest = JsonDocument.Parse(
                File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json")));
            foreach (var entry in manifest.RootElement.GetProperty("helperFiles").EnumerateArray())
            {
                var helperName = entry.ValueKind == JsonValueKind.Object
                    ? entry.GetProperty("path").GetString()!
                    : entry.GetString()!;
                File.Copy(
                    ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", helperName)),
                    Path.Combine(helperRoot, helperName),
                    overwrite: true);
            }

            var command = string.Join(" ; ",
            [
                "$repoScriptPath='" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1").Replace("'", "''") + "'",
                "$archivePath='" + archivePath.Replace("'", "''") + "'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                ". ([scriptblock]::Create((Get-Content $repoScriptPath -Raw))) -Action install -Architecture x64 -Client other -PackageArchivePath $archivePath -NonInteractive -Force -OutputJson | Out-Null",
                "(Get-LocalInstallerHelperRoots) -join ';'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().NotContain(Path.Combine(tempRoot, "scripts", "installer"));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
