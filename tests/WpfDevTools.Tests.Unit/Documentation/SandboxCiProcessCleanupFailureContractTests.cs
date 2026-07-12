using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxProcessCleanup_StartSmokeTarget_ShouldPreserveStartupFailureWhenCleanupThrows()
    {
        var tempRoot = CreateTempRoot();
        var token = $"smoke-cleanup-failure-{Guid.NewGuid():N}";
        try
        {
            var smokeProjectRoot = Path.Combine(tempRoot, token);
            var smokeTargetPath = CreateSleeperExecutable(smokeProjectRoot);
            var childStartedPath = Path.Combine(smokeProjectRoot, "child-started.txt");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ProcessCleanup.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "cleanup-failure-output.txt");
            var command = $$"""
            try {
            $ErrorActionPreference = 'Stop'
            {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
            function Stop-SmokeTarget { throw 'simulated cleanup failure' }

            $env:WPFDEVTOOLS_TEST_CHILD_MARKER = '{{token}}'
            $env:WPFDEVTOOLS_TEST_CHILD_STARTED_PATH = '{{EscapePowerShellPath(childStartedPath)}}'
            $SmokeTargetPath = '{{EscapePowerShellPath(smokeTargetPath)}}'
            $SmokeTargetStartupTimeoutSeconds = 0
            try {
                Start-SmokeTarget | Out-Null
                throw 'Start-SmokeTarget unexpectedly returned.'
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', $_.Exception.Message)
            }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "HARNESS FAILURE: $($_ | Out-String)")
                exit 1
            }
            """;
            var probePath = Path.Combine(tempRoot, "probe-cleanup-failure.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);
            var output = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";

            result.ExitCode.Should().Be(0, output);
            output.Should().Contain("Timed out waiting for smoke target main window:");
            output.Should().Contain("simulated cleanup failure");
        }
        finally
        {
            KillProcessesCommandLineContains(token);
            DeleteTempRoot(tempRoot);
        }
    }
}
