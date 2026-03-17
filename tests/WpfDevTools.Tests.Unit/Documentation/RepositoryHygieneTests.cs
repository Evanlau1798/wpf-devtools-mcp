using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class RepositoryHygieneTests
{
    [Fact]
    public void GitIgnore_ShouldIgnoreTransientTestArtifacts()
    {
        var content = File.ReadAllText(GetRepoFilePath(".gitignore"));

        content.Should().Contain("coverage/");
        content.Should().Contain("TestResults/");
        content.Should().Contain("*.log");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj")]
    [InlineData("tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj")]
    public void TestProjects_ShouldPinSecurityPatchedCompatibilityPackages(string projectPath)
    {
        var content = File.ReadAllText(GetRepoFilePath(projectPath));

        content.Should().Contain("<PackageReference Include=\"System.Net.Http\" Version=\"4.3.4\" />");
        content.Should().Contain("<PackageReference Include=\"System.Text.RegularExpressions\" Version=\"4.3.1\" />");
    }

    [Fact]
    public void UnitTestProject_ShouldUseCommunityLicensedFluentAssertionsVersion()
    {
        var content = File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj"));

        content.Should().Contain("<PackageReference Include=\"FluentAssertions\" Version=\"6.12.0\" />");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
