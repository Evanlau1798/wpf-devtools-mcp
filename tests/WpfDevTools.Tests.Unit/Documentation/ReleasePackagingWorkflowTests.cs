using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReleasePackagingWorkflowTests
{
    [Fact]
    public void CiWorkflow_ShouldSmokeTestReleasePackagingScripts()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Publish-Release.ps1");
        content.Should().Contain("bin/install.ps1");
        content.Should().Contain("scripts/online-installer.ps1");
        content.Should().NotContain("Install-WpfDevTools.ps1");
        content.Should().NotContain("Uninstall-WpfDevTools.ps1");
    }

    [Fact]
    public void CiWorkflow_ShouldCoverArm64ReleasePackagingLayout()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("architecture: [x64, x86]");
        content.Should().Contain("release_*_win-${{ matrix.architecture }}");
        content.Should().Contain("release-packaging-smoke-arm64",
            "ARM64 runtime smoke should run on a dedicated ARM64 lane instead of pretending hosted x64 install/uninstall smoke validates the shipped runtime");
        content.Should().Contain("[self-hosted, Windows, ARM64]",
            "ARM64 runtime smoke needs an actual ARM64 runner so the packaged executable can be launched before release publication");
        content.Should().Contain("WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE",
            "the dedicated ARM64 lane should stay opt-in for CI until the repository runner is configured");
    }

    [Fact]
    public void PublishReleaseScript_ShouldBundleCanonicalInstallerScript()
    {
        var content = ReadPublishReleaseScriptSources();

        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCreateZipArchivesForStaticBootstrapInstaller()
    {
        var publishScript = ReadPublishReleaseScript("Publish-Release.ps1");
        var nativeHelper = ReadPublishReleaseScript("Publish-Release.Native.ps1");

        publishScript.Should().Contain("release_${version}_win-$architecture.zip");
        publishScript.Should().Contain("Invoke-ArchiveCreation `");
        publishScript.Should().Contain("-PackageDirectory $packageDir `");
        publishScript.Should().Contain("-ArchivePath $packageArchivePath");
        nativeHelper.Should().Contain("function New-ReleaseArchive");
        nativeHelper.Should().Contain("New-ReleaseArchive `");
        nativeHelper.Should().Contain("Move-Item -LiteralPath $tempArchivePath -Destination $ArchivePath -Force");
    }

    [Fact]
    public void PublishReleaseScript_ShouldPassProjectVersionIntoNativeBootstrapperResource()
    {
        var publishScript = ReadPublishReleaseScript("Publish-Release.ps1");
        var coreHelper = ReadPublishReleaseScript("Publish-Release.Core.ps1");

        coreHelper.Should().Contain("function ConvertTo-NativeResourceVersion");
        publishScript.Should().Contain("ConvertTo-NativeResourceVersion -Version $version");
        publishScript.Should().Contain("ConvertTo-MSBuildPropertyValue -Value $nativeResourceVersion.Numeric");
        publishScript.Should().Contain("/p:BootstrapperFileVersion=");
        publishScript.Should().Contain("/p:BootstrapperProductVersionString=");
    }

    [Fact]
    public void ReleaseLayoutDocs_ShouldDocumentOnlineInstallerSplitExceptionAndFollowUpPlan()
    {
        var english = File.ReadAllText(GetRepoFilePath("docfx/production/release-layout.md"));
        var traditionalChinese = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/production/release-layout.md"));

        foreach (var content in new[] { english, traditionalChinese })
        {
            content.Should().Contain("scripts/online-installer.ps1");
            content.Should().Contain("single-file release artifact");
            content.Should().Contain("thin source entrypoint");
            content.Should().Contain("generated single-file release artifact");
            content.Should().Contain("Post-remediation");
        }

        english.Should().Contain("temporary exception");
        english.Should().Contain("source-file size target");
        english.Should().Contain("do not split");
        english.Should().Contain("current production remediation loop");

        traditionalChinese.Should().Contain("暫時例外");
        traditionalChinese.Should().Contain("source file size target");
        traditionalChinese.Should().Contain("不要");
        traditionalChinese.Should().Contain("拆分");
        traditionalChinese.Should().Contain("目前的 production remediation loop");
    }

    [Fact]
    public void CiWorkflow_ShouldSmokeTestCanonicalOnlineInstaller()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("scripts/online-installer.ps1");
        content.Should().Contain("release_*_win-${{ matrix.architecture }}.zip");
    }

    [Fact]
    public void CiWorkflow_ShouldDefaultTokenPermissionsToReadOnlyContents()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        var topLevelPermissions = GetTopLevelBlock(lines, "permissions");

        topLevelPermissions.Should().Contain("  contents: read",
            "CI should not inherit broad repository-default GITHUB_TOKEN permissions");
        topLevelPermissions.Should().NotContain("  contents: write",
            "ordinary CI jobs do not need repository write access");
    }

    [Fact]
    public void CiWorkflow_ShouldSmokeTestInstalledServerRuntimeAfterInstall()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Invoke-PackagedRuntimeLiveSmoke.ps1",
            "release packaging smoke should launch the packaged server entrypoint against a live WPF target after install so runtime failures are caught before publication");
        content.Should().Contain("Start installed package runtime smoke test",
            "package-local installs should be exercised beyond install/uninstall script success");
        content.Should().Contain("Start online-installed runtime smoke test",
            "online-installer installs should also launch the packaged server entrypoint before cleanup");
    }

    [Fact]
    public void CiWorkflow_ShouldVerifyFullUninstallResidueForReleasePackagingSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Full uninstall published package residue test",
            "package-local smoke should verify installer-owned payload removal, not only client unregistration");
        content.Should().Contain("Full uninstall online installer residue test",
            "online-installer smoke should verify installer-owned payload removal, not only client unregistration");
        content.Should().Contain("Test-InstallResidue.ps1",
            "release smoke should fail if install manifests, current payloads, generated registration artifacts, or rollback/temp files remain after full-uninstall");
        content.Should().Contain("-Action full-uninstall",
            "full payload cleanup must use the installer-supported full-uninstall action instead of ad hoc deletion");
        content.Should().Contain("-InstallRoot './tmp-release-install-smoke'");
        content.Should().Contain("-InstallRoot './tmp-release-bootstrap-smoke'");
    }

    [Fact]
    public void CiWorkflow_ShouldUseTargetAwarePackagedRuntimeLiveSmokeForHostedX64Packages()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Invoke-PackagedRuntimeLiveSmoke.ps1",
            "hosted package runtime smoke must launch the WPF TestApp and pass its exact process identity to the packaged server smoke");
        content.Should().Contain("Start installed package runtime smoke test");
        content.Should().Contain("Start online-installed runtime smoke test");
    }

    [Fact]
    public void PackagedRuntimeLiveSmokeHelper_ShouldLaunchTestAppAndPassExactTargetToRuntimeSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Invoke-PackagedRuntimeLiveSmoke.ps1"));

        content.Should().Contain("tests/WpfDevTools.Tests.TestApp/WpfDevTools.Tests.TestApp.csproj");
        content.Should().Contain("Start-Process");
        content.Should().Contain("MainWindowHandle",
            "the live target must be ready before connect/get_ui_summary are attempted");
        content.Should().Contain("Test-PackagedServerRuntime.ps1");
        content.Should().Contain("-TargetProcessId $targetProcess.Id");
        content.Should().Contain("-TargetProcessPath $targetProcessPath");
        content.Should().Contain("Stop-Process",
            "the helper must not leave a live WPF TestApp behind after package smoke validation");
    }

    [Fact]
    public void CiWorkflow_ShouldRunHostedRuntimeSmokeForX64AndX86Packages()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/ci-cd.yml"));
        var releasePackagingSmokeJob = GetWorkflowJobBlock(lines, "release-packaging-smoke");
        var buildAndTestJob = GetWorkflowJobBlock(lines, "build-and-test");
        var content = string.Join(Environment.NewLine, releasePackagingSmokeJob);

        var packageRuntimeSmoke = GetNamedStepBlock(lines, "Start installed package runtime smoke test");
        var onlineRuntimeSmoke = GetNamedStepBlock(lines, "Start online-installed runtime smoke test");

        content.Should().Contain("Setup .NET x86 SDK",
            "x86 package runtime smoke needs an x86 .NET host before launching the x86 packaged server");
        buildAndTestJob.Should().NotContain("    - name: Setup .NET x86 SDK",
            "x86 runtime smoke setup belongs to the release packaging smoke job, not the build-and-test platform matrix");
        content.Should().Contain("architecture: x86",
            "actions/setup-dotnet supports architecture-specific SDK installation for the x86 package smoke lane");
        packageRuntimeSmoke.Should().NotContain("      if: matrix.architecture == 'x64'",
            "hosted package runtime smoke should cover both x64 and x86 matrix entries");
        onlineRuntimeSmoke.Should().NotContain("      if: matrix.architecture == 'x64'",
            "online-installed runtime smoke should cover both x64 and x86 matrix entries");
    }

    [Fact]
    public void CiWorkflow_ShouldInstallPackageLocalSmokeFromReleaseArchive()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Expand-Archive -LiteralPath $packageArchive.FullName",
            "Publish-Release.ps1 should leave release output as GitHub-ready archives plus sidecars, so CI package-local smoke must extract the zip before running bin/install.ps1");
        content.Should().NotContain("Get-ChildItem 'artifacts/release' -Directory",
            "release packaging smoke should not depend on expanded package directories in the release output root");
    }

    [Fact]
    public void CiWorkflow_ShouldWriteReleasePackagingSmokeToSearchedOutputRoot()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Publish-Release.ps1 -Architectures '${{ matrix.architecture }}' -OutputRoot 'artifacts/release'",
            "the workflow searches artifacts/release for matrix release archives immediately after packaging");
        content.Should().Contain("Publish-Release.ps1 -Architectures 'arm64' -OutputRoot 'artifacts/release'",
            "the ARM64 smoke lane searches artifacts/release for the generated ARM64 archive");
    }

    [Fact]
    public void CiWorkflow_ShouldSerializeDotNetBuildsToAvoidSharedObjLocks()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("dotnet build --configuration ${{ matrix.configuration }} --no-restore -m:1",
            "solution-level CI builds can compile shared project references for multiple target frameworks into the same obj path on hosted runners");
        content.Should().Contain("dotnet build --configuration Release -p:Platform=ARM64 -m:1",
            "the ARM64 solution build should use the same deterministic build scheduling policy");
        content.Should().Contain("dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-restore -m:1",
            "coverage builds should avoid parallel shared project-reference compilation before running tests");
    }

    [Fact]
    public void CiWorkflow_ShouldPackSdkWithoutGeneratePackageOnBuildRecursion()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj --configuration Release --output ./nupkg -p:GeneratePackageOnBuild=false",
            "direct dotnet pack already runs the pack target, so GeneratePackageOnBuild must be disabled to avoid looking for the package artifact before the build output exists");
    }

    [Fact]
    public void CiWorkflow_ShouldRunNoBuildTestsFromActualSolutionBuildOutput()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --configuration ${{ matrix.configuration }} --no-build --verbosity normal",
            "the unit test assembly is emitted under bin/<configuration> by the solution build, not under bin/<platform>/<configuration>");
        content.Should().Contain("dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --configuration ${{ matrix.configuration }} --no-build --verbosity normal",
            "integration tests should run from the no-build output that the preceding solution build actually produced");
        content.Should().NotContain("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --configuration ${{ matrix.configuration }} --no-build --verbosity normal -p:Platform=${{ matrix.platform }}");
        content.Should().NotContain("dotnet test tests/WpfDevTools.Tests.Integration/WpfDevTools.Tests.Integration.csproj --configuration ${{ matrix.configuration }} --no-build --verbosity normal -p:Platform=${{ matrix.platform }}");
    }

    [Fact]
    public void PackagedServerRuntimeSmokeScript_ShouldExerciseProtocolBeyondInitialize()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1"));

        content.Should().Contain("ExpectedResponseId",
            "packaged runtime smoke should validate JSON-RPC response identity for each request");
        content.Should().Contain("jsonRpcVersion",
            "packaged runtime smoke should validate the JSON-RPC protocol version, not just result payload shape");
        content.Should().Contain("Invoke-McpRequest -Process $process -Id 1 -Method 'initialize'",
            "packaged runtime smoke should perform an MCP initialize request first");
        content.Should().Contain("Send-McpNotification -Process $process -Method 'notifications/initialized'",
            "packaged runtime smoke should send the initialized notification before later requests");
        content.Should().Contain("Invoke-McpRequest -Process $process -Id 2 -Method 'tools/list'",
            "packaged runtime smoke should validate MCP tool discovery, not only initialize");
        content.Should().Contain("Invoke-McpRequest -Process $process -Id 3 -Method 'resources/read'",
            "packaged runtime smoke should validate resource serving from the packaged executable");
        content.Should().Contain("wpf://capabilities",
            "capability resource reads are safe and do not require a target process");
        content.Should().Contain("Invoke-McpTool -Process $process -Id 5 -Name 'get_processes'",
            "packaged runtime smoke should execute at least one safe tool call");
        content.Should().Contain("get_processes",
            "get_processes is a safe process-discovery tool that does not mutate target UI state");
        content.Should().Contain("-Name 'connect'",
            "target-aware packaged smoke must prove the installed server can attach to a live WPF target");
        content.Should().Contain("-Name 'ping'",
            "target-aware packaged smoke must prove the secure pipe remains usable after connect");
        content.Should().Contain("-Name 'get_ui_summary'",
            "target-aware packaged smoke must prove scene-level diagnostics work against the live target");
        content.Should().Contain("-Name 'get_dp_value_source'",
            "target-aware packaged smoke must include a safe dependency-property read");
        content.Should().Contain("-Name 'capture_state_snapshot'",
            "target-aware packaged smoke must establish a rollback point before mutation");
        content.Should().Contain("-Name 'set_dp_value'",
            "target-aware packaged smoke must exercise one rollback-safe mutation");
        content.Should().Contain("-Name 'restore_state_snapshot'",
            "target-aware packaged smoke must verify mutation rollback before release");

        var initializeIndex = content.IndexOf("-Method 'initialize'", StringComparison.Ordinal);
        var initializedIndex = content.IndexOf("-Method 'notifications/initialized'", StringComparison.Ordinal);
        var toolsListIndex = content.IndexOf("-Method 'tools/list'", StringComparison.Ordinal);
        var resourcesReadIndex = content.IndexOf("-Method 'resources/read'", StringComparison.Ordinal);
        var toolCallIndex = content.IndexOf("-Name 'get_processes'", StringComparison.Ordinal);

        initializeIndex.Should().BeLessThan(initializedIndex);
        initializedIndex.Should().BeLessThan(toolsListIndex);
        toolsListIndex.Should().BeLessThan(resourcesReadIndex);
        resourcesReadIndex.Should().BeLessThan(toolCallIndex);
    }

    [Fact]
    public void PackagedServerRuntimeSmokeScript_ShouldGuardAgainstStdoutContamination()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Test-PackagedServerRuntime.ps1"));

        content.Should().Contain("stdout contamination",
            "packaged runtime smoke should distinguish MCP protocol pollution from ordinary JSON-RPC errors");
        content.Should().Contain("Invoke-FailingMcpTool -Process $process -Id 4 -Name 'connect'",
            "the smoke lane should include one deterministic failing tool call before continuing with a successful request");
        content.Should().Contain("-AllowError:$AllowError",
            "the JSON-RPC reader should support expected error responses without treating them as stdout contamination");
        content.Should().Contain("Any non-JSON stdout emitted after this failure is caught by the next successful request.",
            "the successful request after the failing probe is the after-failure contamination guard");

        var initializeIndex = content.IndexOf("-Method 'initialize'", StringComparison.Ordinal);
        var toolsListIndex = content.IndexOf("-Method 'tools/list'", StringComparison.Ordinal);
        var resourcesReadIndex = content.IndexOf("-Method 'resources/read'", StringComparison.Ordinal);
        var failingToolIndex = content.IndexOf("Invoke-FailingMcpTool -Process $process -Id 4 -Name 'connect'", StringComparison.Ordinal);
        var safeToolIndex = content.IndexOf("Invoke-McpTool -Process $process -Id 5 -Name 'get_processes'", StringComparison.Ordinal);

        initializeIndex.Should().BeLessThan(toolsListIndex);
        toolsListIndex.Should().BeLessThan(resourcesReadIndex);
        resourcesReadIndex.Should().BeLessThan(failingToolIndex);
        failingToolIndex.Should().BeLessThan(safeToolIndex);
    }

    [Fact]
    public void CiWorkflow_ShouldRunReleasePackagingSmokeWithDeterministicSignatureTestMode()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("WPFDEVTOOLS_INSTALLER_TEST_MODE",
            "release packaging smoke tests need an executable signature-validation lane even when CI does not hold the production signing certificate");
        content.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA",
            "hosted-runner local archive smoke installs need the explicit test-only trust hook so they exercise the generated release sidecars instead of falling back to live release metadata lookup");
        content.Should().Contain("WPFDEVTOOLS_TEST_SIGNATURE_STATUS",
            "Publish-Release.ps1 only supports deterministic fake signature validation when the workflow opts into installer test mode");
        content.Should().Contain("Valid",
            "the smoke workflow should force a valid test signature state instead of depending on unsigned runner artifacts");
    }

    [Fact]
    public void CiWorkflow_ShouldDotSourceOnlineInstallerWithHarnessVariablesForLocalArchiveSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain(". ./scripts/online-installer.ps1",
            "CI local archive smoke should dot-source the online installer so $PSScriptRoot remains available while test harness script variables are set");
        content.Should().Contain("$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true",
            "online-installer.ps1 intentionally ignores environment-only test mode outside the release test harness");
        content.Should().Contain("-TrustedReleaseMetadataDirectory 'artifacts/release'",
            "local archive smoke tests must provide the sidecar metadata directory used by package integrity checks");
        content.Should().Contain("-PackageArchivePath $packageArchive.FullName",
            "the workflow should still exercise the local release archive install path");
    }

    [Fact]
    public void CiWorkflow_ShouldDotSourcePackageLocalInstallerWithHarnessVariablesForSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain(". (Join-Path $packageDir 'bin/install.ps1')",
            "package-local smoke tests need the same script-variable test harness authority as online-installer local archive smoke");
        content.Should().Contain(". $installedScript -Action uninstall",
            "uninstall smoke should preserve package-local script root while test harness variables are set");
        content.Should().NotContain("-File (Join-Path $packageDir 'bin/install.ps1')",
            "running package-local install.ps1 with -File ignores harness-only installer test mode and blocks deterministic signature smoke");
        content.Should().NotContain("-File $installedScript -Action uninstall",
            "running installed install.ps1 with -File should not be used while deterministic test signature variables are in scope");
    }

    [Fact]
    public void CiWorkflow_ShouldUninstallViaInstalledPackageEntryPoint()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("current\\bin\\install.ps1",
            "the uninstall smoke steps should exercise the installed package-local entrypoint instead of falling back to the source-tree installer helper roots");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string ReadPublishReleaseScript(string fileName)
        => File.ReadAllText(Path.Combine(GetRepoFilePath("scripts/tools/packaging"), fileName));

    private static string ReadPublishReleaseScriptSources()
    {
        var packagingRoot = GetRepoFilePath("scripts/tools/packaging");
        var files = new[]
        {
            "Publish-Release.ps1",
            "Publish-Release.Core.ps1",
            "Publish-Release.Signing.ps1",
            "Publish-Release.Native.ps1"
        };

        return string.Join(
            Environment.NewLine,
            files.Select(fileName => File.ReadAllText(Path.Combine(packagingRoot, fileName))));
    }

    private static string[] GetNamedStepBlock(string[] lines, string stepName)
    {
        var start = Array.FindIndex(lines, line => line == $"    - name: {stepName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should define step {stepName}");

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("    - name: ", StringComparison.Ordinal));
        if (end < 0)
        {
            end = lines.Length;
        }

        return lines[start..end];
    }

    private static string[] GetWorkflowJobBlock(string[] lines, string jobName)
    {
        var start = Array.FindIndex(lines, line => line == $"  {jobName}:");
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should define job {jobName}");

        var end = Array.FindIndex(lines, start + 1, line =>
            line.StartsWith("  ", StringComparison.Ordinal) &&
            !line.StartsWith("    ", StringComparison.Ordinal) &&
            line.TrimEnd().EndsWith(':'));
        if (end < 0)
        {
            end = lines.Length;
        }

        return lines[start..end];
    }

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
}
