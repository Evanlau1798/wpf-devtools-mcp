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
        var hosted = ReadHostedSandboxCiScripts();

        runner.Should().Contain("HostedWindowsX64");
        runner.Should().Contain("SandboxCi.Hosted.ps1");
        runner.Should().Contain("Invoke-HostedWindowsX64Verification");
        hosted.Should().Contain("foreach ($configuration in @('Debug', 'Release'))");
        hosted.Should().Contain("foreach ($platform in @('x64', 'x86'))");
        hosted.Should().Contain("$runtimeId = if ($Platform -eq 'Win32') { 'win-x86' } else { 'win-x64' }");
        hosted.Should().Contain("$nativePlatform = if ($platform -eq 'x86') { 'Win32' } else { 'x64' }");
        hosted.Should().Contain("Resolve-DotNetNativeHostDirectory -RuntimeId $runtimeId");
        hosted.Should().Contain("Invoke-HostedNativeBootstrapperBuild -Configuration $configuration -Platform $nativePlatform");
        hosted.Should().Contain("src\\WpfDevTools.Bootstrapper\\WpfDevTools.Bootstrapper.vcxproj");
        hosted.Should().Contain("$windowsSdkDirectory = $env:WindowsSDKDir.TrimEnd('\\')");
        hosted.Should().Contain("ConvertTo-MSBuildPropertyValue");
        hosted.Should().Contain("Get-HostedNativeBuildProperties");
        hosted.Should().Contain("Resolve-HostedNetFxSdkLibraryDirectory -Platform $Platform");
        hosted.Should().Contain("if ($Platform -ne 'Win32')");
        hosted.Should().Contain("/p:NetFxSdkLibraryDir=$netFxSdkLibraryDirectory");
        hosted.Should().Contain("/p:WindowsSDKDir=$WindowsSdkDirectory");
        hosted.Should().Contain("/p:WindowsTargetPlatformVersion=$WindowsSdkVersion");
        hosted.Should().Contain("/p:IncludePath=$includePath");
        hosted.Should().Contain("/p:LibraryPath=$libraryPath");
        hosted.Should().Contain("/p:ExecutablePath=$executablePath");
        hosted.Should().NotContain("Invoke-NativeFullVerification -DotNetPath $DotNetPath -OutputRoot $OutputRoot -Timestamp $Timestamp -SkipDllLink",
            "HostedWindowsX64 is meant to mimic the GitHub x64 matrix job, so it must exercise the native DLL link path instead of only the sandbox-safe native smoke archive");
        hosted.Should().Contain("Build solution $configuration $platform");
        hosted.Should().Contain("if ($platform -ne 'x64') {");
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
        var hosted = ReadHostedSandboxCiScripts();

        workflow.Should().Contain("WPFDEVTOOLS_TEST_TIMEOUT_SCALE: '4'",
            "GitHub hosted runners can stretch PowerShell installer tests under Release load, so the workflow should use the same explicit harness timeout scale as sandbox hosted CI");
        hosted.Should().Contain("$previousTimeoutScale = $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE");
        hosted.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'",
            "HostedWindowsX64 should mirror the GitHub hosted Release unit timeout budget");
        hosted.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = $previousTimeoutScale");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldMirrorCoveragePackagingSmokeAndNuGetJobs()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var releaseWorkflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));
        var hosted = ReadHostedSandboxCiScripts();

        workflow.Should().Contain("Run tests with coverage");
        workflow.Should().Contain("Run release packaging smoke test");
        workflow.Should().Contain("architecture: [x64, x86]");
        releaseWorkflow.Should().Contain("@('x64', 'x86', 'arm64')");
        workflow.Should().Contain("Build SDK package");
        hosted.Should().Contain("Invoke-HostedCoverageVerification");
        hosted.Should().Contain("--settings', 'coverlet.runsettings'");
        hosted.Should().Contain("Invoke-HostedReleasePackagingSmoke -Architecture 'x64'");
        hosted.Should().Contain("Invoke-HostedReleasePackagingSmoke -Architecture 'x86'");
        hosted.Should().Contain("Invoke-HostedReleasePackagingSmoke -Architecture 'arm64'");
        hosted.Should().Contain("[ValidateSet('x64', 'x86', 'arm64')]");
        hosted.Should().Contain("Publish-Release.ps1");
        hosted.Should().Contain("Test-PackagedServerRuntime.ps1");
        hosted.Should().Contain("Invoke-HostedNuGetPack");
        hosted.Should().Contain("dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj");
        hosted.Should().Contain("Invoke-HostedSdkPackageSmoke");
        hosted.Should().Contain("Inspect SDK package contents");
        hosted.Should().Contain("lib/net8.0-windows7.0/WpfDevTools.Inspector.dll");
        hosted.Should().Contain("lib/net8.0-windows7.0/WpfDevTools.Shared.dll");
        hosted.Should().Contain("Create SDK package consumer smoke app");
        hosted.Should().Contain("'new', 'wpf'");
        hosted.Should().Contain("Install SDK package into clean consumer");
        hosted.Should().Contain("Build SDK package clean consumer");
        hosted.Should().Contain("wpf-devtools-sdk-consumer");
        hosted.Should().Contain("Remove-Item -LiteralPath $consumerRoot");
        hosted.Should().Contain("tmp-release-user-smoke-$Architecture",
            "each package install/uninstall pair should use isolated installer state");
        hosted.Should().Contain("APPDATA = Join-Path $installUserRoot",
            "package-local and online installer smoke checks should not share global installer-state.json");
        hosted.Should().Contain("LOCALAPPDATA = Join-Path $installUserRoot");
        hosted.Should().Contain("APPDATA = Join-Path $bootstrapUserRoot");
        hosted.Should().Contain("LOCALAPPDATA = Join-Path $bootstrapUserRoot");
        hosted.Should().Contain("Start targetless protocol-only installed package runtime smoke test $Architecture",
            "direct package runtime smoke without a launched target must be labelled as protocol-only");
        hosted.Should().Contain("Start targetless protocol-only online-installed runtime smoke test $Architecture",
            "direct online-installed runtime smoke without a launched target must be labelled as protocol-only");
        hosted.Should().Contain("Invoke-PackagedRuntimeLiveSmoke.ps1",
            "sandbox hosted CI must mirror GitHub's target-aware package runtime smoke, not only protocol-only startup");
        hosted.Should().Contain("Start target-aware live-injection installed package runtime smoke test $Architecture");
        hosted.Should().Contain("Start target-aware live-injection online-installed runtime smoke test $Architecture");
        hosted.Should().Contain("Skipping targetless protocol-only packaged server runtime smoke test for $Architecture",
            "the hosted x64 lane validates non-x64 package install/uninstall layout but skips runtime launch");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldUseX86DotNetForX86PackageRuntimeSmoke()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Start-SandboxCi.ps1"));
        var managed = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1"));
        var hosted = ReadHostedSandboxCiScripts();

        managed.Should().Contain("[ValidateSet('x64', 'x86')] [string]$Architecture = 'x64'");
        managed.Should().Contain("'.dotnet-x86'");
        managed.Should().Contain("'-Architecture',");
        managed.Should().Contain("$Architecture");
        runner.Should().Contain("Install-DotNetSdk -RepoRoot $sandboxRepoWorkRoot -Architecture 'x86'");
        runner.Should().Contain("WPFDEVTOOLS_HOSTED_X86_DOTNET_ROOT");
        hosted.Should().Contain("$env:WPFDEVTOOLS_HOSTED_X86_DOTNET_ROOT");
        hosted.Should().Contain("DOTNET_ROOT = $x86DotNetRoot");
        hosted.Should().Contain("PATH = \"$x86DotNetRoot;$env:PATH\"");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldMirrorArm64CrossCompileJob()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var hosted = ReadHostedSandboxCiScripts();

        workflow.Should().Contain("Build ARM64 (cross-compile, no test - no ARM64 runner available)");
        hosted.Should().Contain("Invoke-HostedArm64Build");
        hosted.Should().Contain("Build ARM64 Release cross-compile");
        hosted.Should().Contain("'-p:Platform=ARM64'");
    }

    [Fact]
    public void SandboxHostedWindowsX64Mode_ShouldMirrorDocsPagesLocalBuild()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "docs-pages.yml"));
        var hosted = ReadHostedSandboxCiScripts();

        workflow.Should().Contain("Build DocFX site");
        hosted.Should().Contain("Invoke-HostedDocsPagesBuild");
        hosted.Should().Contain("'tool', 'restore'");
        hosted.Should().Contain("'restore', 'WpfDevTools.sln', '--locked-mode'");
        hosted.Should().Contain("src\\WpfDevTools.Shared\\WpfDevTools.Shared.csproj");
        hosted.Should().Contain("src\\WpfDevTools.Inspector.Sdk\\WpfDevTools.Inspector.Sdk.csproj");
        hosted.Should().Contain("'tool', 'run', 'docfx', 'docfx/docfx.json'");
        hosted.Should().Contain("Test-DocFxDocumentation.ps1");
        hosted.Should().Contain("Validate DocFX links and parity");
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
        hosted.Should().Contain("[int]$UnitDebugShardCount = 1",
            "the default no-VM hosted path should keep process/window-heavy unit tests unsharded while release-unit tests carry the parallel speedup");
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
    public void HostedCiEntryPoint_ShouldRunHostedWindowsX64WithoutStartingWindowsSandbox()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Invoke-HostedCi.ps1"));

        script.Should().Contain("Start-SandboxCi.ps1");
        script.Should().Contain("[string]$Mode = 'HostedWindowsX64'");
        script.Should().Contain("-Mode $Mode");
        script.Should().Contain("-MappedRepoRoot $repoRootPath");
        script.Should().Contain("-MappedWorkRoot $workRootPath");
        script.Should().Contain("-MappedOutputRoot $outputRootPath");
        script.Should().Contain("-LocalWorkRoot $localWorkRootPath");
        script.Should().Contain("tmp\\hosted-ci",
            "the no-VM hosted CI path should keep one-off work and logs under the repository tmp directory");
        script.Should().Contain("[int]$MaxParallelLanes = 4");
        script.Should().Contain("[int]$UnitDebugShardCount = 1");
        script.Should().Contain("[int]$ReleaseUnitShardCount = 8");
        script.Should().NotContain("WindowsSandbox.exe");
        script.Should().NotContain(".wsb");
        script.Should().NotContain("Stop-WindowsSandboxHcs.ps1");
    }

    [Fact]
    public void SandboxRunner_ShouldAllowHostedEntryPointToUseRepositoryTempLocalWorkRoot()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "Start-SandboxCi.ps1"));

        runner.Should().Contain("[string]$LocalWorkRoot = ''");
        runner.Should().Contain("$sandboxLocalWorkRoot = if ([string]::IsNullOrWhiteSpace($LocalWorkRoot))");
        runner.Should().Contain("[System.IO.Path]::GetFullPath($LocalWorkRoot)");
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

    private static string ReadHostedSandboxCiScripts()
    {
        var hosted = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.ps1"));
        var extras = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Hosted.Extras.ps1"));
        return hosted + Environment.NewLine + extras;
    }
}
