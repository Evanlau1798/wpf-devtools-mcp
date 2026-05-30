using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.E2E;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class OnlineInstallerIntegrationTests
{
    private static readonly TimeSpan BuildReleaseTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task OnlineInstaller_ShouldInstallFromLocalArchiveWithoutNetwork_AndPersistState()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var packageResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot },
                timeout: BuildReleaseTimeout);
            packageResult.ExitCode.Should().Be(0, packageResult.Stderr);

            var archivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var installResult = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Version", "latest",
                    "-Architecture", "x64",
                    "-Client", "vscode",
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Force",
                    "-OutputJson"
                },
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
            var registeredExecutable = AssertJsonRegistration(vscodeConfigPath, "servers", installedExecutable);

            AssertGeneratedRegistrationArtifacts(
                Path.Combine(installRoot, "x64", "client-registration"),
                installedExecutable);

            await AssertInstalledMcpServerListsExpectedToolsAsync(
                registeredExecutable,
                appData,
                localAppData,
                userProfile,
                Path.Combine(tempRoot, "McpCerts"));
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string AssertJsonRegistration(
        string configPath,
        string collectionName,
        string expectedExecutable)
    {
        File.Exists(configPath).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        var server = document.RootElement
            .GetProperty(collectionName)
            .GetProperty("wpf-devtools");

        server.GetProperty("type").GetString().Should().Be("stdio");
        server.GetProperty("args").EnumerateArray().Should().BeEmpty();

        var command = server.GetProperty("command").GetString();
        command.Should().NotBeNullOrWhiteSpace();
        command.Should().Be(expectedExecutable);
        Path.IsPathFullyQualified(command!).Should().BeTrue();
        File.Exists(command!).Should().BeTrue();

        return command!;
    }

    private static void AssertGeneratedRegistrationArtifacts(
        string registrationDirectory,
        string expectedExecutable)
    {
        var jsonArtifacts = new[]
        {
            (FileName: "vscode.json", CollectionName: "servers"),
            (FileName: "visual-studio.json", CollectionName: "servers"),
            (FileName: "cursor.global.json", CollectionName: "mcpServers"),
            (FileName: "cursor.project.json", CollectionName: "mcpServers"),
            (FileName: "claude-desktop.json", CollectionName: "mcpServers"),
            (FileName: "other.mcpServers.json", CollectionName: "mcpServers")
        };

        foreach (var artifact in jsonArtifacts)
        {
            AssertJsonRegistration(
                Path.Combine(registrationDirectory, artifact.FileName),
                artifact.CollectionName,
                expectedExecutable);
        }

        File.ReadAllText(Path.Combine(registrationDirectory, "claude-code.txt"))
            .Should().Contain($"claude mcp add --transport stdio wpf-devtools -- \"{expectedExecutable}\"");
        File.ReadAllText(Path.Combine(registrationDirectory, "codex.txt"))
            .Should().Contain($"codex mcp add wpf-devtools -- \"{expectedExecutable}\"");
    }

    private static async Task AssertInstalledMcpServerListsExpectedToolsAsync(
        string serverExecutable,
        string appData,
        string localAppData,
        string userProfile,
        string certDirectory)
    {
        using var client = new McpStdioClient();
        var initializeResponse = await client.StartAsync(
            serverExecutable,
            new Dictionary<string, string>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile,
                ["WPFDEVTOOLS_AUTH_SECRET"] = CreateAuthSecret(),
                ["WPFDEVTOOLS_CERT_DIR"] = certDirectory
            });
        initializeResponse.TryGetProperty("error", out _).Should().BeFalse(initializeResponse.GetRawText());
        initializeResponse.TryGetProperty("result", out _).Should().BeTrue(initializeResponse.GetRawText());

        var response = await client.ListToolsAsync();
        var toolNames = response.GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        toolNames.Should().Contain(new[]
        {
            "get_processes",
            "connect",
            "ping",
            "get_ui_summary",
            "get_binding_errors"
        });
    }

    private static string CreateAuthSecret()
    {
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        return Convert.ToBase64String(secretBytes);
    }
}
