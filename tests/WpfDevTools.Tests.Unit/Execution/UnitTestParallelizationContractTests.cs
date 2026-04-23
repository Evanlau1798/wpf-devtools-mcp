using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit.Injector;
using WpfDevTools.Tests.Unit.Inspector;
using WpfDevTools.Tests.Unit.Inspector.Analyzers;
using WpfDevTools.Tests.Unit.Inspector.Utilities;
using WpfDevTools.Tests.Unit.McpServer;
using WpfDevTools.Tests.Unit.McpServer.Tools;
using WpfDevTools.Tests.Unit.Release;
using WpfDevTools.Tests.Unit.Security;
using WpfDevTools.Tests.Unit.InspectorSdk;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class UnitTestParallelizationContractTests
{
    [Fact]
    public void XunitRunnerConfig_ShouldEnableCollectionParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.Unit/xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean()
            .Should().BeTrue("the unit suite should parallelize independent test collections to avoid multi-minute serial runs");
        document.RootElement.GetProperty("maxParallelThreads").GetInt32()
            .Should().Be(0, "the unit suite should scale collection fan-out with the host CPU count instead of pinning itself to two workers");
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
    public void InspectorSdkInitializationTestClasses_ShouldUseProcessEnvironmentCollection()
    {
        GetCollectionName(typeof(InspectorSdkInitializationConfigurationTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(InspectorSdkCleanupTests)).Should().Be("ProcessEnvironment");
        GetCollectionName(typeof(InspectorSdkDispatcherLifecycleTests)).Should().Be("ProcessEnvironment");
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
    }

    [Fact]
    public void BindingErrorCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Inspector.Analyzers.BindingErrorTestCollection");
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
        GetCollectionName(typeof(DependencyPropertyAnalyzerTests)).Should().Be("DependencyPropertyMonitoring");
        GetCollectionName(typeof(DependencyPropertyAnalyzerCleanupTests)).Should().Be("DependencyPropertyMonitoring");
        GetCollectionName(typeof(DependencyPropertyAnalyzerWatchEventTests)).Should().Be("DependencyPropertyMonitoring");
        GetCollectionName(typeof(DependencyPropertyAnalyzerWatcherCleanupTests)).Should().Be("DependencyPropertyMonitoring");
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
        GetCollectionName(typeof(NamedPipeClientTimeoutBudgetTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostAuthTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InspectorHostSessionTimeoutTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolConcurrencyTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolCompatibilityTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.GetAffectedElementsToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.ConnectToolTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(WpfDevTools.Tests.Unit.McpServer.Tools.ConnectToolConcurrencyTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InstallerTuiRuntimeTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InstallerTuiInstallLocationEditorTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(InstallerProcessLifecycleTests)).Should().Be("TimingSensitive");
        GetCollectionName(typeof(EncryptedCommunicationTests)).Should().Be("TimingSensitive");
    }

    [Fact]
    public void TimingSensitiveCollection_ShouldDisableParallelization()
    {
        AssertCollectionIsNonParallel("WpfDevTools.Tests.Unit.Execution.TimingSensitiveCollection");
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

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

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
