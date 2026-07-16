using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.StandaloneInstallerRegressionTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneInstallerLiveRegistrationOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullUninstall_ShouldPreferLiveJsonRegistrationOverStaleStateOwnership(bool standalone)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRootA = Path.Combine(tempRoot, "install-a");
            var installRootB = Path.Combine(tempRoot, "install-b");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var environment = CreateStandaloneEnvironment(tempRoot);

            RunRepoInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRootA, "-Client", "visual-studio", "-VisualStudioConfigPath", visualStudioConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment).ExitCode.Should().Be(0);
            RunRepoInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRootB, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                environment).ExitCode.Should().Be(0);

            var executableB = Path.Combine(installRootB, "x64", "current", "bin", "wpf-devtools-x64.exe");
            WriteVisualStudioRegistration(visualStudioConfigPath, executableB);

            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootA, "x64", "current", "bin", "installer"));
            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootB, "x64", "current", "bin", "installer"));
            var installerPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            IReadOnlyDictionary<string, string?> removalEnvironment = environment;
            if (standalone)
            {
                var standaloneRoot = Path.Combine(tempRoot, "standalone");
                Directory.CreateDirectory(standaloneRoot);
                installerPath = Path.Combine(standaloneRoot, "online-installer.ps1");
                File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), installerPath);
                ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
                removalEnvironment = new Dictionary<string, string?>(environment)
                {
                    ["WPFDEVTOOLS_INSTALLER_SOURCE_ROOT"] = null,
                    ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = null
                };
            }

            var removeA = RunScopedRemoval(installerPath, installRootA, visualStudioConfigPath, removalEnvironment);

            removeA.ExitCode.Should().Be(0, removeA.Stderr);
            Directory.Exists(installRootA).Should().BeFalse();
            Directory.Exists(Path.Combine(installRootB, "x64")).Should().BeTrue();
            File.ReadAllText(visualStudioConfigPath).Should().Contain(
                "wpf-devtools",
                "root A cleanup in mode {0} must preserve the live root B registration; result: {1}",
                standalone ? "standalone" : "shared",
                removeA.Stdout);
            File.ReadAllText(visualStudioConfigPath).Should().Contain(executableB.Replace("\\", "\\\\"));

            var removeB = RunScopedRemoval(installerPath, installRootB, visualStudioConfigPath, removalEnvironment);

            removeB.ExitCode.Should().Be(0, removeB.Stderr);
            Directory.Exists(installRootB).Should().BeFalse();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullUninstall_ShouldKeepDistinctLiveConfigTargetsIndependent(bool standalone)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRootA = Path.Combine(tempRoot, "install-a");
            var installRootB = Path.Combine(tempRoot, "install-b");
            var configA = Path.Combine(tempRoot, "config-a", ".mcp.json");
            var configB = Path.Combine(tempRoot, "config-b", ".mcp.json");
            var environment = CreateStandaloneEnvironment(tempRoot);
            var installAArguments = new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRootA, "-Client", "visual-studio", "-VisualStudioConfigPath", configA, "-NonInteractive", "-Force", "-OutputJson" };
            var installBArguments = new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRootB, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson" };

            RunRepoInstaller(tempRoot, installAArguments, environment).ExitCode.Should().Be(0);
            RunRepoInstaller(tempRoot, installBArguments, environment).ExitCode.Should().Be(0);
            var executableA = Path.Combine(installRootA, "x64", "current", "bin", "wpf-devtools-x64.exe");
            var executableB = Path.Combine(installRootB, "x64", "current", "bin", "wpf-devtools-x64.exe");
            WriteVisualStudioRegistration(configB, executableB);

            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootA, "x64", "current", "bin", "installer"));
            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootB, "x64", "current", "bin", "installer"));
            var installerPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            IReadOnlyDictionary<string, string?> removalEnvironment = environment;
            if (standalone)
            {
                var standaloneRoot = Path.Combine(tempRoot, "standalone-distinct-targets");
                Directory.CreateDirectory(standaloneRoot);
                installerPath = Path.Combine(standaloneRoot, "online-installer.ps1");
                File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), installerPath);
                ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
                removalEnvironment = new Dictionary<string, string?>(environment)
                {
                    ["WPFDEVTOOLS_INSTALLER_SOURCE_ROOT"] = null,
                    ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = null
                };
            }

            var removeB = RunScopedRemoval(installerPath, installRootB, configB, removalEnvironment);

            removeB.ExitCode.Should().Be(0, removeB.Stderr);
            Directory.Exists(installRootA).Should().BeTrue();
            Directory.Exists(installRootB).Should().BeFalse();
            File.ReadAllText(configA).Should().Contain(executableA.Replace("\\", "\\\\"));
            File.ReadAllText(configB).Should().NotContain("wpf-devtools");

            RunRepoInstaller(tempRoot, installBArguments, environment).ExitCode.Should().Be(0);
            WriteVisualStudioRegistration(configB, executableB);
            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootB, "x64", "current", "bin", "installer"));
            var globalRemoval = ReleaseScriptTestHarness.RunPowerShellScript(
                installerPath,
                ["-Action", "full-uninstall", "-VisualStudioConfigPath", configB, "-NonInteractive", "-Force", "-OutputJson"],
                removalEnvironment);

            globalRemoval.ExitCode.Should().Be(0, globalRemoval.Stderr);
            Directory.Exists(installRootA).Should().BeFalse();
            Directory.Exists(installRootB).Should().BeFalse();
            File.ReadAllText(configA).Should().NotContain("wpf-devtools");
            File.ReadAllText(configB).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullUninstall_ShouldRefreshOwnershipForPersistedConfigTarget(bool standalone)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRootA = Path.Combine(tempRoot, "install-a");
            var installRootB = Path.Combine(tempRoot, "install-b");
            var configA = Path.Combine(tempRoot, "config-a", ".mcp.json");
            var configB = Path.Combine(tempRoot, "config-b", ".mcp.json");
            var environment = CreateStandaloneEnvironment(tempRoot);

            RunRepoInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRootA, "-Client", "visual-studio", "-VisualStudioConfigPath", configA, "-NonInteractive", "-Force", "-OutputJson"],
                environment).ExitCode.Should().Be(0);
            RunRepoInstaller(
                tempRoot,
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRootB, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                environment).ExitCode.Should().Be(0);
            var executableB = Path.Combine(installRootB, "x64", "current", "bin", "wpf-devtools-x64.exe");
            WriteVisualStudioRegistration(configA, executableB);
            WriteVisualStudioRegistration(configB, executableB);

            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootA, "x64", "current", "bin", "installer"));
            ReleaseScriptTestHarness.DeleteDirectory(Path.Combine(installRootB, "x64", "current", "bin", "installer"));
            var installerPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            IReadOnlyDictionary<string, string?> removalEnvironment = environment;
            if (standalone)
            {
                var standaloneRoot = Path.Combine(tempRoot, "standalone-refreshed-ownership");
                Directory.CreateDirectory(standaloneRoot);
                installerPath = Path.Combine(standaloneRoot, "online-installer.ps1");
                File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), installerPath);
                ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
                removalEnvironment = new Dictionary<string, string?>(environment)
                {
                    ["WPFDEVTOOLS_INSTALLER_SOURCE_ROOT"] = null,
                    ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = null
                };
            }

            var removeA = RunScopedRemoval(installerPath, installRootA, configB, removalEnvironment);

            removeA.ExitCode.Should().Be(0, removeA.Stderr);
            Directory.Exists(installRootA).Should().BeFalse();
            Directory.Exists(installRootB).Should().BeTrue();
            File.ReadAllText(configA).Should().Contain(executableB.Replace("\\", "\\\\"));
            File.ReadAllText(configB).Should().Contain(executableB.Replace("\\", "\\\\"));

            var removeB = RunScopedRemoval(installerPath, installRootB, configA, removalEnvironment);

            removeB.ExitCode.Should().Be(0, removeB.Stderr);
            Directory.Exists(installRootB).Should().BeFalse();
            File.ReadAllText(configA).Should().NotContain("wpf-devtools");
            File.ReadAllText(configB).Should().Contain("wpf-devtools");

            var removeRemainingRegistration = ReleaseScriptTestHarness.RunPowerShellScript(
                installerPath,
                ["-Action", "full-uninstall", "-VisualStudioConfigPath", configB, "-NonInteractive", "-Force", "-OutputJson"],
                removalEnvironment);

            removeRemainingRegistration.ExitCode.Should().Be(0, removeRemainingRegistration.Stderr);
            File.ReadAllText(configB).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void WriteVisualStudioRegistration(string path, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                servers = new Dictionary<string, object>
                {
                    ["wpf-devtools"] = new { command = executable, args = Array.Empty<string>() }
                }
            }));
    }

    private static (int ExitCode, string Stdout, string Stderr) RunScopedRemoval(
        string installerPath,
        string installRoot,
        string visualStudioConfigPath,
        IReadOnlyDictionary<string, string?> environment)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            installerPath,
            ["-Action", "full-uninstall", "-InstallRoot", installRoot, "-VisualStudioConfigPath", visualStudioConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
            environment);
}
