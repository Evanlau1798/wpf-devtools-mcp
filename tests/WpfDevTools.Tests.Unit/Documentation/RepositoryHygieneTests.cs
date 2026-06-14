using System.Text.RegularExpressions;
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
            "*.md")
            .ToArray();

        trackedIgnoredMarkdown.Should().BeEmpty(
            "local documentation markdown that is ignored by .gitignore, including AGENTS.md, must not stay tracked");
    }

    [Fact]
    public void TrackedMarkdownFiles_ShouldStayInsideDocfxOrApprovedRepositoryContracts()
    {
        var approvedRootMarkdown = new HashSet<string>(StringComparer.Ordinal)
        {
            "AGENT_INSTALL.md",
            "CODE_SIGNING.md",
            "CONTRIBUTING.md",
            "EXAMPLES.md",
            "README.md",
            "RELEASING.md",
            "SECURITY.md",
            "THIRD_PARTY_NOTICES.md",
            "TRADEMARK.md"
        };
        var approvedNonDocfxMarkdown = new HashSet<string>(approvedRootMarkdown, StringComparer.Ordinal)
        {
            "examples/layout-process-and-agent-tips.md",
            "examples/scene-inspection.md",
            "examples/state-and-interaction.md",
            "src/WpfDevTools.Inspector.Sdk/README.md"
        };

        var forbiddenTrackedMarkdown = RunGitLines("ls-files", "*.md")
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !path.StartsWith("docfx/", StringComparison.Ordinal))
            .Where(path => !approvedNonDocfxMarkdown.Contains(path))
            .ToArray();

        forbiddenTrackedMarkdown.Should().BeEmpty(
            "development plans, checklists, docs/ notes, and ad hoc markdown must not be tracked; product documentation markdown belongs under docfx/ or an explicit root contract");
    }

    [Fact]
    public void GitIgnore_ShouldExplainPrivateAgentsGuide()
    {
        var content = File.ReadAllText(GetRepoFilePath(".gitignore"));

        content.Should().Contain("AGENTS.md is a private local workflow file and must remain untracked",
            ".gitignore should explicitly document why AGENTS.md is local-only");
        content.Should().NotContain("AGENTS.md remains a tracked repository contract",
            "AGENTS.md is not a public repository contract");
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
    public void LineLimitExceptions_ShouldOnlyDocumentCurrentOversizedFiles()
    {
        var oversizedFiles = EnumeratePolicyFiles()
            .Where(path => File.ReadLines(GetRepoFilePath(path)).Count() > SourceFileLineLimit)
            .ToHashSet(StringComparer.Ordinal);

        var staleExceptions = ReadLineLimitExceptions()
            .Where(path => !oversizedFiles.Contains(path))
            .ToArray();

        staleExceptions.Should().BeEmpty(
            "the line-limit exception manifest should shrink as files are split below 500 lines");
    }

    [Fact]
    public void LineLimitExceptions_ShouldNotPermitCSharpSourceOrTestFiles()
    {
        var csharpExceptions = ReadLineLimitExceptions()
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        csharpExceptions.Should().BeEmpty(
            "production and test C# files must stay under 500 lines instead of entering the exception manifest");
    }

    [Fact]
    public void LineLimitPolicy_ShouldCoverTestAppXamlFiles()
    {
        EnumeratePolicyFiles()
            .Should()
            .Contain("tests/WpfDevTools.Tests.TestApp/MainWindow.xaml",
                "large WPF test surfaces need the same 500-line governance as source and script files");
    }

    [Fact]
    public void LineLimitPolicy_ShouldCoverGitHubWorkflowFiles()
    {
        EnumeratePolicyFiles()
            .Should()
            .Contain(".github/workflows/release.yml",
                "GitHub workflow files are production release code and need explicit line-limit governance");
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
    public void IntegrationTests_ShouldNotDeclareStaticSkippedFacts()
    {
        var violations = EnumeratePolicyFiles()
            .Where(path => path.StartsWith("tests/WpfDevTools.Tests.Integration/", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(GetRepoFilePath(path))
                .Select((line, index) => new
                {
                    Path = path,
                    Line = index + 1,
                    Text = line.Trim()
                }))
            .Where(line => line.Text.Contains("[Fact(Skip", StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}")
            .ToArray();

        violations.Should().BeEmpty(
            "Release-specific integration exclusions should use conditional compilation instead of producing skipped test results");
    }

    [Fact]
    public void Tests_ShouldNotThrowRuntimeSkipExceptions()
    {
        const string forbiddenSkipCall = "throw SkipException." + "ForSkip";
        var violations = EnumeratePolicyFiles()
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(GetRepoFilePath(path))
                .Select((line, index) => new
                {
                    Path = path,
                    Line = index + 1,
                    Text = line.Trim()
                }))
            .Where(line => line.Text.Contains(forbiddenSkipCall, StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}")
            .ToArray();

        violations.Should().BeEmpty(
            "production verification should fail visibly when required runner capabilities or build artifacts are missing");
    }

    [Fact]
    public void EnvironmentMutatingTests_ShouldUseApprovedNonParallelCollection()
    {
        const string envMutationCall = "Environment." + "SetEnvironmentVariable(";
        var approvedCollections = new[]
        {
            "ProcessEnvironment",
            "SecurityState",
            "ToolCallHelperState"
        };
        var testFiles = EnumeratePolicyFiles()
            .Where(path => path.StartsWith("tests/WpfDevTools.Tests.Unit", StringComparison.Ordinal)
                           && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Content = ReadRepoFile(path) })
            .ToArray();

        var violations = testFiles
            .Where(file => file.Content.Contains(envMutationCall, StringComparison.Ordinal))
            .Where(file => file.Content.Contains("[Fact", StringComparison.Ordinal)
                           || file.Content.Contains("[Theory", StringComparison.Ordinal))
            .Where(file => !HasApprovedCollection(file.Content, testFiles.Select(item => item.Content), approvedCollections))
            .Select(file => file.Path)
            .ToArray();

        violations.Should().BeEmpty(
            "tests that mutate process-wide environment variables must run in an approved non-parallel collection");
    }

    private static bool HasApprovedCollection(string content, IEnumerable<string> allTestFileContent, string[] approvedCollections)
    {
        if (ContainsApprovedCollection(content, approvedCollections))
        {
            return true;
        }

        var classNames = Regex.Matches(
            content,
            @"(?m)^(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        return allTestFileContent.Any(candidate =>
            ContainsApprovedCollection(candidate, approvedCollections)
            && classNames.Any(name => candidate.Contains($"class {name}", StringComparison.Ordinal)));
    }

    private static bool ContainsApprovedCollection(string content, string[] approvedCollections) =>
        approvedCollections.Any(collection =>
            content.Contains($"[Collection(\"{collection}\")]", StringComparison.Ordinal));

    [Fact]
    public void VisualTreeAnalyzerTests_ShouldNotDocumentStaleDefaultDepth()
    {
        var content = ReadRepoFile("tests/WpfDevTools.Tests.Unit/Inspector/Analyzers/VisualTreeAnalyzerGapTests.cs");

        content.Should().NotContain("ShouldUse50",
            "visual tree tests should name the documented default depth contract");
        content.Should().NotContain("defaults to 50",
            "tree tools default to depth=10 when depth is omitted");
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
        var content = ReadRepoFiles("src/WpfDevTools.Mcp.Server", "SessionManager*.cs");

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

    private static string ReadRepoFiles(string relativeDirectory, string searchPattern)
    {
        var directory = GetRepoFilePath(relativeDirectory);
        Directory.Exists(directory).Should().BeTrue($"{relativeDirectory} should exist");

        var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        files.Should().NotBeEmpty($"{searchPattern} should match repository source files");

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
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
        var roots = new[] { "src", "tests", "scripts", ".github/workflows" };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".ps1",
            ".xaml",
            ".yml",
            ".yaml"
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
