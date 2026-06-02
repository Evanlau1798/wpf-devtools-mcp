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
        content.Should().Contain("-EvidencePath \"artifacts/release/release-evidence-${{ matrix.architecture }}.json\"");
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
        content.Should().Contain("Write-ReleaseSecurityEvidence.ps1");
        content.Should().Contain("security-evidence.json");
        content.Should().Contain("Copy-Item -LiteralPath 'artifacts/release/security-evidence.json'");
        content.Should().Contain("/docfx-evidence.json");
        content.Should().Contain("/security-evidence.json");
        content.Should().Contain("-DocFxEvidencePath (Join-Path $stagingRoot 'docfx-evidence.json')");
        content.Should().Contain("-SecurityEvidencePath (Join-Path $stagingRoot 'security-evidence.json')");
        content.Should().Contain("-PublicReleaseStrict");
        content.Should().Contain("/package-sbom.spdx.json");
        content.Should().Contain("-PackageSbomPath (Join-Path $stagingRoot 'package-sbom.spdx.json')");
        content.Should().Contain("-WorkflowSha '${{ github.workflow_sha }}'");
        content.Should().Contain("-EvidencePath (Join-Path $stagingRoot 'release-evidence.json')");
    }

    [Fact]
    public void Workflows_ShouldPassDistinctRuntimeSmokeInstallModes()
    {
        var ciWorkflow = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/ci-cd.yml"));
        var releaseWorkflow = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/release.yml"));

        ciWorkflow.Should().Contain("-SmokeInstallMode 'package-local'");
        ciWorkflow.Should().Contain("-SmokeInstallMode 'online-installer'");
        releaseWorkflow.Should().Contain("-SmokeInstallMode 'package-local'");
        releaseWorkflow.Should().Contain("-SmokeInstallMode 'online-installer'");
    }

    [Fact]
    public void CiWorkflow_ShouldWriteArm64ReleaseEvidenceWhenArm64SmokeIsEnabled()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("release-packaging-smoke-arm64:");
        content.Should().Contain("release-evidence-ci-arm64");
        content.Should().Contain("-EvidenceOutputPath 'artifacts/release/runtime-evidence-arm64-installed.json'");
        content.Should().Contain("-EvidenceOutputPath 'artifacts/release/runtime-evidence-arm64-online.json'");
        content.Should().Contain("-OutputPath 'artifacts/release/release-evidence-arm64.json'");
        content.Should().Contain("-RuntimeEvidencePath 'artifacts/release/runtime-evidence-arm64-installed.json,artifacts/release/runtime-evidence-arm64-online.json'");
        content.Should().Contain("-EvidencePath 'artifacts/release/release-evidence-arm64.json'");
        content.Should().Contain("path: artifacts/release/release-evidence-arm64.json");
    }
}
