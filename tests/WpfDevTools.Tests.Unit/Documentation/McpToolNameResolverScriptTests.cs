using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit.Release;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class McpToolNameResolverScriptTests
{
    private static readonly string HelperPath = ReleaseScriptTestHarness.GetRepoFilePath(
        "scripts/ci/Get-McpToolNames.ps1");

    [Fact]
    public void SourceAttributes_ShouldParseMultilineEscapedAndDeduplicateNames()
    {
        var fixture = CreateFixture("""
            [McpServerTool(
                Title = "Connect",
                Name
                    =
                    "connect")]
            public static void Connect() { }

            [McpServerTool(Name = "connect", Title = "Duplicate")]
            public static void ConnectDuplicate() { }

            [McpServerTool(
                Title = "Escaped",
                Name = "tool\"quote")]
            public static void Escaped() { }

            [McpServerTool(
                Tags = new[] { "tree", "agent]" },
                Name = "bracketed")]
            public static void Bracketed() { }
            """);

        try
        {
            var result = RunHelperScript(fixture, """
                ConvertTo-Json -InputObject @(Get-McpToolNamesFromSourceAttributes -RepoRoot $repoRoot) -Compress
                """);

            result.ExitCode.Should().Be(0, result.Output);
            var names = JsonSerializer.Deserialize<string[]>(result.Output.Trim())!;
            names.Should().BeEquivalentTo(["bracketed", "connect", "tool\"quote"], options => options.WithStrictOrdering());
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(fixture);
        }
    }

    [Fact]
    public void ToolNames_WhenRuntimeManifestCannotLoad_ShouldFallBackToSourceAttributes()
    {
        var fixture = CreateFixture("""[McpServerTool(Name = "source_tool")] public static void SourceTool() { }""");

        try
        {
            var runtimeDll = Path.Combine(
                fixture,
                "src",
                "WpfDevTools.Mcp.Server",
                "bin",
                "Debug",
                "net8.0",
                "WpfDevTools.Mcp.Server.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeDll)!);
            File.WriteAllText(runtimeDll, "not a managed assembly");

            var result = RunHelperScript(fixture, """
                ConvertTo-Json -InputObject @(Get-McpToolNames -RepoRoot $repoRoot) -Compress
                """);

            result.ExitCode.Should().Be(0, result.Output);
            var names = JsonSerializer.Deserialize<string[]>(result.Output.Trim())!;
            names.Should().Equal("source_tool");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(fixture);
        }
    }

    [Fact]
    public void ToolNames_WhenRuntimeManifestIsOlderThanToolSource_ShouldFallBackToSourceAttributes()
    {
        var fixture = CreateFixture("""[McpServerTool(Name = "fresh_source")] public static void FreshSource() { }""");

        try
        {
            var result = RunHelperScript(fixture, """
                $binRoot = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\bin\Debug\net48'
                New-Item -ItemType Directory -Force -Path $binRoot | Out-Null
                $runtimeDll = Join-Path $binRoot 'WpfDevTools.Mcp.Server.dll'
                $typeDefinition = @'
                namespace WpfDevTools.Mcp.Server.McpResources {
                    public static class CapabilityResources {
                        public static string GetToolManifest() {
                            return "{\"tools\":[{\"name\":\"stale_runtime\"}]}";
                        }
                    }
                }
                '@
                Add-Type -TypeDefinition $typeDefinition -OutputAssembly $runtimeDll -Language CSharp
                $sourceFile = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\McpTools\FakeMcpTools.cs'
                (Get-Item -LiteralPath $runtimeDll).LastWriteTimeUtc = (Get-Item -LiteralPath $sourceFile).LastWriteTimeUtc.AddMinutes(-10)

                ConvertTo-Json -InputObject @(Get-McpToolNames -RepoRoot $repoRoot) -Compress
                """);

            result.ExitCode.Should().Be(0, result.Output);
            var names = JsonSerializer.Deserialize<string[]>(result.Output.Trim())!;
            names.Should().Equal("fresh_source");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(fixture);
        }
    }

    private static string CreateFixture(string toolSource)
    {
        var fixture = ReleaseScriptTestHarness.CreateTempDirectory();
        WriteFile(fixture, "src/WpfDevTools.Mcp.Server/McpTools/FakeMcpTools.cs", toolSource);
        return fixture;
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ScriptRunResult RunHelperScript(string repoRoot, string body)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "tool-name-resolver-probe.ps1");
            File.WriteAllText(scriptPath, CreateProbeScript(repoRoot, body));
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

    private static string CreateProbeScript(string repoRoot, string body) =>
        $$"""
        $ErrorActionPreference = 'Stop'
        . {{QuotePowerShellString(HelperPath)}}
        $repoRoot = {{QuotePowerShellString(repoRoot)}}

        {{body}}
        """;

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record ScriptRunResult(int ExitCode, string Output);
}
