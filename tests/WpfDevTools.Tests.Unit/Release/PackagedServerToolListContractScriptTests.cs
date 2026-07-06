using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class PackagedServerToolListContractScriptTests
{
    private static readonly string HelperPath = ReleaseScriptTestHarness.GetRepoFilePath(
        "scripts/tools/packaging/Test-McpToolListContract.ps1");

    [Fact]
    public void PackagedRuntimeSmoke_ShouldInvokeToolListContractHelper()
    {
        var script = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Test-PackagedServerRuntime.ps1"));

        script.Should().Contain("Test-McpToolListContract");
        script.Should().Contain("Get-PackagedServerExpectedToolNames");
        script.Should().NotContain("did not include get_processes");
    }

    [Fact]
    public void ToolListContract_WhenExpectedNamesAreNotSeventyOne_ShouldFail()
    {
        var result = RunHelperScript("""
            $expected = 1..70 | ForEach-Object { "tool$_" }
            $response = New-ToolsResponse -ToolNames $expected
            Test-McpToolListContract -ToolsResponse $response -ExpectedToolNames $expected
            """);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("expected 71");
    }

    [Fact]
    public void ToolListContract_WhenRuntimeOmitsExpectedTool_ShouldFail()
    {
        var result = RunHelperScript("""
            $expected = New-ExpectedToolNames
            $actual = @($expected | Where-Object { $_ -ne 'tool56' })
            $response = New-ToolsResponse -ToolNames $actual
            Test-McpToolListContract -ToolsResponse $response -ExpectedToolNames $expected
            """);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("tool56");
    }

    [Fact]
    public void ToolListContract_WhenRuntimeMatchesExpectedTools_ShouldPass()
    {
        var result = RunHelperScript("""
            $expected = New-ExpectedToolNames
            $response = New-ToolsResponse -ToolNames $expected
            Test-McpToolListContract -ToolsResponse $response -ExpectedToolNames $expected
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }

    private static ScriptRunResult RunHelperScript(string body)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "tool-contract-probe.ps1");
            File.WriteAllText(scriptPath, CreateProbeScript(body));
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [],
                timeout: TimeSpan.FromSeconds(30));

            return new ScriptRunResult(result.ExitCode, result.Stdout + result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateProbeScript(string body) =>
        $$"""
        $ErrorActionPreference = 'Stop'
        . {{QuotePowerShellString(HelperPath)}}

        function New-ExpectedToolNames {
            $representative = @(
                'connect',
                'get_processes',
                'get_ui_summary',
                'get_element_snapshot',
                'get_bindings',
                'capture_state_snapshot',
                'get_state_diff',
                'restore_state_snapshot'
            )
            return @($representative + (1..63 | ForEach-Object { "tool$_" }))
        }

        function New-ToolsResponse {
            param([Parameter(Mandatory)] [string[]]$ToolNames)
            $tools = @($ToolNames | ForEach-Object { [pscustomobject]@{ name = $_ } })
            return [pscustomobject]@{ result = [pscustomobject]@{ tools = $tools } }
        }

        {{body}}
        """;

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record ScriptRunResult(int ExitCode, string Output);
}
