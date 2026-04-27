using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class PackagedServerRuntimeSmokeScriptTests
{
    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldExerciseBuiltServerProtocolSurface()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1");
        var serverPath = ReleaseScriptTestHarness.GetRepoFilePath("src/WpfDevTools.Mcp.Server/bin/Debug/net8.0/WpfDevTools.Mcp.Server.exe");

        File.Exists(serverPath).Should().BeTrue(
            "the unit test build should produce the MCP server executable before the runtime smoke script is exercised");

        var result = ReleaseScriptTestHarness.RunPowerShellScript(
            scriptPath,
            ["-ServerPath", serverPath],
            timeout: TimeSpan.FromSeconds(30));

        result.ExitCode.Should().Be(0,
            $"the packaged runtime smoke script should complete against the built server. Stdout: {result.Stdout}; Stderr: {result.Stderr}");
    }
}