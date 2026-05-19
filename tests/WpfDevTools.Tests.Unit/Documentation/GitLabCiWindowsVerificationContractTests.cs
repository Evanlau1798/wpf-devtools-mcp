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
        hosted.Should().Contain("Resolve-DotNetNativeHostDirectory -RuntimeId 'win-x64'");
        hosted.Should().Contain("Invoke-HostedNativeBootstrapperBuild -Configuration $configuration -Platform 'x64'");
        hosted.Should().Contain("src\\WpfDevTools.Bootstrapper\\WpfDevTools.Bootstrapper.vcxproj");
        hosted.Should().Contain("$windowsSdkDirectory = $env:WindowsSDKDir.TrimEnd('\\')");
        hosted.Should().Contain("ConvertTo-MSBuildPropertyValue");
        hosted.Should().Contain("/p:WindowsSDKDir=$windowsSdkDirectory");
        hosted.Should().Contain("/p:WindowsTargetPlatformVersion=$windowsSdkVersion");
        hosted.Should().Contain("/p:IncludePath=$includePath");
        hosted.Should().Contain("/p:LibraryPath=$libraryPath");
        hosted.Should().Contain("/p:ExecutablePath=$executablePath");
        hosted.Should().NotContain("Invoke-NativeFullVerification -DotNetPath $DotNetPath -OutputRoot $OutputRoot -Timestamp $Timestamp -SkipDllLink",
            "HostedWindowsX64 is meant to mimic the GitHub x64 matrix job, so it must exercise the native DLL link path instead of only the sandbox-safe native smoke archive");
        hosted.Should().Contain("Build solution $configuration x64");
        hosted.Should().Contain("-nodeReuse:false");
        hosted.Should().Contain("-p:UseSharedCompilation=false");
        hosted.Should().Contain("Invoke-UnitDebugTests");
        hosted.Should().Contain("-Configuration $configuration -MaxParallelLanes 1 -UnitDebugShardCount $UnitDebugShardCount");
        hosted.Should().Contain("Invoke-ManagedTestLanes");
        hosted.Should().Contain("-Configuration $configuration -MaxParallelLanes $MaxParallelLanes -ReleaseUnitShardCount $ReleaseUnitShardCount -IncludeReleaseUnit");
        hosted.Should().Contain("Run integration tests Debug");
        hosted.Should().Contain("tests\\WpfDevTools.Tests.Integration\\WpfDevTools.Tests.Integration.csproj");
        hosted.Should().NotContain("Run integration tests Release");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldUseGitHubTimeoutScale()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));

        workflow.Should().Contain("WPFDEVTOOLS_TEST_TIMEOUT_SCALE: '4'",
            "GitHub hosted runners can stretch PowerShell installer tests under Release load, so the workflow should use the same explicit harness timeout scale as sandbox hosted CI");
        hosted.Should().Contain("$previousTimeoutScale = $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE");
        hosted.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'",
            "HostedWindowsX64 should mirror the GitHub hosted Release unit timeout budget");
        hosted.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = $previousTimeoutScale");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldMirrorCoverageAndPackagingSmokeJobs()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));

        workflow.Should().Contain("Run tests with coverage");
        workflow.Should().Contain("Run release packaging smoke test");
        hosted.Should().Contain("Invoke-HostedCoverageVerification");
        hosted.Should().Contain("--settings', 'coverlet.runsettings'");
        hosted.Should().Contain("Invoke-HostedReleasePackagingSmoke -Architecture 'x64'");
        hosted.Should().Contain("Publish-Release.ps1");
        hosted.Should().Contain("Test-PackagedServerRuntime.ps1");
    }

    [Fact]
    public void HostedWindowsX64Ci_ShouldPrepareRuntimeSpecificServerOutputBeforeIntegrationTests()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var gitlab = File.ReadAllText(Path.Combine(RepoRoot, ".gitlab-ci.yml"));
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));
        var runtimeBuildStart = hosted.IndexOf("function Invoke-HostedServerRuntimeBuild", StringComparison.Ordinal);
        var runtimeBuildEnd = hosted.IndexOf("function Invoke-HostedWindowsX64Verification", StringComparison.Ordinal);
        var runtimeBuildBlock = hosted[runtimeBuildStart..runtimeBuildEnd];

        workflow.Should().Contain("Restore server runtime dependencies");
        workflow.Should().Contain("dotnet restore src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj --locked-mode -r win-x64");
        workflow.Should().Contain("Prepare server runtime output");
        workflow.Should().Contain("dotnet build src/WpfDevTools.Mcp.Server/WpfDevTools.Mcp.Server.csproj --configuration ${{ matrix.configuration }} --runtime win-x64 --self-contained false --no-restore -nodeReuse:false -p:UseSharedCompilation=false");

        gitlab.Should().Contain("Restore server runtime dependencies win-x64");
        gitlab.Should().Contain("Prepare server runtime output $configuration win-x64");

        hosted.Should().Contain("Invoke-HostedServerRuntimeBuild");
        hosted.Should().Contain("src\\WpfDevTools.Mcp.Server\\WpfDevTools.Mcp.Server.csproj");
        hosted.Should().Contain("'--runtime', 'win-x64'");
        hosted.Should().Contain("Invoke-HostedServerRuntimeBuild -DotNetPath $DotNetPath -Configuration $configuration");
        hosted.IndexOf("Invoke-HostedServerRuntimeBuild -DotNetPath $DotNetPath -Configuration $configuration", StringComparison.Ordinal)
            .Should().BeLessThan(hosted.IndexOf("Run integration tests Debug", StringComparison.Ordinal));
        runtimeBuildBlock.Should().NotContain("'-p:Platform=x64'",
            "the runtime-specific server output must land under bin/<Configuration>/net8.0/win-x64 for release packaging -SkipBuild");
    }

    [Fact]
    public void NativeBootstrapperCiBuilds_ShouldDisableIncrementalLinking()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var gitlab = File.ReadAllText(Path.Combine(RepoRoot, ".gitlab-ci.yml"));
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));

        workflow.Should().Contain("/p:LinkIncremental=false");
        gitlab.Should().Contain("/p:LinkIncremental=false");
        hosted.Should().Contain("/p:LinkIncremental=false",
            "Windows Sandbox mapped folders can make Debug incremental native linking fail inside link.exe before managed tests start");
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
