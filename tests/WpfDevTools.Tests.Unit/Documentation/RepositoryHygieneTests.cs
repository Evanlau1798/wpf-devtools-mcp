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

    [Fact]
    public void GitIgnoredMarkdownFiles_ShouldNotBeTracked()
    {
        var trackedIgnoredMarkdown = RunGitLines(
            "ls-files",
            "-ci",
            "--exclude-standard",
            "*.md");

        trackedIgnoredMarkdown.Should().BeEmpty(
            "non-DocFX local documentation markdown that is ignored by .gitignore must not stay tracked");
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
    public void TestProjects_ShouldUseCentralPackageManagementWithoutObsoleteBclPins(string projectPath)
    {
        ReadRepoFile(projectPath)
            .Should()
            .NotContain("Version=", "test projects should use central package management");

        var centralPackages = ReadRepoFile("Directory.Packages.props");

        centralPackages.Should().NotContain("<PackageVersion Include=\"System.Net.Http\"",
            "net8.0 provides System.Net.Http and must not restore obsolete compatibility packages directly");
        centralPackages.Should().NotContain("<PackageVersion Include=\"System.Text.RegularExpressions\"",
            "net8.0 provides System.Text.RegularExpressions and must not restore obsolete compatibility packages directly");
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

    [Fact]
    public void SessionManagerCleanupTimerRearm_ShouldDocumentDisposeRace()
    {
        var content = ReadRepoFile("src/WpfDevTools.Mcp.Server/SessionManager.Cleanup.cs");

        content.Should().Contain("Dispose() may set _disposeState between cleanup work and timer re-arm",
            "the cleanup timer re-arm guard is subtle concurrency behavior and must stay documented");
    }

    [Fact]
    public void SessionManagerConnectPaths_ShouldShareAttachAfterConnectFlow()
    {
        var content = ReadRepoFile("src/WpfDevTools.Mcp.Server/SessionManager.cs");

        content.Should().Contain("ConnectAndAttachSessionAsync(",
            "injected and existing-host connection paths should share the same attach-after-connect ownership handoff");
        CountOccurrences(content, "AttachSession(processId, detachedPipeClient);").Should().Be(1,
            "the connected detached client should be attached in exactly one shared helper");
        CountOccurrences(content, "SetActiveProcess(processId);").Should().Be(1,
            "active-process selection should be part of the same shared attach-after-connect flow");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = GetRepoFilePath(relativePath);

        File.Exists(path).Should().BeTrue($"{relativePath} should exist");

        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static bool GitIgnoreMatches(string relativePath)
    {
        var result = RunGit(
            "check-ignore",
            "--quiet",
            relativePath);

        return result.ExitCode == 0;
    }

    private static string[] RunGitLines(params string[] arguments)
    {
        var result = RunGit(arguments);
        result.ExitCode.Should().Be(0, result.Error);

        return result.Output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static GitCommandResult RunGit(params string[] arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "git";
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.WorkingDirectory = GetRepoRoot();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitCommandResult(process.ExitCode, output, error);
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

    private sealed record GitCommandResult(int ExitCode, string Output, string Error);
}
