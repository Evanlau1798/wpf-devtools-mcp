using FluentAssertions;
using static WpfDevTools.Tests.Unit.Release.ReleaseScriptTestHarness;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class NativeResourceVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0,1,0,0", "0.1.0.0", "0.1.0")]
    [InlineData("v1.2.3-beta.4+sha", "1,2,3,0", "1.2.3.0", "1.2.3-beta.4+sha")]
    public void ConvertToNativeResourceVersion_ShouldPreservePackageVersionAndEmitNumericResourceVersion(
        string packageVersion,
        string expectedNumeric,
        string expectedFileVersion,
        string expectedProductVersion)
    {
        var command = $"""
            . '{EscapePowerShellPath(GetRepoFilePath("scripts/tools/packaging/Publish-Release.Core.ps1"))}'
            $result = ConvertTo-NativeResourceVersion -Version '{packageVersion}'
            "$($result.Numeric)|$($result.FileVersionString)|$($result.ProductVersionString)"
            """;

        var result = RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(20));

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be($"{expectedNumeric}|{expectedFileVersion}|{expectedProductVersion}");
    }

    private static string EscapePowerShellPath(string path) =>
        path.Replace("'", "''", StringComparison.Ordinal);
}
