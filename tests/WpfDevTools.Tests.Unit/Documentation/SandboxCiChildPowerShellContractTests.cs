using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiChildPowerShellContractTests
{
    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/Documentation/SandboxCiProcessCleanupContractTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit/Documentation/SandboxCiProcessCleanupBehaviorRegressionTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit/Documentation/SandboxCiProcessSnapshotIdentityRegressionTests.cs")]
    public void SandboxProcessCleanupTests_ShouldStartNestedPowerShellHidden(string relativePath)
    {
        var path = TestRepositoryPaths.GetRepoFilePath(relativePath);
        var rawNestedPowerShellStarts = File.ReadLines(path)
            .Select((Line, Index) => new { Line, LineNumber = Index + 1 })
            .Where(entry => entry.Line.Contains("Start-Process powershell.exe -ArgumentList", StringComparison.Ordinal))
            .ToArray();

        rawNestedPowerShellStarts.Should().NotBeEmpty();
        rawNestedPowerShellStarts.Should().OnlyContain(
            entry => entry.Line.Contains("-WindowStyle Hidden", StringComparison.Ordinal),
            "nested PowerShell sleeper fixtures must not create visible windows in Windows Sandbox");
    }
}
