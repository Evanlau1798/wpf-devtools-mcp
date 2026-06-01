using System.Text.Json;
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
        script.Should().Contain("WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE");
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

    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldWriteRuntimeEvidenceWhenRequested()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var serverPath = new[]
        {
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", configuration, "net8.0", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", configuration, "net8.0", "win-x64", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine("src", "WpfDevTools.Mcp.Server", "bin", "x64", configuration, "net8.0", "WpfDevTools.Mcp.Server.exe")
        }
        .Select(ReleaseScriptTestHarness.GetRepoFilePath)
        .FirstOrDefault(File.Exists);
        serverPath.Should().NotBeNull("the built MCP server executable is required for runtime evidence");

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var evidencePath = Path.Combine(tempRoot, "runtime-evidence.json");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                ["-ServerPath", serverPath!, "-EvidenceOutputPath", evidencePath],
                timeout: TimeSpan.FromSeconds(30));

            result.ExitCode.Should().Be(0, result.Stderr);
            using var evidence = JsonDocument.Parse(File.ReadAllText(evidencePath));
            var root = evidence.RootElement;
            root.GetProperty("toolsList").GetProperty("count").GetInt32().Should().Be(64);
            root.GetProperty("toolsList").GetProperty("nameSetHash").GetString()
                .Should().MatchRegex("^[a-f0-9]{64}$");
            root.GetProperty("toolsList").GetProperty("schemaSnapshotHash").GetString()
                .Should().MatchRegex("^[a-f0-9]{64}$");
            root.GetProperty("security").GetProperty("stdoutPurityPassed").GetBoolean().Should().BeTrue();
            root.GetProperty("liveSmoke").GetProperty("connect").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldFailFastOnNonJsonStdoutFromLiveProcess()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var responseTimeoutMilliseconds = (int)Math.Ceiling(
            ReleaseScriptTestHarness.ScaleTimeout(TimeSpan.FromSeconds(3)).TotalMilliseconds);

        var command = $$"""
$scriptPath = '{{scriptPath}}'
$source = Get-Content -LiteralPath $scriptPath -Raw
$helperStart = $source.IndexOf('Set-StrictMode')
$mainStart = $source.IndexOf('$resolvedServerPath =')
if ($helperStart -lt 0 -or $mainStart -lt 0 -or $helperStart -ge $mainStart) { throw 'Could not locate packaged runtime smoke helper body.' }
$helperPath = Join-Path $env:TEMP ('packaged-smoke-functions-' + [guid]::NewGuid().ToString('N') + '.ps1')
try {
    Set-Content -LiteralPath $helperPath -Value $source.Substring($helperStart, $mainStart - $helperStart) -Encoding UTF8
    . $helperPath

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = '-NoProfile -Command ' + [char]34 + "Write-Output 'not-json'; while (`$true) { Start-Sleep -Seconds 1 }" + [char]34
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) { throw 'Failed to start fake packaged server.' }
        Read-McpResponse -Process $process -OperationName 'initialize' -ExpectedResponseId 1 -TimeoutMilliseconds {{responseTimeoutMilliseconds}}
        throw 'Read-McpResponse unexpectedly accepted non-JSON stdout.'
    }
    catch {
        if ($_.Exception.Message -notlike '*stdout contamination*') { throw }
        Write-Output 'non-json stdout failed fast'
    }
    finally {
        if ($null -ne $process) {
            Stop-PackagedServerProcess -Process $process
            $process.Dispose()
        }
    }
}
finally {
    Remove-Item -LiteralPath $helperPath -Force -ErrorAction SilentlyContinue
}
""";

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(5));

        result.ExitCode.Should().Be(0, $"stdout: {result.Stdout}; stderr: {result.Stderr}");
        result.Stdout.Should().Contain("non-json stdout failed fast");
    }
}
