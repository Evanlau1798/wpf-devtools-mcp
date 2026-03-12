using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ClientRegistrationArtifactTests
{
    [Fact]
    public void InstallScript_ShouldCreateGitHubCopilotAndGenericRegistrationArtifacts()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var registrationDir = Path.Combine(installRoot, "x64", "client-registration");
            File.ReadAllText(Path.Combine(registrationDir, "github-copilot-vscode.json"))
                .Should().Contain("WpfDevTools.Mcp.Server.exe")
                .And.Contain("\"servers\"");
            File.ReadAllText(Path.Combine(registrationDir, "other.mcpServers.json"))
                .Should().Contain("WpfDevTools.Mcp.Server.exe")
                .And.Contain("\"mcpServers\"");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldAcceptArtifactOnlyClientsWithoutExternalRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "github-copilot-vscode,other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(new[] { "github-copilot-vscode", "other" });
            json.RootElement.GetProperty("registrations").EnumerateArray().Select(x => x.GetProperty("mode").GetString())
                .Should().OnlyContain(mode => mode == "artifact-only");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
