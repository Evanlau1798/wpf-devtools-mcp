using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BuildConfigurationTests
{
    [Fact]
    public void DirectoryBuildProps_ShouldNotGloballyDisableParallelBuilds()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().NotContain("<BuildInParallel>false</BuildInParallel>");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldNotGloballyDisableParallelRestoreGraph()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().NotContain("<RestoreBuildInParallel>false</RestoreBuildInParallel>");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldKeepNuGetAuditConnectivityWarnings_NonFatal()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().Contain("WarningsNotAsErrors");
        content.Should().Contain("NU1900");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldDisableNuGetAuditOutsideCi_ToAvoidLocalConnectivityWarnings()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().Contain("<NuGetAudit");
        content.Should().Contain("Condition=");
        content.Should().Contain("GITHUB_ACTIONS");
        content.Should().Contain("TF_BUILD");
    }

    [Fact]
    public void RepoResponseFiles_ShouldNotForceSingleNodeBuilds()
    {
        var msbuildRsp = ReadResponseFileOptions("msbuild.rsp");
        var directoryBuildRsp = ReadResponseFileOptions("Directory.Build.rsp");

        AssertResponseFileKeepsNodeReuseSuppressionWithoutSingleNodeThrottle(msbuildRsp);
        AssertResponseFileKeepsNodeReuseSuppressionWithoutSingleNodeThrottle(directoryBuildRsp);
    }

    [Fact]
    public void ContributingGuide_ShouldRecommendBuildThenNoBuildTestWorkflow()
    {
        var content = File.ReadAllText(GetRepoFilePath("CONTRIBUTING.md"));

        content.Should().Contain("dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj",
            "contributors should build the target test project before running tests to avoid file-lock issues");
        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj",
            "contributors should call dotnet test on the explicit unit test project path");
        content.Should().Contain("--no-build",
            "contributors should rerun unit tests without rebuilding once the target project has been built");
        content.Should().NotContain("dotnet watch test --project tests/WpfDevTools.Tests.Unit/",
            "the contributing guide should not present watch mode as the primary TDD path when the repository requires build/test separation");
    }

    [Fact]
    public void CoverageWorkflow_ShouldBuildUnitTestsBeforeRunningNoBuildCoverageStep()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-restore",
            "coverage CI should compile the unit test project before invoking dotnet test to avoid implicit rebuilds");
        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-build --settings coverlet.runsettings",
            "coverage CI should use a no-build test invocation after the explicit build step");
    }

    [Fact]
    public void BuildAndTestWorkflow_ShouldRunExplicitNoBuildTestProjectLanes()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("name: Run unit tests");
        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --configuration ${{ matrix.configuration }} --no-build",
            "CI should shard unit tests into an explicit no-build project invocation instead of a broad solution test run");
        content.Should().Contain("name: Run integration tests");
        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --configuration ${{ matrix.configuration }} --no-build",
            "CI should shard integration tests into an explicit no-build project invocation instead of a broad solution test run");
        content.Should().NotContain("dotnet test --configuration ${{ matrix.configuration }} --no-build --verbosity normal -p:Platform=${{ matrix.platform }}",
            "the broad solution-level test command should be replaced by project-specific lanes");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string[] ReadResponseFileOptions(string relativePath)
    {
        return File.ReadAllLines(GetRepoFilePath(relativePath))
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static void AssertResponseFileKeepsNodeReuseSuppressionWithoutSingleNodeThrottle(string[] options)
    {
        options.Should().Contain("-nodeReuse:false");
        options.Any(IsSingleNodeThrottleOption).Should().BeFalse();
    }

    private static bool IsSingleNodeThrottleOption(string option)
    {
        var normalized = option.TrimStart('-', '/');
        return string.Equals(normalized, "maxCpuCount:1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "m:1", StringComparison.OrdinalIgnoreCase);
    }
}

