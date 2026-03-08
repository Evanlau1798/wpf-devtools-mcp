using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReleasePackagingWorkflowTests
{
    [Fact]
    public void CiWorkflow_ShouldSmokeTestReleasePackagingScripts()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Publish-Release.ps1",
            "CI should exercise the release packaging path automatically");
        content.Should().Contain("Install-WpfDevTools.ps1",
            "CI should smoke-test installation from a published package");
        content.Should().Contain("Uninstall-WpfDevTools.ps1",
            "CI should verify uninstall/cleanup as part of packaging validation");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}