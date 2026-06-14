using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Tests.Unit.Injector;
using WpfDevTools.Tests.Unit.Inspector;
using WpfDevTools.Tests.Unit.Inspector.Analyzers;
using WpfDevTools.Tests.Unit.Inspector.Handlers;
using WpfDevTools.Tests.Unit.Inspector.Utilities;
using WpfDevTools.Tests.Unit.McpServer;
using WpfDevTools.Tests.Unit.McpServer.Tools;
using WpfDevTools.Tests.Unit.Security;
using WpfDevTools.Tests.Unit.InspectorSdk;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

[Collection("BindingErrorTests")]
public sealed class UnitTestParallelizationContractTests
{
    private const string ReleaseUnitTestProjectPath =
        "tests/WpfDevTools.Tests.Unit.Release/WpfDevTools.Tests.Unit.Release.csproj";

    [Fact]
    public void XunitRunnerConfig_ShouldEnableAssemblyParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.Unit/xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeAssembly").GetBoolean()
            .Should().BeTrue("the unit suite should run independent test collections in parallel; collections with shared mutable state already enforce serialization via DisableParallelization");
    }

    [Fact]
    public void XunitRunnerConfig_ShouldEnableCollectionParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.Unit/xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean()
            .Should().BeTrue("the unit suite should parallelize tests within independent test collections to avoid multi-minute serial runs");
        document.RootElement.GetProperty("maxParallelThreads").GetInt32()
            .Should().Be(0, "the unit suite should scale collection fan-out with the host CPU count instead of pinning itself to two workers");
    }

    [Fact]
    public void UnitTests_ShouldNotGloballyEnableUnsafePlaintextInspectorHost()
    {
        var testRoot = GetRepoFilePath("tests/WpfDevTools.Tests.Unit");
        var offenders = Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(GetRepoFilePath("."), path).Replace('\\', '/'),
                Content = File.ReadAllText(path)
            })
            .Where(file => file.Path != "tests/WpfDevTools.Tests.Unit/Execution/UnitTestParallelizationContractTests.cs")
            .Where(file =>
                file.Content.Contains("ModuleInitializer", StringComparison.Ordinal) &&
                file.Content.Contains("WPFDEVTOOLS_ALLOW_UNSAFE_PLAINTEXT_INSPECTORHOST", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        offenders.Should().BeEmpty(
            "legacy plaintext InspectorHost tests must opt in with BeginUnsafePlaintextPolicyTestScope per test instead of changing the whole unit-test process");
    }

    [Fact]
    public void ReleaseUnitXunitRunnerConfig_ShouldLimitPowerShellProcessFanOut()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.Unit.Release/xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean()
            .Should().BeTrue("independent release unit collections can still run concurrently");
        document.RootElement.GetProperty("maxParallelThreads").GetInt32()
            .Should().Be(4, "release unit tests launch many PowerShell installer processes, so fan-out should stay bounded while still avoiding the previous multi-minute two-thread bottleneck");
    }

    [Fact]
    public void ReleaseUnitTestProject_ShouldBeIncludedInPrimaryBuildAndTestEntrypoints()
    {
        ReadRepoFile("WpfDevTools.sln").Should().Contain(
            ReleaseUnitTestProjectPath,
            "the solution build and solution-level test discovery must include the split Release/InstallerScripts test assembly");
        ReadRepoFile(".github/workflows/ci-cd.yml").Should().Contain(
            ReleaseUnitTestProjectPath,
            "CI must execute the split Release/InstallerScripts test assembly");
        ReadRepoFile("scripts/tools/packaging/Preflight-Release.ps1").Should().Contain(
            ReleaseUnitTestProjectPath,
            "release preflight must execute release and installer tests before publishing artifacts");
    }

    [Fact]
    public void ReleaseUnitTestCiStep_ShouldNotPassPlatformWhenUsingNoBuild()
    {
        var ciLines = ReadRepoFile(".github/workflows/ci-cd.yml")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var releaseTestStep = GetNamedStepBlock(ciLines, "Run release unit tests");
        var releaseTestCommand = ciLines.Single(line => line.Contains(
            $"dotnet test {ReleaseUnitTestProjectPath}",
            StringComparison.Ordinal));

        releaseTestStep.Should().Contain("      if: matrix.platform == 'x64'",
            "x86 matrix builds validate compilation, while no-build test lanes should run only against the hosted architecture output layout");
        releaseTestCommand.Should().Contain("--no-build");
        releaseTestCommand.Should().NotContain(
            "-p:Platform=",
            "the solution maps the split release test project to Any CPU, so passing Platform to a csproj --no-build test command makes VSTest probe bin/<Platform>/<Configuration> instead of the built output");
    }

    [Fact]
    public void ReleaseUnitTests_ShouldNotInspectMovedHarnessFromOldAssemblyPath()
    {
        var releaseTestSources = Directory
            .EnumerateFiles(GetRepoFilePath("tests/WpfDevTools.Tests.Unit.Release"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        releaseTestSources.Should().NotContain(
            source => source.Contains("tests/WpfDevTools.Tests.Unit/Release/ReleaseScriptTestHarness.cs", StringComparison.Ordinal),
            "contracts in the split release test assembly must inspect the active harness copy, not the stale file left in the original unit assembly");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/Release/ReleaseScriptTestHarness.Packaging.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit.Release/Release/ReleaseScriptTestHarness.Packaging.cs")]
    public void ReleaseScriptHarnessPackageCache_ShouldUseCrossProcessLock(string relativePath)
    {
        var source = ReadRepoFile(relativePath);
        var buildStart = source.IndexOf(
            "private static CachedPackageArtifacts BuildCachedPackageArtifacts",
            StringComparison.Ordinal);
        var lockIndex = source.IndexOf(
            "using var cacheLock = AcquirePackageArtifactCacheLock(cacheKey);",
            StringComparison.Ordinal);
        var firstCacheProbe = source.IndexOf(
            "if (Directory.Exists(packageDir)",
            buildStart,
            StringComparison.Ordinal);
        var firstCacheDelete = source.IndexOf(
            "DeleteDirectory(cacheRoot);",
            buildStart,
            StringComparison.Ordinal);

        buildStart.Should().BeGreaterThanOrEqualTo(0);
        lockIndex.Should().BeGreaterThan(buildStart,
            "shared release package cache rebuilds must be serialized across the Unit and ReleaseUnit assemblies");
        firstCacheProbe.Should().BeGreaterThan(lockIndex,
            "cache validity probes must run under the same cross-process lock as destructive rebuilds");
        firstCacheDelete.Should().BeGreaterThan(lockIndex,
            "deleting the shared cache root must happen only after the lock is held");
        source.Should().Contain("private static FileStream AcquirePackageArtifactCacheLock(string cacheKey)");
        source.Should().Contain("release-script-harness-cache-locks");
    }

    [Fact]
    public void SignaturePolicyTests_ShouldUseProcessEnvironmentCollection()
    {
        var attributeData = typeof(SignaturePolicyTests).GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull();
        attributeData!.ConstructorArguments.Should().ContainSingle();
        attributeData.ConstructorArguments[0].Value.Should().Be("ProcessEnvironment");
    }

    [Fact]
    public void ConnectToolEnvironmentMutationTests_ShouldUseProcessEnvironmentCollection()
    {
        GetCollectionName(typeof(ConnectToolTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(ConnectToolRawInjectionPolicyTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(ConnectToolRawInjectionPolicyErrorCodeTests)).Should().Be("ProcessEnvironment");
    }

    [Fact]
    public void InspectorSdkInitializationTestClasses_ShouldUseProcessEnvironmentCollection()
    {
        GetCollectionName(typeof(InspectorSdkInitializationConfigurationTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(InspectorSdkCleanupTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(InspectorSdkDispatcherLifecycleTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(InspectorSdkOptionsInitializationTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(ScreenshotStorageTests)).Should().Be("ProcessEnvironment");
    }

    [Fact]
    public void InspectorSdkDispatcherLifecycleTests_ShouldUseGenerousAsyncGuards()
    {
        var source = ReadRepoFile(
            "tests/WpfDevTools.Tests.Unit/InspectorSdk/InspectorSdkDispatcherLifecycleTests.cs");

        source.Should().NotContain("WaitAsync(TimeSpan.FromSeconds(3))",
            "slow Windows CI runners can delay timer continuations long enough for a tight outer guard to hide the SDK timeout being asserted");
        source.Should().NotContain("WaitAsync(TimeSpan.FromSeconds(2))",
            "timeout-boundary tests also need the outer guard to be wider than the SDK deadline under VM contention");
        source.Should().Contain("WaitAsync(TimeSpan.FromSeconds(10))");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/Serialization/MessageFramingTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit/Serialization/MessageFramingBufferPoolingTests.cs")]
    public void MessageFramingPipeTests_ShouldUseBoundedPipeOperations(string relativePath)
    {
        var source = ReadRepoFile(relativePath);

        source.Should().Contain("CreatePipeTimeout()");
        source.Should().Contain("timeout.Token");
        source.Should().Contain("WaitAsync(TimeSpan.FromSeconds(10))");
        source.Should().NotContain("ConnectAsync();");
        source.Should().NotContain("WaitForConnectionAsync();");
    }

    [Fact]
    public void ProcessEnvironmentCollection_ShouldDisableParallelization()
    {
        var collectionType = typeof(SignaturePolicyTests).Assembly
            .GetType("WpfDevTools.Tests.Unit.Execution.ProcessEnvironmentCollection");
        var attribute = collectionType?.GetCustomAttribute<CollectionDefinitionAttribute>();

        collectionType.Should().NotBeNull();
        attribute.Should().NotBeNull();
        attribute!.DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public void BindingErrorTraceListenerTests_ShouldUseBindingErrorCollection()
    {
        GetCollectionName(typeof(BindingErrorTraceListenerTests))
            .Should().Be("BindingErrorTests");
        GetCollectionName(typeof(BindingErrorTraceListenerLifecycleTests))
            .Should().Be("BindingErrorTests");
        GetCollectionName(typeof(BindingErrorCorrelationTests))
            .Should().Be("BindingErrorTests");
        GetCollectionName(typeof(DiagnosticNormalizationTests))
            .Should().Be("BindingErrorTests");
        GetCollectionName(typeof(RequestDispatcherDisposeTraceCleanupTests))
            .Should().Be("BindingErrorTests");
        GetCollectionName(typeof(RequestDispatcherRegistryTests))
            .Should().Be("BindingErrorTests");
    }

    [Fact]
    public void BindingErrorCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Inspector.Analyzers.BindingErrorTestCollection");
    }

    [Fact]
    public void DiagnosticNormalizationTests_ShouldRestoreBindingTraceSwitchLevelOnDispose()
    {
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        try
        {
            source.Switch.Level = SourceLevels.Warning;
            var test = new DiagnosticNormalizationTests();
            test.GetBindingErrors_ShouldExposeNormalizedDiagnosticFields();
            source.Switch.Level.Should().Be(SourceLevels.Error);

            ((IDisposable)test).Dispose();

            source.Switch.Level.Should().Be(SourceLevels.Warning);
        }
        finally
        {
            BindingErrorTraceListener.ResetInstance();
            source.Switch.Level = originalLevel;
        }
    }

    [Fact]
    public void BootstrapStateTests_ShouldUseBootstrapStateCollection()
    {
        GetCollectionName(typeof(BootstrapConcurrencyTests)).Should().Be("BootstrapState");
        GetCollectionName(typeof(BootstrapInitializationRollbackTests)).Should().Be("BootstrapState");
    }

    [Fact]
    public void BootstrapStateCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.BootstrapStateCollection");
    }

    [Fact]
    public void DependencyPropertyMonitoringTests_ShouldUseDependencyPropertyMonitoringCollection()
    {
        GetCollectionName(typeof(HandlerWithParamsTests)).Should().Be("DependencyPropertyMonitoring");
    }

    [Fact]
    public void DependencyPropertyMonitoringCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.DependencyPropertyMonitoringCollection");
    }

    [Fact]
    public void TimingSensitiveTests_ShouldUseTimingSensitiveCollection()
    {
        GetCollectionName(typeof(FileLoggerPerformanceTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(FileLoggerTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(CertificateManagerTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(DependencyPropertyWaitForChangeTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(EventAnalyzerTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(EventAnalyzerHandledEventRegressionTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(EventHandlerTraceModeTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(NamedPipeClientTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(NamedPipeClientProtocolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(NamedPipeClientTimeoutBudgetTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostAuthTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostSessionTimeoutTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolConcurrencyTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolCompatibilityTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.GetAffectedElementsToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.DiagnoseVisibilityToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WatchDpChangesToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.ElementScreenshotToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.TraceRoutedEventsToolReplayTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.ConnectToolConcurrencyTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.ConnectToolRawInjectionIdentityTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostObservabilityTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(EncryptedCommunicationTests)).Should().Be("TimingSensitive");
    }

    [Fact]
    public void FileLoggerTests_ShouldUseXunitV2DisposableCleanup()
    {
        typeof(FileLoggerTests).Should().BeAssignableTo<IDisposable>(
            "xUnit v2 reliably invokes IDisposable on test classes, while IAsyncDisposable-only cleanup left test_log_* files in OS temp");
    }

    [Fact]
    public void InspectorSdkInitializationTests_ShouldUseTrackedTestContextCleanup()
    {
        typeof(InspectorSdkInitializationTests).Should().BeAssignableTo<IDisposable>(
            "SDK initialization tests create temporary certificate directories and must use tracked cleanup even when initialization fails early");
    }

    [Fact]
    public void TimingSensitiveCollection_ShouldRemainParallelizable()
    {
        var collectionType = typeof(SignaturePolicyTests).Assembly
            .GetType("WpfDevTools.Tests.Unit.Execution.TimingSensitiveCollection");
        var attribute = collectionType?.GetCustomAttribute<CollectionDefinitionAttribute>();

        collectionType.Should().NotBeNull();
        attribute.Should().NotBeNull();
        attribute!.DisableParallelization.Should().BeFalse(
            "TimingSensitive serializes timing-sensitive classes within one lane, but should not block unrelated collections");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Integration/DependencyPropertyWaitForChangeIntegrationTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/E2E/WaitForDpChangeE2eTests.cs")]
    public void DpWaitIntegrationTests_ShouldUseConditionBasedSynchronization(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain("Task.Delay(",
            "DP wait integration and E2E tests should coordinate on observable handshakes instead of fixed sleeps");
        content.Should().NotContain("Thread.Sleep(",
            "DP wait integration and E2E tests should coordinate on observable handshakes instead of fixed sleeps");
    }

    [Fact]
    public void InspectorHostLifecycleTests_ShouldUseInspectorHostLifecycleCollection()
    {
        GetCollectionName(typeof(InspectorHostConcurrencyTests)).Should().Be("InspectorHostLifecycle");
        GetCollectionName(typeof(InspectorHostLifecycleReviewTests)).Should().Be("InspectorHostLifecycle");
    }

    [Fact]
    public void InspectorHostLifecycleCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.InspectorHostLifecycleCollection");
    }

    [Fact]
    public void TraceStateTests_ShouldUseTraceStateCollection()
    {
        GetCollectionName(typeof(TraceAuditLoggerTests)).Should().Be("TraceState");
        GetCollectionName(typeof(AuditLoggerStaticTests)).Should().Be("TraceState");
        GetCollectionName(typeof(EventLogAuditLoggerTests)).Should().Be("TraceState");
        GetCollectionName(typeof(ConnectToolLoggingTests)).Should().Be("TraceState");
    }

    [Fact]
    public void TraceStateCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.TraceStateCollection");
    }

    [Fact]
    public void ToolCallHelperStateTests_ShouldUseToolCallHelperStateCollection()
    {
        GetCollectionName(typeof(ToolCallHelperMetricsTests)).Should().Be("ToolCallHelperState");
        GetCollectionName(typeof(ToolCallHelperTests)).Should().Be("ToolCallHelperState");
        GetCollectionName(typeof(McpToolsWrapperTests)).Should().Be("ToolCallHelperState");
    }

    [Fact]
    public void ToolCallHelperStateCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.ToolCallHelperStateCollection");
    }

    [Fact]
    public void SecurityStateTests_ShouldUseSecurityStateCollection()
    {
        GetCollectionName(typeof(LocalSecretProtectorTests)).Should().Be("SecurityState");
    }

    [Fact]
    public void SecurityStateCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.SecurityStateCollection");
    }

    [Fact]
    public void ProcessDiscoveryTests_ShouldUseProcessDiscoveryCollection()
    {
        GetCollectionName(typeof(WpfProcessDetectorTests)).Should().Be("ProcessDiscovery");
        GetCollectionName(typeof(WpfProcessDetectorPerformanceTests)).Should().Be("ProcessDiscovery");
    }

    [Fact]
    public void ProcessDiscoveryCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.ProcessDiscoveryCollection");
    }

    [Fact]
    public void InteractionStateTests_ShouldUseInteractionStateCollection()
    {
        GetCollectionName(typeof(InteractionAnalyzerTests)).Should().Be("InteractionState");
        GetCollectionName(typeof(InteractionAnalyzerKeyboardNavigationTests)).Should().Be("InteractionState");
    }

    [Fact]
    public void InteractionStateCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.InteractionStateCollection");
    }

    [Fact]
    public void VisualTreeAnalyzerPerformanceTests_ShouldUseWpfCollection()
    {
        GetCollectionName(typeof(VisualTreeAnalyzerPerformanceTests))
            .Should().Be("WPF");
    }

    [Fact]
    public void WpfDispatcherCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.WpfDispatcherCollection");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(GetRepoFilePath(relativePath)).Replace('\\', '/');

    private static string[] GetNamedStepBlock(string[] lines, string stepName)
    {
        var normalizedLines = lines.Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(normalizedLines, line => line == $"    - name: {stepName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should define step {stepName}");

        var end = Array.FindIndex(normalizedLines, start + 1, line => line.StartsWith("    - name: ", StringComparison.Ordinal));
        if (end < 0)
        {
            end = normalizedLines.Length;
        }

        return normalizedLines[start..end];
    }

    private static string? GetCollectionName(Type testClass)
    {
        var attributeData = testClass.GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull();
        attributeData!.ConstructorArguments.Should().ContainSingle();
        return attributeData.ConstructorArguments[0].Value as string;
    }

    private static void AssertCollectionIsNonParallel(string collectionTypeName)
    {
        var collectionType = typeof(SignaturePolicyTests).Assembly.GetType(collectionTypeName);
        var attribute = collectionType?.GetCustomAttribute<CollectionDefinitionAttribute>();

        collectionType.Should().NotBeNull();
        attribute.Should().NotBeNull();
        attribute!.DisableParallelization.Should().BeTrue();
    }
}
