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

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

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
