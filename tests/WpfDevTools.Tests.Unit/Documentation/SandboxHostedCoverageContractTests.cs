using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxHostedCoverageContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void HostedCoverageVerification_ShouldMirrorGitHubCoverageHangDiagnosticsAndFilter()
    {
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));
        var extras = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.Extras.ps1"));
        var managed = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1"));
        var coverageScripts = hosted + extras;

        coverageScripts.Should().Contain("'--blame-hang-timeout', '10m'",
            "hosted local CI should fail coverage hangs with VSTest diagnostics like GitHub CI");
        coverageScripts.Should().Contain("'--logger', 'trx;LogFileName=coverage-debug.trx'");
        coverageScripts.Should().Contain("(Join-Path $ResultsRoot 'coverage')");
        coverageScripts.Should().Contain("'--filter', 'FullyQualifiedName!~WpfDevTools.Tests.Unit.Release&FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation&Category!=ComposerCompile&Category!=ComposerRuntime'",
            "the local hosted coverage lane should not rerun release, documentation, or expensive Composer capabilities");
        coverageScripts.Should().NotContain("FullyQualifiedName!~ComposerPreview");
        hosted.Should().Contain("-Filter $composerCapabilityExclusionFilter");
        hosted.Should().Contain("Invoke-HostedComposerCapabilityTests");
        extras.Should().Contain("$composerCapabilityExclusionFilter = 'Category!=ComposerCompile&Category!=ComposerRuntime'");
        extras.Should().Contain("'Category=ComposerCompile|Category=ComposerRuntime'");
        managed.Should().Contain("[string]$Filter = ''");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
