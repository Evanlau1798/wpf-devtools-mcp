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
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCreateZipArchivesForStaticBootstrapInstaller()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Compress-Archive");
        content.Should().Contain("release_${version}_win-$architecture.zip");
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

        content.Should().Contain("Test-PackagedServerRuntime.ps1",
            "release packaging smoke should launch the packaged server entrypoint after install so runtime failures are caught before publication");
        content.Should().Contain("Start installed package runtime smoke test",
            "package-local installs should be exercised beyond install/uninstall script success");
        content.Should().Contain("Start online-installed runtime smoke test",
            "online-installer installs should also launch the packaged server entrypoint before cleanup");
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
        content.Should().Contain("Invoke-McpRequest -Process $process -Id 4 -Method 'tools/call'",
            "packaged runtime smoke should execute at least one safe tool call");
        content.Should().Contain("get_processes",
            "get_processes is a safe process-discovery tool that does not mutate target UI state");

        var initializeIndex = content.IndexOf("-Method 'initialize'", StringComparison.Ordinal);
        var initializedIndex = content.IndexOf("-Method 'notifications/initialized'", StringComparison.Ordinal);
        var toolsListIndex = content.IndexOf("-Method 'tools/list'", StringComparison.Ordinal);
        var resourcesReadIndex = content.IndexOf("-Method 'resources/read'", StringComparison.Ordinal);
        var toolCallIndex = content.IndexOf("-Method 'tools/call'", StringComparison.Ordinal);

        initializeIndex.Should().BeLessThan(initializedIndex);
        initializedIndex.Should().BeLessThan(toolsListIndex);
        toolsListIndex.Should().BeLessThan(resourcesReadIndex);
        resourcesReadIndex.Should().BeLessThan(toolCallIndex);
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
    public void CiWorkflow_ShouldUninstallViaInstalledPackageEntryPoint()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("current\\bin\\install.ps1",
            "the uninstall smoke steps should exercise the installed package-local entrypoint instead of falling back to the source-tree installer helper roots");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

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
