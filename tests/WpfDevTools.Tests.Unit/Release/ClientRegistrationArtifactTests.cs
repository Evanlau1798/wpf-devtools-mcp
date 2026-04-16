using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ClientRegistrationArtifactTests
{
    [Fact]
    public void OnlineInstaller_ShouldWriteVsCodeRegistrationToConfiguredJsonFile()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(vscodeConfigPath, "servers", "existing", "old.exe");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(vscodeConfigPath)
                .Should().Contain("\"servers\"")
                .And.Contain("existing")
                .And.Contain("wpf-devtools-x64.exe");
            Directory.GetFiles(Path.GetDirectoryName(vscodeConfigPath)!, "mcp.json.bak-*").Should().NotBeEmpty();

            using var json = JsonDocument.Parse(result.Stdout);
            var registration = json.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("mode").GetString().Should().Be("json-file");
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
            registration.GetProperty("target").GetString().Should().Be(vscodeConfigPath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldWriteVisualStudioRegistrationToUserProfileConfig()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(visualStudioConfigPath)
                .Should().Contain("\"servers\"")
                .And.Contain("wpf-devtools-x64.exe");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["visual-studio"]);
            json.RootElement.GetProperty("registrations").EnumerateArray().Select(x => x.GetProperty("mode").GetString())
                .Should().OnlyContain(mode => mode == "json-file");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveJsonRegistrationFromConfiguredFiles()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(vscodeConfigPath, "servers", "existing", "old.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(vscodeConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(vscodeConfigPath, "servers", "existing", "old.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveCustomJsonRegistrationWithoutRequiringOverrideAgain()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(customVisualStudioConfigPath, "servers", "existing", "custom-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", customVisualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(customVisualStudioConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(customVisualStudioConfigPath, "servers", "existing", "custom-existing.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldNotRollbackCustomVisualStudioRemovalWhenDefaultConfigStillContainsAnotherRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");
            var defaultVisualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");
            var environment = CreateInstallerEnvironment(tempRoot);
            JsonRegistrationTestAssertions.SeedRegistrationFile(customVisualStudioConfigPath, "servers", "existing", "custom-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", customVisualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                environment);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().Contain("wpf-devtools");

            Directory.CreateDirectory(Path.GetDirectoryName(defaultVisualStudioConfigPath)!);
            File.WriteAllText(
                defaultVisualStudioConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"type\":\"stdio\",\"command\":\"C:\\\\external\\\\wpf-devtools-x64.exe\",\"args\":[]}}}");

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                },
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(customVisualStudioConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(customVisualStudioConfigPath, "servers", "existing", "custom-existing.exe");
            using var defaultConfig = JsonDocument.Parse(File.ReadAllText(defaultVisualStudioConfigPath));
            defaultConfig.RootElement.GetProperty("servers").GetProperty("wpf-devtools").GetProperty("command").GetString()
                .Should().Be("C:\\external\\wpf-devtools-x64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveLiveJsonRegistrationWhenStateTargetIsStale()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var liveVisualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(liveVisualStudioConfigPath, "servers", "existing", "live-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(liveVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            var staleTargetPath = Path.Combine(tempRoot, "stale", ".mcp.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var visualStudio = registrations.GetProperty("visual-studio");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["visual-studio"] = new
                        {
                            architecture = visualStudio.GetProperty("architecture").GetString(),
                            installRoot = visualStudio.GetProperty("installRoot").GetString(),
                            mode = visualStudio.GetProperty("mode").GetString(),
                            target = staleTargetPath,
                            resolvedVersion = visualStudio.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = visualStudio.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = visualStudio.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(liveVisualStudioConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(liveVisualStudioConfigPath, "servers", "existing", "live-existing.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveArtifactOnlyRegistrationForOtherClient()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            File.Exists(artifactPath).Should().BeTrue();

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(artifactPath).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldIgnoreHostileStateTargetForOtherClientArtifact()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var other = registrations.GetProperty("other");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["other"] = new
                        {
                            architecture = other.GetProperty("architecture").GetString(),
                            installRoot = other.GetProperty("installRoot").GetString(),
                            mode = other.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = other.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = other.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = other.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(artifactPath).Should().BeFalse();
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveOtherArtifactWithoutExplicitInstallRootWhenManifestIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            var manifestPath = Path.Combine(installRoot, "x64", "install-manifest.json");
            File.Exists(artifactPath).Should().BeTrue();
            File.Delete(manifestPath);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(artifactPath).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldIgnoreHostileStateTargetForVisualStudioConfig()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var liveVisualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");
            JsonRegistrationTestAssertions.SeedRegistrationFile(liveVisualStudioConfigPath, "servers", "existing", "live-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(liveVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var visualStudio = registrations.GetProperty("visual-studio");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["visual-studio"] = new
                        {
                            architecture = visualStudio.GetProperty("architecture").GetString(),
                            installRoot = visualStudio.GetProperty("installRoot").GetString(),
                            mode = visualStudio.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = visualStudio.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = visualStudio.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = visualStudio.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(liveVisualStudioConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(liveVisualStudioConfigPath, "servers", "existing", "live-existing.exe");
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };
}
