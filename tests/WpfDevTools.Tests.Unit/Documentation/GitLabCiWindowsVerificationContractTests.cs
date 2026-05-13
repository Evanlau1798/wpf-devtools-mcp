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
    public void SandboxHostedWindowsX64Mode_ShouldMirrorHostedCiManagedBuildAndTestLanes()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Start-SandboxCi.ps1"));
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));

        runner.Should().Contain("HostedWindowsX64");
        runner.Should().Contain("SandboxCi.Hosted.ps1");
        runner.Should().Contain("Invoke-HostedWindowsX64Verification");
        hosted.Should().Contain("foreach ($configuration in @('Debug', 'Release'))");
        hosted.Should().Contain("Invoke-NativeFullVerification -DotNetPath $DotNetPath -OutputRoot $OutputRoot -Timestamp $Timestamp -SkipDllLink");
        hosted.Should().Contain("Build solution $configuration x64");
        hosted.Should().Contain("-nodeReuse:false");
        hosted.Should().Contain("-p:UseSharedCompilation=false");
        hosted.Should().Contain("Invoke-UnitDebugTests");
        hosted.Should().Contain("-Configuration $configuration -MaxParallelLanes 1 -UnitDebugShardCount $UnitDebugShardCount");
        hosted.Should().Contain("Invoke-ManagedTestLanes");
        hosted.Should().Contain("-Configuration $configuration -MaxParallelLanes $MaxParallelLanes -ReleaseUnitShardCount $ReleaseUnitShardCount -IncludeReleaseUnit");
        hosted.Should().NotContain("Run integration tests Debug",
            "Windows Sandbox does not reliably produce the native DLL link artifacts required by live bootstrap integration tests");
        hosted.Should().NotContain("Run integration tests Release");
    }

    [Fact]
    public void SandboxEntryPoints_ShouldExposeHostedWindowsX64Mode()
    {
        var launcher = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxCi.ps1"));
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Start-SandboxCi.ps1"));

        launcher.Should().Contain("'HostedWindowsX64'");
        runner.Should().Contain("'HostedWindowsX64'");
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
