using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitLabCiWindowsVerificationContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void GitLabWindowsCi_ShouldReuseSandboxManagedShardRunner()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".gitlab-ci.yml"));

        ci.Should().Contain("SandboxCi.Process.ps1");
        ci.Should().Contain("SandboxCi.Managed.ps1");
        ci.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'");
        ci.Should().Contain("Invoke-ManagedTestLanes");
        ci.Should().Contain("-Configuration $configuration");
        ci.Should().Contain("-UnitDebugShardCount 4");
        ci.Should().Contain("-ReleaseUnitShardCount 8");
        ci.Should().Contain("-MaxParallelLanes 4");
    }

    [Fact]
    public void GitLabWindowsCi_ShouldFailFastWhenManagedTestLanesReportErrors()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".gitlab-ci.yml"));

        ci.Should().Contain("try {");
        ci.Should().Contain("catch {");
        ci.Should().Contain("Managed test lanes $configuration failed");
        ci.Should().NotContain("Assert-NoPowerShellErrors");
        ci.Should().NotContain("$Error.Count");
    }

    [Fact]
    public void GitLabWindowsCi_ShouldRunDebugUnitShardsOneLaneAtATime()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".gitlab-ci.yml"));

        ci.Should().Contain("Invoke-UnitDebugTests");
        ci.Should().Contain("-MaxParallelLanes 1");
    }

    [Fact]
    public void SandboxManagedScript_ShouldSupportConfiguredManagedTestShards()
    {
        var managed = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1"));

        managed.Should().Contain("[ValidateSet('Debug', 'Release')]");
        managed.Should().Contain("[string]$Configuration = 'Debug'");
        managed.Should().Contain("--configuration");
        managed.Should().Contain("$Configuration");
        managed.Should().Contain("unit-$configurationSlug-shard-$shardNumber.trx");
        managed.Should().Contain("release-unit-$configurationSlug-shard-$shardNumber.trx");
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
