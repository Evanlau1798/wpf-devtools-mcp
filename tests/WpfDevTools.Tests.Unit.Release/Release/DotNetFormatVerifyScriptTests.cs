using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class DotNetFormatVerifyScriptTests
{
    [Fact]
    public void FormatVerify_ShouldRetryExactTransientDotnetCliResolutionFailure()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "dotnet-format.log");
            var fakeDotNet = WriteFakeDotNetCommand(
                tempRoot,
                logPath,
                transientFirstFormatAttempt: true,
                nonTransientFormatFailure: false,
                alwaysTransientFormatFailure: false);

            var result = RunFormatVerify(fakeDotNet, retryDelaySeconds: 0);

            result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
            File.ReadAllLines(logPath).Should().HaveCount(2);
            result.Stdout.Should().Contain("Retrying dotnet format verification");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FormatVerify_ShouldNotRetryNonTransientFormattingFailure()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "dotnet-format-failure.log");
            var fakeDotNet = WriteFakeDotNetCommand(
                tempRoot,
                logPath,
                transientFirstFormatAttempt: false,
                nonTransientFormatFailure: true,
                alwaysTransientFormatFailure: false);

            var result = RunFormatVerify(fakeDotNet, retryDelaySeconds: 0);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr).Should().Contain("synthetic formatting failure");
            File.ReadAllLines(logPath).Should().ContainSingle();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void FormatVerify_ShouldPreserveExitCodeAfterBoundedTransientRetriesAreExhausted()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "dotnet-format-exhausted.log");
            var fakeDotNet = WriteFakeDotNetCommand(
                tempRoot,
                logPath,
                transientFirstFormatAttempt: false,
                nonTransientFormatFailure: false,
                alwaysTransientFormatFailure: true);

            var result = RunFormatVerify(fakeDotNet, retryDelaySeconds: 0, maxAttempts: 2);

            result.ExitCode.Should().Be(7, result.Stdout + result.Stderr);
            File.ReadAllLines(logPath).Should().HaveCount(2);
            var normalizedOutput = string.Join(
                ' ',
                (result.Stdout + Environment.NewLine + result.Stderr)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            normalizedOutput.Should().Contain("failed after 2 attempts");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunFormatVerify(
        string dotNetPath,
        int retryDelaySeconds,
        int maxAttempts = 3)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/ci/Invoke-DotNetFormatVerify.ps1"),
            [
                "-SolutionPath", "WpfDevTools.sln",
                "-Include", "src/WpfDevTools.Mcp.Server/Composer",
                "-DotNetPath", dotNetPath,
                "-MaxAttempts", maxAttempts.ToString(),
                "-RetryDelaySeconds", retryDelaySeconds.ToString()
            ],
            timeout: TimeSpan.FromSeconds(20));

    private static string WriteFakeDotNetCommand(
        string tempRoot,
        string logPath,
        bool transientFirstFormatAttempt,
        bool nonTransientFormatFailure,
        bool alwaysTransientFormatFailure)
    {
        var statePath = Path.Combine(tempRoot, "dotnet-format-attempts.txt");
        var scriptPath = Path.Combine(tempRoot, "dotnet.cmd");
        File.WriteAllText(
            scriptPath,
            string.Join(Environment.NewLine, [
                "@echo off",
                ">>" + QuoteBatchArgument(logPath) + " echo %*",
                alwaysTransientFormatFailure ? "1>&2 echo Unable to locate dotnet CLI. Ensure that it is on the PATH." : string.Empty,
                alwaysTransientFormatFailure ? "exit /b 7" : string.Empty,
                "if not exist " + QuoteBatchArgument(statePath) + " (",
                "  >" + QuoteBatchArgument(statePath) + " echo attempted",
                transientFirstFormatAttempt ? "  1>&2 echo Unable to locate dotnet CLI. Ensure that it is on the PATH." : string.Empty,
                transientFirstFormatAttempt ? "  exit /b 1" : string.Empty,
                ")",
                nonTransientFormatFailure ? "1>&2 echo synthetic formatting failure" : string.Empty,
                nonTransientFormatFailure ? "exit /b 2" : string.Empty,
                "echo format ok",
                "exit /b 0"
            ]));

        return scriptPath;
    }

    private static string QuoteBatchArgument(string value)
        => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
