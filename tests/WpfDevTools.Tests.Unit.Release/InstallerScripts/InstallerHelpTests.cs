using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerHelpTests
{
    [Fact]
    public void OnlineInstaller_QuestionMarkAlias_ShouldPrintHelpWithoutResolvingARelease()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var result = ReleaseScriptTestHarness.RunPowerShellCommand($"& '{scriptPath}' -?");

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("Usage:");
        result.Stdout.Should().Contain("-Help");
        result.Stdout.Should().NotContainEquivalentOf("Resolving");
    }
}
