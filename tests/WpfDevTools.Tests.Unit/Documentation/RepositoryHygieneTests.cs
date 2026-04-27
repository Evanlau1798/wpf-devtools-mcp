using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class RepositoryHygieneTests
{
    private const int SourceFileLineLimit = 500;
    private const string LineLimitExceptionsPath =
        "tests/WpfDevTools.Tests.Unit/Documentation/LineLimitExceptions.txt";

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
    [InlineData("tests/WpfDevTools.Tests.Unit/Release/NewReleaseRegressionTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit/Release/NewInstallerGuardTests.cs")]
    public void GitIgnore_ShouldNotIgnoreReleaseTestSources(string relativePath)
    {
        var ignored = GitIgnoreMatches(relativePath);

        ignored.Should().BeFalse($"{relativePath} is a source test file and must remain commit-visible");
    }

    [Fact]
    public void SourceFiles_ShouldStayUnderLineLimitUnlessExplicitlyExcepted()
    {
        var exceptions = ReadLineLimitExceptions();
        var oversizedFiles = EnumeratePolicyFiles()
            .Select(path => new
            {
                Path = path,
                Lines = File.ReadLines(GetRepoFilePath(path)).Count()
            })
            .Where(file => file.Lines > SourceFileLineLimit)
            .Where(file => !exceptions.Contains(file.Path))
            .Select(file => $"{file.Path} has {file.Lines} lines")
            .ToArray();

        oversizedFiles.Should().BeEmpty(
            "new source, script, and test files must stay under 500 lines unless the exception manifest explicitly documents the debt");
    }

    [Fact]
    public void LineLimitPolicy_ShouldCoverTestAppXamlFiles()
    {
        EnumeratePolicyFiles()
            .Should()
            .Contain("tests/WpfDevTools.Tests.TestApp/MainWindow.xaml",
                "large WPF test surfaces need the same 500-line governance as source and script files");
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

    [Fact]
    public void UnitTests_ShouldNotUseTrivialTrueAssertionsAsBehaviorAssertions()
    {
        const string trivialTrueAssertion = "Assert.True(" + "true";
        var violations = EnumeratePolicyFiles()
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(GetRepoFilePath(path))
                .Select((line, index) => new
                {
                    Path = path,
                    Line = index + 1,
                    Text = line.Trim()
                }))
            .Where(line => line.Text.Contains(trivialTrueAssertion, StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}")
            .ToArray();

        violations.Should().BeEmpty(
            "tests should assert deterministic behavior instead of recording that a code path merely completed");
    }

    [Fact]
    public void SessionManagerCleanupTimer_ShouldRemainWeakRootSafeForTestCreatedInstances()
    {
        var content = ReadRepoFile("src/WpfDevTools.Mcp.Server/SessionManager.cs");

        content.Should().Contain("WeakReference<SessionManager>",
            "test-created SessionManager instances may intentionally rely on weak-root cleanup safety when a test does not dispose them");
        content.Should().Contain("callback: static state => CleanupTimerState.Invoke(state)",
            "the cleanup timer callback must not capture the SessionManager instance strongly");
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

    private static HashSet<string> ReadLineLimitExceptions()
    {
        var path = GetRepoFilePath(LineLimitExceptionsPath);
        File.Exists(path).Should().BeTrue(
            "the 500-line policy needs an explicit exception manifest for existing debt");

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split('|', 2))
            .Select(parts =>
            {
                parts.Should().HaveCount(2, "each line-limit exception needs a path and reason");
                parts[0].Trim().Should().NotBeNullOrWhiteSpace("exception paths must be explicit");
                parts[1].Trim().Should().NotBeNullOrWhiteSpace("each exception must document why the debt remains");
                return parts[0].Trim().Replace('\\', '/');
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumeratePolicyFiles()
    {
        var repoRoot = GetRepoRoot();
        var roots = new[] { "src", "tests", "scripts" };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".ps1",
            ".xaml"
        };

        return roots
            .Select(root => Path.Combine(repoRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal)
                           && !path.Contains("/obj/", StringComparison.Ordinal));
    }
}
