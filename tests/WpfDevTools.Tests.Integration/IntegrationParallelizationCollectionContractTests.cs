using FluentAssertions;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WpfDevTools.Tests.Integration.E2E;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Integration;

public sealed class IntegrationParallelizationCollectionContractTests
{
    [Fact]
    public void RuntimeXunitRunnerConfig_ShouldEnableCollectionParallelization()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeAssembly").GetBoolean().Should().BeTrue("the integration suite should run independent collections in parallel; WpfIntegration and LiveBootstrapIntegration share a common serialization lane via merged collection");
        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("maxParallelThreads").GetInt32().Should().Be(0);
    }

    [Fact]
    public void RuntimeAssembly_ShouldRunLiveBootstrapCollectionFirst()
    {
        var attribute = typeof(LiveBootstrapFirstCollectionOrderer).Assembly
            .GetCustomAttribute<TestCollectionOrdererAttribute>();

        attribute.Should().NotBeNull();
        typeof(LiveBootstrapFirstCollectionOrderer)
            .Should()
            .BeAssignableTo<ITestCollectionOrderer>();
    }

    [Fact]
    public void LiveBootstrapFirstCollectionOrderer_ShouldPrioritizeLiveBootstrapCollection()
    {
        var alphaCollection = new StubTestCollection("AlphaCollection");
        var liveBootstrapCollection = new StubTestCollection("WpfAndBootstrapIntegration");
        var zetaCollection = new StubTestCollection("ZetaCollection");

        var orderedCollections = new LiveBootstrapFirstCollectionOrderer()
            .OrderTestCollections(new ITestCollection[]
            {
                alphaCollection,
                zetaCollection,
                liveBootstrapCollection
            })
            .ToArray();

        orderedCollections[0].Should().BeSameAs(liveBootstrapCollection);
        orderedCollections.Skip(1).Select(collection => collection.DisplayName)
            .Should()
            .Equal("AlphaCollection", "ZetaCollection");
    }

    [Fact]
    public void WpfApplicationFixtureBackedTests_ShouldUseWpfAndBootstrapIntegrationCollection()
    {
        var violatingTypes = typeof(WpfAndBootstrapIntegrationCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(WpfApplicationFixture)))
            .Where(type => !string.Equals(GetCollectionName(type), "WpfAndBootstrapIntegration", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        violatingTypes.Should().BeEmpty(
            "tests backed by WpfApplicationFixture share a single STA WPF Application and must remain in the WpfAndBootstrapIntegration serialization lane");
    }

    [Fact]
    public void BindingErrorCorrelationIntegrationTests_ShouldDisposeBindingTraceState()
    {
        typeof(BindingErrorCorrelationIntegrationTests).Should().BeAssignableTo<IDisposable>(
            "xUnit v2 only invokes this per-test cleanup automatically when the test class implements IDisposable");
    }

    [Fact]
    public void McpFixtureBackedE2eTests_ShouldUseMcpE2ECollectionUnlessExplicitlyIsolated()
    {
        var explicitlyIsolatedTypes = new[] { typeof(VisibilityDiagnosisE2eTests) };

        var violatingTypes = typeof(McpE2eCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(McpE2eFixture)))
            .Where(type => !explicitlyIsolatedTypes.Contains(type))
            .Where(type => !string.Equals(GetCollectionName(type), "McpE2E", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        violatingTypes.Should().BeEmpty(
            "tests backed by McpE2eFixture should stay on the shared McpE2E lane unless they are explicitly isolated onto a dedicated parallelizable collection");
    }

    [Theory]
    [InlineData(typeof(ActiveProcessWorkflowE2eTests))]
    [InlineData(typeof(McpToolSearchMetadataE2eTests))]
    [InlineData(typeof(ToolErrorContractE2eTests))]
    public void StdioServerOwningE2eTests_ShouldUseMcpE2ECollection(Type testClass)
    {
        GetCollectionName(testClass).Should().Be("McpE2E",
            "tests that start their own MCP server process should not run concurrently with shared McpE2E server lifecycles");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Integration/BootstrapInjectionTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/BootstrapEventTraceIntegrationTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/ConnectToolActiveProcessIntegrationTests.cs")]
    public void LiveBootstrapCleanup_ShouldDisposeProcessHandlesEvenAfterEarlyExit(string relativePath)
    {
        var content = File.ReadAllText(ReleasePackagingTestHarness.GetRepoFilePath(relativePath));

        content.Should().Contain("LiveTestProcessCleanup.StopAndDispose");
        content.Should().NotContain("_testApp != null && !_testApp.HasExited");
    }

    [Theory]
    [InlineData(
        "tests/WpfDevTools.Tests.Integration/BootstrapInjectionTests.cs",
        "SecureLiveSession.Create(")]
    [InlineData(
        "tests/WpfDevTools.Tests.Integration/E2E/NestedExecuteCommandPolicyE2eTests.cs",
        "ReleasePackagingTestHarness.GetRepoFilePath(\"tmp\")")]
    [InlineData(
        "tests/WpfDevTools.Tests.Integration/TestSupport/SecureLiveSession.cs",
        "ReleasePackagingTestHarness.GetRepoFilePath(\"tmp\")")]
    public void LiveSecurityTempRoots_ShouldUseRepoTmpAndRobustCleanup(
        string relativePath,
        string expectedTempRootMarker)
    {
        var content = File.ReadAllText(ReleasePackagingTestHarness.GetRepoFilePath(relativePath));

        content.Should().Contain(expectedTempRootMarker);
        content.Should().Contain("ReleasePackagingTestHarness.DeleteDirectory");
        content.Should().NotContain("Path.GetTempPath()");
        content.Should().NotContain("Directory.Delete(");
    }

    [Fact]
    public void LiveTestProcessCleanup_ShouldHandleExpectedKillRaceExceptionsAndStillDispose()
    {
        var content = File.ReadAllText(ReleasePackagingTestHarness.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/TestSupport/LiveTestProcessCleanup.cs"));

        content.Should().Contain("catch (InvalidOperationException)");
        content.Should().Contain("catch (System.ComponentModel.Win32Exception)");
        content.Should().Contain("process.Kill(entireProcessTree: true);");
        content.Should().Contain("if (!process.WaitForExit(timeoutMilliseconds))");
        content.Should().Contain("process.Dispose();");
    }

    [Fact]
    public void LiveTestProcessCleanupBehaviorTest_ShouldFailSafeDisposeSpawnedProcess()
    {
        var source = File.ReadAllText(ReleasePackagingTestHarness.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/IntegrationParallelizationCollectionContractTests.cs"));
        var start = source.LastIndexOf(
            "public void LiveTestProcessCleanup_StopAndDispose_ShouldKillRunningProcessAndDisposeHandle",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public void SecureLiveSession_Dispose_ShouldDeleteCertificateDirectory",
            start,
            StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var method = source[start..end];
        method.Should().Contain("try");
        method.Should().Contain("finally");
        method.Should().Contain("process.Kill(entireProcessTree: true)");
        method.Should().Contain("process.Dispose();");
    }

    [Fact]
    public void LiveTestProcessCleanup_StopAndDispose_ShouldKillRunningProcessAndDisposeHandle()
    {
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoLogo -NoProfile -Command \"Start-Sleep -Seconds 30\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process.Should().NotBeNull();

            LiveTestProcessCleanup.StopAndDispose(process, timeoutMilliseconds: 5000);
            var disposedProcess = process;
            process = null;

            Action readDisposedHandle = () => _ = disposedProcess!.Handle;
            readDisposedHandle.Should().Throw<Exception>(
                "live process cleanup must dispose or detach handles even after killing a running TestApp process")
                .Where(ex =>
                    ex.GetType() == typeof(ObjectDisposedException)
                    || ex.GetType() == typeof(InvalidOperationException));
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    [Fact]
    public void SecureLiveSession_Dispose_ShouldDeleteCertificateDirectory()
    {
        var session = SecureLiveSession.Create("WpfDevTools_SecureLiveSessionBehavior");
        var certificateDirectory = session.CertificateDirectoryForTesting;
        Directory.Exists(certificateDirectory).Should().BeTrue();

        session.Dispose();

        Directory.Exists(certificateDirectory).Should().BeFalse(
            "secure live sessions should delete their repo tmp certificate directory during cleanup");
        session.Dispose();
    }

    [Theory]
    [InlineData(typeof(BootstrapInjectionTests))]
    [InlineData(typeof(ConnectToolActiveProcessIntegrationTests))]
    [InlineData(typeof(BootstrapEventTraceIntegrationTests))]
    public void LiveBootstrapTests_ShouldUseWpfAndBootstrapIntegrationCollection(Type testClass)
    {
        GetCollectionName(testClass).Should().Be("WpfAndBootstrapIntegration");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Integration/BootstrapInjectionTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/BootstrapEventTraceIntegrationTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/ConnectAutoDiscoveryIntegrationTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Integration/ConnectToolActiveProcessIntegrationTests.cs")]
    public void LiveBootstrapConnectTests_ShouldUseSecureLiveSession(string relativePath)
    {
        var content = File.ReadAllText(ReleasePackagingTestHarness.GetRepoFilePath(relativePath));

        content.Should().Contain("SecureLiveSession.Create(",
            "live injected inspectors fail closed unless the test session passes authentication and TLS artifacts through bootstrap");
    }

    [Fact]
    public void WpfAndBootstrapIntegrationCollection_ShouldRemainParallelizableWithOtherCollections()
    {
        GetCollectionDefinitionAttribute(typeof(WpfAndBootstrapIntegrationCollection))
            .DisableParallelization
            .Should().BeFalse(
                "tests in the merged WPF/bootstrap collection are already serialized with each other by the shared collection, but the lane should remain eligible to run beside isolated integration collections");
    }

    [Fact]
    public void BootstrapDelayHookTests_ShouldScopeDelayEnvironmentToTargetProcess()
    {
        var sourceRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            sourceRoot,
            "tests",
            "WpfDevTools.Tests.Integration",
            "BootstrapInjectionTests.cs"));

        source.Should().NotContain("Environment.SetEnvironmentVariable",
            "Debug bootstrap delay hooks must not mutate process-wide environment while integration collections run in parallel");
        source.Should().NotContain("EnvironmentVariableScope",
            "delay hooks should be scoped to the launched TestApp process instead of the parent test process");
        source.Should().Contain("IReadOnlyDictionary<string, string>? environmentVariables = null");
        source.Should().Contain("environmentVariables: new Dictionary<string, string>");
    }

    [Fact]
    public void McpE2eCollection_ShouldDisableParallelization()
    {
        GetCollectionDefinitionAttribute(typeof(McpE2eCollection))
            .DisableParallelization
            .Should().BeTrue();
    }

    [Fact]
    public void VisibilityDiagnosisCollection_ShouldRemainParallelizable()
    {
        GetCollectionDefinitionAttribute(typeof(VisibilityDiagnosisE2eCollection))
            .DisableParallelization
            .Should().BeFalse(
                "the visibility E2E lane uses an isolated fixture and should remain eligible for collection-level parallel scheduling");
    }

    [Fact]
    public void VisibilityDiagnosisTests_ShouldUseVisibilityMcpE2ECollection()
    {
        GetCollectionName(typeof(VisibilityDiagnosisE2eTests))
            .Should().Be("VisibilityMcpE2E");
    }

    [Fact]
    public void VisibilityMcpE2ECollection_ShouldContainOnlyVisibilityDiagnosisTests()
    {
        var laneMembers = typeof(McpE2eCollection).Assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(type => UsesFixture(type, typeof(McpE2eFixture)))
            .Where(type => string.Equals(GetCollectionName(type), "VisibilityMcpE2E", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        laneMembers.Should().Equal(typeof(VisibilityDiagnosisE2eTests).FullName);
    }

    [Fact]
    public void IntegrationAssembly_ShouldNotEnableSignatureSkipWithModuleInitializer()
    {
        var sourceRoot = FindRepoRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "tests", "WpfDevTools.Tests.Integration"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("[ModuleInitializer]", StringComparison.Ordinal)
                    && text.Contains("Environment.SetEnvironmentVariable(\"WPFDEVTOOLS_SKIP_SIGNATURE_CHECK\"", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "Release signature skip must be scoped to explicit test validators or child-process environments, not enabled globally for the integration assembly");
    }

    [Fact]
    public void TrustedLocalReleaseSignatureSkip_ShouldNotMutateProcessEnvironment()
    {
        var sourceRoot = FindRepoRoot();
        var helperPath = Path.Combine(
            sourceRoot,
            "tests",
            "WpfDevTools.Tests.Integration",
            "TrustedLocalReleaseSignatureSkip.cs");

        File.ReadAllText(helperPath).Should().NotContain("SetEnvironmentVariable",
            "the live-injection Release signature skip helper must not toggle process-wide environment state while integration tests run in parallel");
    }

    private static bool IsConcreteTestClass(Type type)
        => type is { IsClass: true, IsAbstract: false }
            && !type.IsGenericTypeDefinition;

    private static bool UsesFixture(Type type, Type fixtureType)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == fixtureType);

    private static string? GetCollectionName(Type type)
    {
        var attributeData = type.GetCustomAttributesData()
            .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));

        attributeData.Should().NotBeNull($"{type.FullName} should declare an explicit xUnit collection");
        attributeData!.ConstructorArguments.Should().ContainSingle();
        return attributeData.ConstructorArguments[0].Value as string;
    }

    private static CollectionDefinitionAttribute GetCollectionDefinitionAttribute(Type type)
    {
        var attribute = type.GetCustomAttribute<CollectionDefinitionAttribute>();

        attribute.Should().NotBeNull($"{type.FullName} should declare a collection definition attribute");
        return attribute!;
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

    private sealed class StubTestCollection : LongLivedMarshalByRefObject, ITestCollection
    {
        public StubTestCollection()
            : this(string.Empty)
        {
        }

        public StubTestCollection(string displayName)
        {
            DisplayName = displayName;
        }

        public ITypeInfo CollectionDefinition => null!;

        public string DisplayName { get; }

        public ITestAssembly TestAssembly => null!;

        public Guid UniqueID { get; } = Guid.NewGuid();

        public void Deserialize(IXunitSerializationInfo info)
        {
        }

        public void Serialize(IXunitSerializationInfo info)
        {
        }
    }
}
