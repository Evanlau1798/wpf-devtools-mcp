using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubReleaseWorkflowTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void ReleaseWorkflow_ShouldBuildAllSupportedArchitectures()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("@('x64', 'x86', 'arm64')",
            "GitHub Release packaging should build x64, x86, and arm64 assets from the same workflow");
        content.Should().Contain("Publish-Release.ps1",
            "the release workflow should call the production packaging script instead of duplicating packaging logic inline");
        content.Should().Contain("-ExpectedReleaseTag $env:RELEASE_TAG",
            "release packaging should fail closed if the checked-out tag and the packaged project version drift apart without interpolating the tag directly into PowerShell");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldExportChecksumsAndReleaseManifest()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("Export-GitHubReleaseAssets.ps1",
            "the release workflow should stage GitHub Release assets through the export script");
        content.Should().Contain("SHA256SUMS.txt",
            "release uploads should include a checksum manifest for operators");
        content.Should().Contain("release-assets.json",
            "release uploads should include machine-readable asset metadata");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldUploadAssetsToGitHubRelease()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("release:",
            "the workflow should integrate with published GitHub Releases");
        content.Should().Contain("workflow_dispatch:",
            "operators should be able to rerun release packaging manually for a specific tag");
        content.Should().Contain("upload-gh-release.ps1",
            "CI should execute the generated upload helper to publish staged assets");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldRequireArm64RuntimeValidationBeforeUpload()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE",
            "public release publication should fail closed until a dedicated ARM64 runtime validation lane is configured");
        content.Should().Contain("validate-arm64-release-assets",
            "release publication should have a dedicated ARM64 validation job that runs before upload");
        content.Should().Contain("[self-hosted, Windows, ARM64]",
            "ARM64 asset validation must occur on an actual ARM64 runner so the packaged executable can launch");
        content.Should().Contain("Invoke-PackagedRuntimeLiveSmoke.ps1",
            "the ARM64 release validation lane should start the packaged server runtime against a live WPF target, not just install and uninstall scripts");
        content.Should().Contain("-TrustedReleaseMetadataDirectory $stagingRoot",
            "the pre-upload ARM64 online-installer smoke must consume the staged release sidecars explicitly instead of falling back to GitHub metadata that does not exist yet");
        content.Should().Contain("upload-release-assets:",
            "asset upload should happen in a separate job after validation finishes");
        content.Should().Contain("needs:",
            "the upload job must depend on the package build and ARM64 runtime validation jobs");
        content.Should().Contain("- validate-arm64-release-assets",
            "the ARM64 runtime validation job should be part of the release workflow DAG before upload");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldValidateX64StagedAssetsWithTargetAwareLiveSmokeBeforeUpload()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("validate-x64-release-assets",
            "signed x64 Release assets must be installed and live-injection tested before upload");
        content.Should().Contain("Invoke-PackagedRuntimeLiveSmoke.ps1",
            "signed release validation must launch TestApp and pass the exact target process to the packaged runtime smoke helper");
        content.Should().Contain("- validate-x64-release-assets",
            "asset upload should wait for signed x64 live-injection validation");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldValidateX86StagedAssetsWithTargetAwareLiveSmokeBeforeUpload()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("validate-x86-release-assets",
            "public x86 Release assets are built and uploaded, so they must be installed and live-injection tested before upload");
        content.Should().Contain("release_*_win-x86.zip",
            "the x86 validation lane must resolve the staged x86 archive instead of accidentally retesting another architecture");
        content.Should().Contain("-Architecture 'x86'",
            "the x86 validation lane must exercise the package through the x86 install and runtime smoke paths");
        content.Should().Contain("- validate-x86-release-assets",
            "asset upload should wait for signed x86 live-injection validation");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldVerifyFullUninstallResidueForStagedAssetsBeforeUpload()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("Full uninstall staged installed x64 package residue test");
        content.Should().Contain("Full uninstall staged x64 online installer residue test");
        content.Should().Contain("Full uninstall staged installed ARM64 package residue test");
        content.Should().Contain("Full uninstall staged ARM64 online installer residue test");
        content.Should().Contain("Test-InstallResidue.ps1",
            "signed staged packages must prove installer-owned files and generated registrations are removed before upload");
        content.Should().Contain("-Action full-uninstall",
            "release validation should use the supported full-uninstall action instead of ad hoc cleanup");
        content.Should().Contain("-InstallRoot './tmp-release-install-smoke'");
        content.Should().Contain("-InstallRoot './tmp-release-bootstrap-smoke'");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldProvideExecutableSigningInputsToPackaging()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64",
            "the hosted release workflow needs a secret-backed certificate payload instead of assuming a local file path exists on the runner");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH",
            "Publish-Release.ps1 needs a materialized certificate path or an already-installed certificate thumbprint to sign release payloads");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            "release packaging enforces signer pinning, so the workflow must pass the expected signer thumbprint into the packaging step");
        content.Should().Contain("WPFDEVTOOLS_PFX_PASSWORD",
            "PFX-backed signing in GitHub Actions must inject the certificate password through an environment variable instead of interactive prompts");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldPassSignerTrustToAssetStagingStep()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));
        var step = GetNamedStepBlock(lines, "Stage GitHub Release assets");

        step.Should().Contain(line =>
            string.Equals(
                line.Trim(),
                "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT: ${{ secrets.WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT }}",
                StringComparison.Ordinal),
            "release sidecar generation validates signed archive metadata and must receive the same pinned signer trust as packaging");
        step.Should().Contain(line =>
            string.Equals(
                line.Trim(),
                "-TrustedSignerThumbprint $env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT `",
                StringComparison.Ordinal),
            "the staging script should pass signer trust explicitly instead of relying on test-mode metadata trust");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldLimitContentsWritePermissionToUploadJob()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));

        var topLevelPermissions = GetTopLevelBlock(lines, "permissions");
        topLevelPermissions.Should().Contain("  contents: read",
            "the workflow-level token should default to read-only repository contents access");
        topLevelPermissions.Should().NotContain("  contents: write",
            "write access should not be granted to every release job");

        var contentsWriteJobs = lines
            .Select((line, index) => new { Line = line.Trim(), Index = index })
            .Where(item => string.Equals(item.Line, "contents: write", StringComparison.Ordinal))
            .Select(item => GetEnclosingJobName(lines, item.Index))
            .ToArray();

        contentsWriteJobs.Should().Equal(["upload-release-assets"],
            "only the job that creates or updates GitHub Release assets should receive contents: write");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldValidateReleaseTagsBeforeShellUse()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/release.yml"));

        content.Should().Contain("RELEASE_INPUT_TAG: ${{ inputs.tag }}");
        content.Should().Contain("RELEASE_EVENT_TAG: ${{ github.event.release.tag_name }}");
        content.Should().Contain("-notmatch '^v\\d+\\.\\d+\\.\\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'",
            "release tags should be constrained before reaching PowerShell, git, gh, or staging paths");
        content.Should().Contain("git rev-parse --verify --end-of-options",
            "tag checkout should verify the exact ref before switching revisions");
        content.Should().NotContain("$tag = '${{ inputs.tag }}'",
            "workflow inputs should flow through environment variables instead of direct script interpolation");
        content.Should().NotContain("$tag = '${{ steps.release-metadata.outputs.tag }}'",
            "validated release tags should flow through environment variables before shell use");
        content.Should().NotContain("$tag = '${{ needs.publish-release-assets.outputs.release-tag }}'",
            "upload jobs should not interpolate release tags directly into PowerShell scripts");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string[] GetTopLevelBlock(string[] lines, string header)
    {
        var headerIndex = Array.FindIndex(lines, line => string.Equals(line, $"{header}:", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(headerIndex + 1)
            .TakeWhile(line => string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]))
            .ToArray();
    }

    private static string[] GetNamedStepBlock(string[] lines, string stepName)
    {
        var stepHeader = $"      - name: {stepName}";
        var stepIndex = Array.FindIndex(lines, line => string.Equals(line, stepHeader, StringComparison.Ordinal));
        if (stepIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(stepIndex)
            .TakeWhile((line, index) => index == 0 || !line.StartsWith("      - name:", StringComparison.Ordinal))
            .ToArray();
    }

    private static string? GetEnclosingJobName(string[] lines, int lineIndex)
    {
        var jobsIndex = Array.FindIndex(lines, line => string.Equals(line, "jobs:", StringComparison.Ordinal));
        if (jobsIndex < 0 || lineIndex <= jobsIndex)
        {
            return null;
        }

        for (var index = lineIndex - 1; index > jobsIndex; index--)
        {
            var line = lines[index];
            if (line.StartsWith("  ", StringComparison.Ordinal) &&
                !line.StartsWith("    ", StringComparison.Ordinal) &&
                line.TrimEnd().EndsWith(':'))
            {
                return line.Trim().TrimEnd(':');
            }
        }

        return null;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
