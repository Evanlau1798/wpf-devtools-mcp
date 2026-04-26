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
        ReadRepoFile(projectPath)
            .Should()
            .NotContain("Version=", "test projects should use central package management");

        var centralPackages = ReadRepoFile("Directory.Packages.props");

        centralPackages.Should().Contain("<PackageVersion Include=\"System.Net.Http\" Version=\"4.3.4\" />");
        centralPackages.Should().Contain("<PackageVersion Include=\"System.Text.RegularExpressions\" Version=\"4.3.1\" />");
    }

    [Fact]
    public void UnitTestProject_ShouldUseCommunityLicensedFluentAssertionsVersion()
    {
        ReadRepoFile("tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj")
            .Should()
            .NotContain("Version=", "test projects should use central package management");

        ReadRepoFile("Directory.Packages.props")
            .Should()
            .Contain("<PackageVersion Include=\"FluentAssertions\" Version=\"6.12.0\" />");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = GetRepoFilePath(relativePath);

        File.Exists(path).Should().BeTrue($"{relativePath} should exist");

        return File.ReadAllText(path);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
