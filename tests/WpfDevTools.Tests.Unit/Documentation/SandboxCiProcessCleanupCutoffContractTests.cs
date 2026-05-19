using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxProcessCleanup_ShouldNotMoveExitedRootCutoffBackToExitTime()
    {
        var cleanup = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ProcessCleanup.ps1"));
        var updateStart = cleanup.IndexOf("function Update-ProcessSnapshotCutoffFromProcess", StringComparison.Ordinal);
        var updateEnd = cleanup.IndexOf("function Expand-ProcessSnapshots", StringComparison.Ordinal);
        var updateBlock = cleanup[updateStart..updateEnd];

        updateBlock.Should().Contain("[Math]::Max([long]$Snapshot.DescendantCutoffUtcTicks",
            "startup cleanup must keep the wider scan cutoff when a short-lived root exits before its launcher or child is fully discoverable");
        updateBlock.Should().NotContain("$Snapshot.DescendantCutoffUtcTicks = $Process.ExitTime.ToUniversalTime().Ticks",
            "using only ExitTime can exclude descendants that become visible just after the root exits on GitHub-hosted timing");
    }
}
