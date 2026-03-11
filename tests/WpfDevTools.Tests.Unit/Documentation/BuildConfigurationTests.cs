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
    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}

