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
    [InlineData("docs/review-note.md")]
    [InlineData("tmp/probe.txt")]
    [InlineData("release/release_0.0.0_win-x64.zip")]
    [InlineData("docfx/_site/index.html")]
    [InlineData("docfx/api/WpfDevTools.yml")]
    [InlineData("docfx/obj/cache.json")]
    [InlineData("docfx/.xrefmap")]
    [InlineData("docfx/xrefmap.yml")]
    public void GitIgnore_ShouldIgnoreLocalOnlyRepositoryArtifacts(string relativePath)
    {
        var ignored = GitIgnoreMatches(relativePath);

        ignored.Should().BeTrue($"{relativePath} should not be accidentally committed");
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

    private static bool GitIgnoreMatches(string relativePath)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.ArgumentList.Add("check-ignore");
        process.StartInfo.ArgumentList.Add("--quiet");
        process.StartInfo.ArgumentList.Add(relativePath);
        process.StartInfo.WorkingDirectory = GetRepoRoot();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static string GetRepoRoot()
        => Path.GetDirectoryName(GetRepoFilePath(".gitignore"))
           ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
}
