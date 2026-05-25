using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubActionsWorkflowSecurityTests
{
    private static readonly Regex UsesActionRegex = new(
        @"^\s*uses:\s+(?<action>[^@\s#]+)@(?<ref>[^\s#]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex GitShaRegex = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void GitHubActions_ShouldPinReusableActionsToImmutableShas()
    {
        var workflowDirectory = TestRepositoryPaths.GetRepoFilePath(".github/workflows");

        var unpinnedActions = Directory
            .EnumerateFiles(workflowDirectory, "*.yml", SearchOption.TopDirectoryOnly)
            .SelectMany(EnumerateUnpinnedActionReferences)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        unpinnedActions.Should().BeEmpty(
            "workflow dependencies should be pinned to immutable commit SHAs rather than mutable tags");
    }

    [Fact]
    public void ReleaseCriticalWindowsJobs_ShouldPinRunnerImage()
    {
        var workflowDirectory = TestRepositoryPaths.GetRepoFilePath(".github/workflows");
        var releaseCriticalWorkflows = new[]
        {
            Path.Combine(workflowDirectory, "ci-cd.yml"),
            Path.Combine(workflowDirectory, "release.yml")
        };

        var mutableRunnerReferences = releaseCriticalWorkflows
            .SelectMany(path => File.ReadLines(path)
                .Select((text, index) => new { Path = path, Text = text, Number = index + 1 }))
            .Where(line => line.Text.Contains("runs-on: windows-latest", StringComparison.Ordinal))
            .Select(line => $"{Path.GetRelativePath(TestRepositoryPaths.GetRepoFilePath("."), line.Path).Replace('\\', '/')}:{line.Number}")
            .ToArray();

        mutableRunnerReferences.Should().BeEmpty(
            "release-critical validation should pin the Windows runner image instead of relying on the mutable windows-latest alias");
    }

    [Fact]
    public void CiCdWorkflow_ShouldSupportManualDispatchForMirrorCiRetry()
    {
        var workflow = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/ci-cd.yml"));

        workflow.Should().Contain("workflow_dispatch:",
            "mirror CI repositories should support one-off CI retries without creating extra commits solely to trigger Actions");
    }

    [Fact]
    public void SecurityScanWorkflow_ShouldRunStaticSecurityScanningBeyondNuGetAudit()
    {
        var workflow = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/security-scan.yml"));

        workflow.Should().Contain("security-scan:");
        workflow.Should().Contain("dotnet format WpfDevTools.sln analyzers --verify-no-changes",
            "CI should run a .NET analyzer gate, not only restore-time NuGet vulnerability checks");
        workflow.Should().Contain("PSScriptAnalyzer",
            "PowerShell installer and release scripts should be statically scanned in CI");
        workflow.Should().Contain("Invoke-ScriptAnalyzer");
        workflow.Should().Contain("-RequiredVersion",
            "the analyzer module dependency should be pinned so the security gate is reproducible");
        workflow.Should().NotContain("-SkipPublisherCheck",
            "the security gate should not disable publisher validation for the analyzer module");
        workflow.Should().Contain("-Severity Error",
            "the security scan should fail closed on high-confidence analyzer errors");
        workflow.Should().Contain("Run repository secret pattern scan");
        workflow.Should().Contain("Run native bootstrapper security analysis");
        workflow.Should().Contain("/p:RunCodeAnalysis=true");
        workflow.Should().Contain("/p:TreatWarningsAsErrors=true");
    }

    [Fact]
    public void CiCdWorkflow_ShouldKeepSecurityScanInDedicatedWorkflow()
    {
        var workflow = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/ci-cd.yml"));

        workflow.Should().NotContain("security-scan:",
            "the CI/CD workflow is already large; the security scan must stay in a dedicated workflow file");
    }

    private static IEnumerable<string> EnumerateUnpinnedActionReferences(string workflowPath)
    {
        var relativePath = Path.GetRelativePath(
            TestRepositoryPaths.GetRepoFilePath("."),
            workflowPath).Replace('\\', '/');
        var lines = File.ReadLines(workflowPath).Select((text, index) => new { Text = text, Number = index + 1 });

        foreach (var line in lines)
        {
            var match = UsesActionRegex.Match(line.Text);
            if (!match.Success)
            {
                continue;
            }

            var actionRef = match.Groups["ref"].Value;
            if (!GitShaRegex.IsMatch(actionRef))
            {
                yield return $"{relativePath}:{line.Number}: {line.Text.Trim()}";
            }
        }
    }
}
