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
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var registrationDir = Path.Combine(installRoot, "x64", "client-registration");
            File.ReadAllText(Path.Combine(registrationDir, "github-copilot-vscode.json"))
                .Should().Contain("wpf-devtools-x64.exe")
                .And.Contain("\"servers\"");
            File.ReadAllText(Path.Combine(registrationDir, "other.mcpServers.json"))
                .Should().Contain("wpf-devtools-x64.exe")
                .And.Contain("\"mcpServers\"");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldAcceptVisualStudioAsAFileBasedRegistrationTarget()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["USERPROFILE"] = userProfile
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(new[] { "visual-studio" });
            json.RootElement.GetProperty("registrations").EnumerateArray().Select(x => x.GetProperty("mode").GetString())
                .Should().OnlyContain(mode => mode == "json-file");
            File.ReadAllText(Path.Combine(userProfile, ".mcp.json"))
                .Should().Contain("\"servers\"")
                .And.Contain("wpf-devtools-x64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
