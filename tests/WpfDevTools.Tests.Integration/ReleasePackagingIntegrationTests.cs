using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.E2E;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class ReleasePackagingIntegrationTests
{
    private static readonly TimeSpan BuildReleaseTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task BuildReleaseScript_MultiArchitecturePackage_ShouldInstallAndExposeExpectedRuntime()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var escapedOutputRoot = outputRoot.Replace("'", "''");
            var command = "& '" +
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1").Replace("'", "''") +
                "' -Configuration Debug -Architectures @('x64','x86') -OutputRoot '" + escapedOutputRoot + "'";
            var result = ReleasePackagingTestHarness.RunPowerShellCommand(
                command,
                timeout: BuildReleaseTimeout);

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(outputRoot, "SHA256SUMS.txt")).Should().BeTrue();
            File.Exists(Path.Combine(outputRoot, "release-assets.json")).Should().BeTrue();

            var x64ArchivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var x86ArchivePath = Directory.GetFiles(outputRoot, "release_*_win-x86.zip").Single();
            var extractRoot = ReleasePackagingTestHarness.ExtractArchive(x86ArchivePath, Path.Combine(tempRoot, "x86-extract"));
            var inspectorNet8Root = Path.Combine(extractRoot, "bin", "inspectors", "net8.0-windows");

            Directory.Exists(Path.Combine(inspectorNet8Root, "win-x64")).Should().BeFalse(
                "the x86 package must not include RID-specific inspector output from a previous x64 publish");

            AssertPackageLayout(x64ArchivePath, tempRoot);
            await AssertOfflineInstallAsync(x64ArchivePath, tempRoot);
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_ReleaseMode_ShouldFailWhenPayloadSigningRequirementsAreNotMet()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"),
                new[] { "-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", outputRoot },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "1",
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "NotSigned"
                },
                timeout: BuildReleaseTimeout);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("signature",
                "Release packaging should refuse to emit RequireAuthenticodeSignature packages when payloads are unsigned");
            Directory.GetFiles(outputRoot, "release_*_win-*.zip").Should().BeEmpty();
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_ReleaseMode_ShouldRejectForcedSignatureStatusOutsideTestMode()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"),
                new[] { "-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", outputRoot },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_TEST_SIGNATURE_STATUS",
                "production packaging must not honor test-only signature overrides");
            Directory.GetFiles(outputRoot, "release_*_win-*.zip").Should().BeEmpty();
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_WhenBootstrapperStepFails_ShouldCleanPartialArm64PackageDirectory()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var fakeMsbuildLog = Path.Combine(tempRoot, "fake-msbuild.log");
            var fakeMsbuild = ReleasePackagingTestHarness.CreateFakeCommand(tempRoot, "fake-msbuild", fakeMsbuildLog, "exit /b 1");

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "arm64", "-OutputRoot", outputRoot },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("arm64",
                "the failure should identify which release architecture could not be packaged");
            Directory.Exists(Path.Combine(outputRoot, "release_0.1.0_win-arm64")).Should().BeFalse(
                "failed packaging should clean partial package directories so later retries start from a clean slate");
            File.Exists(Path.Combine(outputRoot, "release_0.1.0_win-arm64.zip")).Should().BeFalse();
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void AssertPackageLayout(string archivePath, string tempRoot)
    {
        var extractRoot = ReleasePackagingTestHarness.ExtractArchive(
            archivePath,
            Path.Combine(tempRoot, "x64-extract"));
        File.Exists(Path.Combine(extractRoot, "run.bat")).Should().BeTrue();
        File.Exists(Path.Combine(extractRoot, "bin", "install.ps1")).Should().BeTrue(
            CreateArchiveLayoutDiagnostic(archivePath, extractRoot));
        foreach (var helper in new[]
        {
            "Tui.ScreenModel.ps1", "Tui.Renderer.ps1", "Tui.Input.ps1", "Tui.Flow.ps1",
            "Tui.Confirm.ps1", "Installer.Discovery.ps1", "Installer.Uninstall.ps1"
        })
        {
            File.Exists(Path.Combine(extractRoot, "bin", "installer", helper)).Should().BeTrue(helper);
        }

        foreach (var relativePath in new[]
        {
            "bin/manifest.json", "bin/wpf-devtools-x64.exe", "bin/WpfDevTools.Inspector.Sdk.dll",
            "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
            "bin/inspectors/net48/WpfDevTools.Inspector.dll",
            "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
        })
        {
            File.Exists(Path.Combine(extractRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue(relativePath);
        }

        File.Exists(Path.Combine(extractRoot, "bin", "internal-install.ps1")).Should().BeFalse();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(extractRoot, "bin", "manifest.json")));
        manifest.RootElement.GetProperty("entryExecutable").GetString().Should().Be("bin/wpf-devtools-x64.exe");
    }

    private static async Task AssertOfflineInstallAsync(string archivePath, string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "install-root");
        var appData = Path.Combine(tempRoot, "AppData", "Roaming");
        var localAppData = Path.Combine(tempRoot, "AppData", "Local");
        var userProfile = Path.Combine(tempRoot, "UserProfile");
        var installResult = ReleasePackagingTestHarness.RunPowerShellScript(
            ReleasePackagingTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            [
                "-Version", "latest", "-Architecture", "x64", "-Client", "vscode",
                "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Force", "-OutputJson"
            ],
            new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            });

        installResult.ExitCode.Should().Be(0, installResult.Stderr);
        var installedExecutable = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
        File.Exists(installedExecutable).Should().BeTrue();
        File.Exists(Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json")).Should().BeTrue();
        using var json = JsonDocument.Parse(installResult.Stdout);
        json.RootElement.GetProperty("mode").GetString().Should().Be("offline");
        json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("vscode");
        json.RootElement.GetProperty("statePath").GetString().Should().EndWith("installer-state.json");

        var vscodeConfigPath = Path.Combine(appData, "Code", "User", "mcp.json");
        using var registration = JsonDocument.Parse(File.ReadAllText(vscodeConfigPath));
        var command = registration.RootElement.GetProperty("servers").GetProperty("wpf-devtools")
            .GetProperty("command").GetString();
        command.Should().Be(installedExecutable);
        Path.IsPathFullyQualified(command!).Should().BeTrue();

        var registrationRoot = Path.Combine(installRoot, "x64", "client-registration");
        foreach (var artifact in new[]
        {
            ("vscode.json", "servers"), ("visual-studio.json", "servers"),
            ("cursor.global.json", "mcpServers"), ("cursor.project.json", "mcpServers"),
            ("claude-desktop.json", "mcpServers"), ("other.mcpServers.json", "mcpServers")
        })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(registrationRoot, artifact.Item1)));
            document.RootElement.GetProperty(artifact.Item2).GetProperty("wpf-devtools")
                .GetProperty("command").GetString().Should().Be(installedExecutable, artifact.Item1);
        }

        File.ReadAllText(Path.Combine(registrationRoot, "claude-code.txt"))
            .Should().Contain($"claude mcp add --transport stdio wpf-devtools -- \"{installedExecutable}\"");
        File.ReadAllText(Path.Combine(registrationRoot, "codex.txt"))
            .Should().Contain($"codex mcp add wpf-devtools -- \"{installedExecutable}\"");

        using var client = new McpStdioClient();
        var initializeResponse = await client.StartAsync(
            command!,
            new Dictionary<string, string>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile,
                ["WPFDEVTOOLS_AUTH_SECRET"] = CreateAuthSecret(),
                ["WPFDEVTOOLS_CERT_DIR"] = Path.Combine(tempRoot, "McpCerts")
            });
        initializeResponse.TryGetProperty("error", out _).Should().BeFalse(initializeResponse.GetRawText());
        var tools = (await client.ListToolsAsync()).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        tools.Should().HaveCount(72);
        tools.Select(tool => tool.GetProperty("name").GetString()).Should()
            .Contain(["connect", "get_ui_summary", "get_binding_errors"]);
    }

    private static string CreateAuthSecret()
    {
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        return Convert.ToBase64String(secretBytes);
    }

    private static string CreateArchiveLayoutDiagnostic(string archivePath, string extractRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var archiveEntries = archive
            .Entries
            .Select(entry => entry.FullName)
            .Order(StringComparer.Ordinal)
            .Take(80);
        var extractedEntries = Directory.EnumerateFileSystemEntries(extractRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(extractRoot, path))
            .Order(StringComparer.Ordinal)
            .Take(80);

        return "archive entries: " + string.Join(" | ", archiveEntries) +
            "; extracted entries: " + string.Join(" | ", extractedEntries);
    }
}
