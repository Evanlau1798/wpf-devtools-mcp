using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxHostedCoverageContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void HostedCoverageVerification_ShouldMirrorGitHubCoverageHangDiagnosticsAndFilter()
    {
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));

        hosted.Should().Contain("'--blame-hang-timeout', '10m'",
            "hosted local CI should fail coverage hangs with VSTest diagnostics like GitHub CI");
        hosted.Should().Contain("'--logger', 'trx;LogFileName=coverage-debug.trx'");
        hosted.Should().Contain("(Join-Path $ResultsRoot 'coverage')");
        hosted.Should().Contain("'--filter', 'FullyQualifiedName!~WpfDevTools.Tests.Unit.Release&FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation'",
            "the local hosted coverage lane should not rerun release or documentation contracts that do not contribute managed coverage");
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
