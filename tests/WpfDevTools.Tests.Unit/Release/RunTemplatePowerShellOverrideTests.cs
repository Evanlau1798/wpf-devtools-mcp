using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class RunTemplatePowerShellOverrideTests
{
    [Fact]
    public void RunTemplate_ShouldValidatePowerShellOverrideBeforeLaunchingOrElevating()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE cannot contain quote characters.");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must point to a .exe host.");
        content.Should().Contain("where \"%WPFDEVTOOLS_POWERSHELL_EXE%\"");

        var validationIndex = content.IndexOf("WPFDEVTOOLS_POWERSHELL_EXE cannot contain quote characters.", StringComparison.Ordinal);
        var launchIndex = content.IndexOf(":launch_install", StringComparison.Ordinal);
        var elevationIndex = content.IndexOf(":elevate", StringComparison.Ordinal);

        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeLessThan(launchIndex);
        validationIndex.Should().BeLessThan(elevationIndex);
    }
}
