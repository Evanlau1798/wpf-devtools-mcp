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
        content.Should().Contain("docfx-evidence");
        content.Should().Contain("-DocFxEvidencePath 'artifacts/release/docfx-evidence.json'");
        content.Should().Contain("-PackageSbomPath 'artifacts/release/package-sbom.spdx.json'");
        content.Should().Contain("-WorkflowSha '${{ github.workflow_sha }}'");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldUploadReleaseEvidenceArtifactBeforePublication()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("Write-ReleaseEvidence.ps1");
        content.Should().Contain("release-evidence.json");
        content.Should().Contain("release-evidence-${{ needs.publish-release-assets.outputs.release-tag }}");
        content.Should().Contain("upload-release-assets:");
        content.Should().Contain("Copy-Item -LiteralPath 'artifacts/release/docfx-evidence.json'");
        content.Should().Contain("/docfx-evidence.json");
        content.Should().Contain("-DocFxEvidencePath (Join-Path $stagingRoot 'docfx-evidence.json')");
        content.Should().Contain("/package-sbom.spdx.json");
        content.Should().Contain("-PackageSbomPath (Join-Path $stagingRoot 'package-sbom.spdx.json')");
        content.Should().Contain("-WorkflowSha '${{ github.workflow_sha }}'");
    }
}
