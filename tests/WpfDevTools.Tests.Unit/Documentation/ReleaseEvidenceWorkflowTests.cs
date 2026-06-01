using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReleaseEvidenceWorkflowTests
{
    [Fact]
    public void CiWorkflow_ShouldUploadReleaseEvidenceArtifact()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Write-ReleaseEvidence.ps1");
        content.Should().Contain("release-evidence-${{ matrix.architecture }}.json");
        content.Should().Contain("release-evidence-ci-${{ matrix.architecture }}");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldUploadReleaseEvidenceArtifactBeforePublication()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("Write-ReleaseEvidence.ps1");
        content.Should().Contain("release-evidence.json");
        content.Should().Contain("release-evidence-${{ needs.publish-release-assets.outputs.release-tag }}");
        content.Should().Contain("upload-release-assets:");
    }
}
