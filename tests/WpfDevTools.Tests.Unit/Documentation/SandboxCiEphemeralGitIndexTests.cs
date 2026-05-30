using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiEphemeralGitIndexTests
{
    [Fact]
    public void StartSandboxCi_ShouldFilterStaleTrackedManifestPathsBeforeGitAdd()
    {
        var content = File.ReadAllText(
            WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath("scripts/ci/Start-SandboxCi.ps1"));

        var filterIndex = content.IndexOf("git-existing-tracked-files.txt", StringComparison.Ordinal);
        var addIndex = content.IndexOf("'Index copied sandbox repository files'", StringComparison.Ordinal);

        filterIndex.Should().BeGreaterThan(0);
        filterIndex.Should().BeLessThan(addIndex,
            "stale tracked-file manifests can contain paths deleted by the remediation branch");
        content.Should().Contain("Test-Path -LiteralPath (Join-Path $RepoRoot $_) -PathType Leaf");
        content.Should().Contain("$existingTrackedFilesPath");
    }
}
