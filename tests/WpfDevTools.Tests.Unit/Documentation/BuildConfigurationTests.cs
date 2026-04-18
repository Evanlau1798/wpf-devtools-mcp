using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BuildConfigurationTests
{
    [Fact]
    public void DirectoryBuildProps_ShouldDisableParallelBuilds_ForDeterministicProjectReferenceResolution()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().Contain("<BuildInParallel>false</BuildInParallel>");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldDisableParallelRestoreGraph_ForDeterministicRestore()
    {
        var content = File.ReadAllText(GetRepoFilePath("Directory.Build.props"));

        content.Should().Contain("<RestoreBuildInParallel>false</RestoreBuildInParallel>");
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
    public void MsBuildResponseFile_ShouldForceSingleNodeBuilds_ForDeterministicSolutionBuilds()
    {
        var content = File.ReadAllText(GetRepoFilePath("msbuild.rsp"));

        content.Should().Contain("-maxCpuCount:1");
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

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}

