using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class PackagedServerRuntimeSmokeScriptTests
{
    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldSupportOptionalTargetProcessInspection()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1"));

        script.Should().Contain("[int]$TargetProcessId = 0");
        script.Should().Contain("[string]$TargetProcessPath = ''");
        script.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        script.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS");
        script.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS");
        script.Should().Contain("connect");
        script.Should().Contain("get_ui_summary");
        script.Should().Contain("FocusStatusTextBlock");
        script.Should().NotContain("NameTextBox");
        script.Should().Contain("TargetProcessId");
    }

    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldExerciseBuiltServerProtocolSurface()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var serverPathCandidates = new[]
        {
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", configuration, "net8.0", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", configuration, "net8.0", "win-x64", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", "x64", configuration, "net8.0", "WpfDevTools.Mcp.Server.exe")
        }
        .Select(ReleaseScriptTestHarness.GetRepoFilePath)
        .ToArray();
        var serverPath = serverPathCandidates.FirstOrDefault(File.Exists) ?? serverPathCandidates[0];

        File.Exists(serverPath).Should().BeTrue(
            "the unit test build should produce the {0} MCP server executable before the runtime smoke script is exercised; checked {1}",
            configuration,
            string.Join(", ", serverPathCandidates));

        var result = ReleaseScriptTestHarness.RunPowerShellScript(
            scriptPath,
            ["-ServerPath", serverPath],
            timeout: TimeSpan.FromSeconds(30));

        result.ExitCode.Should().Be(0,
            $"the packaged runtime smoke script should complete against the built server. Stdout: {result.Stdout}; Stderr: {result.Stderr}");
    }
}
