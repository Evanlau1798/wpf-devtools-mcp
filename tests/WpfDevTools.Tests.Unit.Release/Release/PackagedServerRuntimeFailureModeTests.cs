using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("ProcessEnvironment")]
public sealed class PackagedServerRuntimeFailureModeTests
{
    [Fact]
    public void StopPackagedServerProcess_ShouldNotRequireLastExitCodeUnderStrictMode()
    {
        var command = BuildHelperBootstrap() + """

        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        $process = Start-FakePowerShellProcess "Start-Sleep -Seconds 2"
        if (-not (Stop-PackagedServerProcess -Process $process)) {
            throw 'Stop-PackagedServerProcess did not terminate the fake packaged server.'
        }

        $process.Dispose()
        Write-Output 'unset LASTEXITCODE cleanup succeeded'
        """;

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(5));

        result.ExitCode.Should().Be(0, $"stdout: {result.Stdout}; stderr: {result.Stderr}");
        result.Stdout.Should().Contain("unset LASTEXITCODE cleanup succeeded");
    }

    [Fact]
    public void ReadMcpResponse_ShouldFailFastWhenLiveServerNeverWritesStdout()
    {
        var command = BuildHelperBootstrap() + """

        $process = Start-FakePowerShellProcess "while (`$true) { Start-Sleep -Seconds 1 }"
        try {
            Read-McpResponse -Process $process -OperationName 'initialize' -ExpectedResponseId 1 -TimeoutMilliseconds 300
            throw 'Read-McpResponse unexpectedly accepted a silent live process.'
        }
        catch {
            if ($_.Exception.Message -notlike '*Timed out waiting for initialize response*') { throw }
            Write-Output 'silent live process failed fast'
        }
        finally {
            Stop-AndDisposeFakeProcess $process
        }
        """;

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(5));

        result.ExitCode.Should().Be(0, $"stdout: {result.Stdout}; stderr: {result.Stderr}");
        result.Stdout.Should().Contain("silent live process failed fast");
    }

    [Fact]
    public void ReadMcpResponse_ShouldFailFastWhenServerExitsBeforeResponse()
    {
        var command = BuildHelperBootstrap() + """

        $process = Start-FakePowerShellProcess "exit 0"
        try {
            $process.WaitForExit(5000) | Out-Null
            if (-not $process.HasExited) { throw 'Fake packaged server did not exit before closed-stdout test.' }
            Read-McpResponse -Process $process -OperationName 'initialize' -ExpectedResponseId 1 -TimeoutMilliseconds 1000
            throw 'Read-McpResponse unexpectedly accepted closed stdout.'
        }
        catch {
            if ($_.Exception.Message -notlike '*closed stdout before returning initialize response*') { throw }
            Write-Output 'closed stdout failed fast'
        }
        finally {
            Stop-AndDisposeFakeProcess $process
        }
        """;

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(5));

        result.ExitCode.Should().Be(0, $"stdout: {result.Stdout}; stderr: {result.Stderr}");
        result.Stdout.Should().Contain("closed stdout failed fast");
    }

    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldRejectTargetProcessIdWithoutTargetPathBeforeLaunch()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeServerPath = Path.Combine(tempRoot, "fake-server.exe");
            File.WriteAllText(fakeServerPath, "not a real executable");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1"),
                ["-ServerPath", fakeServerPath, "-TargetProcessId", "12345"],
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("TargetProcessPath is required when TargetProcessId is specified");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TestPackagedServerRuntimeScript_ShouldFailFastForMissingServerPath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var missingServerPath = Path.Combine(tempRoot, "missing-server.exe");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1"),
                ["-ServerPath", missingServerPath],
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("missing-server.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string BuildHelperBootstrap()
    {
        var scriptPath = ReleaseScriptTestHarness
            .GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1")
            .Replace("'", "''", StringComparison.Ordinal);

        return $$"""
        $scriptPath = '{{scriptPath}}'
        $source = Get-Content -LiteralPath $scriptPath -Raw
        $helperStart = $source.IndexOf('Set-StrictMode')
        $mainStart = $source.IndexOf('$resolvedServerPath =')
        if ($helperStart -lt 0 -or $mainStart -lt 0 -or $helperStart -ge $mainStart) { throw 'Could not locate packaged runtime smoke helper body.' }
        $helperRoot = Join-Path $env:TEMP ('packaged-smoke-functions-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $helperRoot | Out-Null
        $sourceDirectory = Split-Path -Parent $scriptPath
        Copy-Item -LiteralPath (Join-Path $sourceDirectory 'Test-McpToolListContract.ps1') -Destination $helperRoot -Force
        Copy-Item -LiteralPath (Join-Path $sourceDirectory 'Test-PackagedServerProcessCleanup.ps1') -Destination $helperRoot -Force
        $helperPath = Join-Path $helperRoot 'packaged-smoke-functions.ps1'
        Set-Content -LiteralPath $helperPath -Value $source.Substring($helperStart, $mainStart - $helperStart) -Encoding UTF8
        try {
            . $helperPath
        }
        finally {
            Remove-Item -LiteralPath $helperRoot -Recurse -Force -ErrorAction SilentlyContinue
        }

        function Start-FakePowerShellProcess {
            param([Parameter(Mandatory)] [string]$Command)
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'powershell.exe'
            $startInfo.Arguments = '-NoProfile -Command ' + [char]34 + $Command + [char]34
            $startInfo.UseShellExecute = $false
            $startInfo.RedirectStandardInput = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.CreateNoWindow = $true
            $process = [System.Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) { throw 'Failed to start fake packaged server.' }
            return $process
        }

        function Stop-AndDisposeFakeProcess {
            param([System.Diagnostics.Process]$Process)
            if ($null -ne $Process) {
                Stop-PackagedServerProcess -Process $Process
                $Process.Dispose()
            }
        }
        """;
    }
}
