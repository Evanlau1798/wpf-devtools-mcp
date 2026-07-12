using FluentAssertions;
using static WpfDevTools.Tests.Unit.Release.ReleaseScriptTestHarness;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class NativeResourceVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0,1,0,0", "0.1.0.0", "0.1.0")]
    [InlineData("v1.2.3-beta.4+sha", "1,2,3,0", "1.2.3.0", "1.2.3.0")]
    public void ConvertToNativeResourceVersion_ShouldEmitRcSafeNumericVersionStrings(
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

    [Fact]
    public void ConvertToMsBuildPropertyValue_ShouldEscapeNativeResourceVersionCommas()
    {
        var command = $"""
            . '{EscapePowerShellPath(GetRepoFilePath("scripts/tools/packaging/Publish-Release.Core.ps1"))}'
            ConvertTo-MSBuildPropertyValue -Value '0,1,0,0'
            """;

        var result = RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(20));

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("0%2c1%2c0%2c0");
    }

    [Fact]
    public void BootstrapperDefaultResourceVersions_ShouldStayRcSafeForBetaRelease()
    {
        var resource = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Bootstrapper/bootstrapper.rc"));
        var project = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        resource.Should().Contain("WPFDEVTOOLS_BOOTSTRAPPER_PRODUCT_VERSION_STRING \"1.0.0.0\"");
        project.Should().Contain("<BootstrapperProductVersionString Condition=\"'$(BootstrapperProductVersionString)' == ''\">1.0.0.0</BootstrapperProductVersionString>");
        resource.Should().NotContain("1.0.0-beta.1");
        resource.Should().NotContain("1.0.0-beta.2");
        project.Should().NotContain("1.0.0-beta.1</BootstrapperProductVersionString>");
        project.Should().NotContain("1.0.0-beta.2</BootstrapperProductVersionString>");
    }

    private static string EscapePowerShellPath(string path) =>
        path.Replace("'", "''", StringComparison.Ordinal);
}
